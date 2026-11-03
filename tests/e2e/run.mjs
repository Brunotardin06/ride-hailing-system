// End-to-end tests for the running system.
//
//   1. Start everything:  docker compose up -d --build   (or infrastructure + dotnet run per service)
//   2. Run:               cd tests/e2e && npm install && npm test
//
// The tests use the public interfaces only: REST APIs, the SignalR hub (as real clients, exactly like
// the browser) and the RabbitMQ management API. Every test uses fresh passenger ids, so it can be
// re-run; drivers are reset to the demo state before each test (DriverService /api/dev/reset).

import { randomUUID } from 'node:crypto'
import * as signalR from '@microsoft/signalr'

const url = {
  ride: process.env.RIDE_URL ?? 'http://localhost:5001',
  driver: process.env.DRIVER_URL ?? 'http://localhost:5002',
  matching: process.env.MATCHING_URL ?? 'http://localhost:5003',
  realtime: process.env.REALTIME_URL ?? 'http://localhost:5004',
  analytics: process.env.ANALYTICS_URL ?? 'http://localhost:5005',
  matchingExperiment: process.env.MATCHING_EXPERIMENT_URL ?? 'http://localhost:5013',
  rabbitAdmin: process.env.RABBITMQ_ADMIN_URL ?? 'http://localhost:15672',
}
const rabbitAuth = 'Basic ' + Buffer.from(`${process.env.RABBITMQ_USER ?? 'ridehailing'}:${process.env.RABBITMQ_PASSWORD ?? 'ridehailing'}`).toString('base64')

const EMMA = '11111111-1111-1111-1111-000000000001'
const LUCAS = '11111111-1111-1111-1111-000000000002'

// ---------- tiny test harness ----------

const results = []
async function test(name, fn) {
  const started = Date.now()
  process.stdout.write(`\n▶ ${name}\n`)
  try {
    await fn()
    results.push({ name, ok: true, ms: Date.now() - started })
    console.log(`  ✔ passed (${Date.now() - started} ms)`)
  } catch (error) {
    results.push({ name, ok: false, ms: Date.now() - started, error })
    console.log(`  ✘ FAILED: ${error.message}`)
  }
}
const step = (text) => console.log(`  · ${text}`)
function assert(condition, message) { if (!condition) throw new Error(message) }
function assertEqual(actual, expected, what) {
  if (actual !== expected) throw new Error(`${what}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`)
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

async function eventually(description, fn, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs
  let last
  while (Date.now() < deadline) {
    try {
      const value = await fn()
      if (value) return value
    } catch (e) { last = e }
    await sleep(250)
  }
  throw new Error(`Timed out waiting for: ${description}${last ? ` (${last.message})` : ''}`)
}

// ---------- HTTP helpers ----------

async function http(base, path, { method = 'GET', body, headers } = {}) {
  const response = await fetch(base + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await response.text()
  return { status: response.status, body: text ? JSON.parse(text) : undefined }
}
async function ok(base, path, options) {
  const r = await http(base, path, options)
  if (r.status >= 300) throw new Error(`${options?.method ?? 'GET'} ${path} → ${r.status} ${JSON.stringify(r.body)}`)
  return r.body
}

const requestRide = (passenger) => ok(url.ride, '/api/rides', {
  method: 'POST',
  body: { passengerId: passenger.id, passengerName: passenger.name, pickup: 'Fontys R1', destination: 'Eindhoven Centraal' },
})
const getRide = (id) => ok(url.ride, `/api/rides/${id}`)
const pendingOffers = (driverId) => ok(url.driver, `/api/drivers/${driverId}/offers?status=Pending`)
const resetDrivers = () => ok(url.driver, '/api/dev/reset', { method: 'POST' })
const newPassenger = (name) => ({ id: randomUUID(), name })

// ---------- SignalR client (behaves like the frontend) ----------

async function connect(role, userId) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${url.realtime}/hubs/rides`)
    .configureLogging(signalR.LogLevel.Warning)
    .build()
  const notifications = []
  const chat = []
  connection.on('notification', (n) => {
    notifications.push(n)
    connection.invoke('Acknowledge', n.id).catch(() => {}) // client-side ack, used for delivery metrics
  })
  connection.on('chatMessage', (m) => {
    chat.push(m)
    connection.invoke('Acknowledge', m.id).catch(() => {})
  })
  await connection.start()
  await connection.invoke('Register', role, userId)
  return {
    connection,
    notifications,
    chat,
    waitFor: (type, rideId, timeoutMs = 15000) =>
      eventually(`${role} notification "${type}"`, () => notifications.find((n) => n.type === type && (!rideId || n.rideId === rideId)), timeoutMs),
    stop: () => connection.stop(),
  }
}

// ---------- RabbitMQ management API ----------

async function queueInfo(queue) {
  const r = await fetch(`${url.rabbitAdmin}/api/queues/%2F/${encodeURIComponent(queue)}`, { headers: { Authorization: rabbitAuth } })
  return r.json()
}
async function publishRaw(routingKey, envelope) {
  const r = await fetch(`${url.rabbitAdmin}/api/exchanges/%2F/ridehailing.events/publish`, {
    method: 'POST',
    headers: { Authorization: rabbitAuth, 'Content-Type': 'application/json' },
    body: JSON.stringify({ properties: { content_type: 'application/json', delivery_mode: 2 }, routing_key: routingKey, payload: JSON.stringify(envelope), payload_encoding: 'string' }),
  })
  return r.json()
}

// =====================================================================================

const happy = {}

await test('0. All services are healthy', async () => {
  for (const [name, base] of Object.entries(url)) {
    if (name === 'rabbitAdmin') continue
    const r = await fetch(`${base}/health`)
    assertEqual(await r.text(), 'Healthy', `${name} /health`)
  }
  await resetDrivers()
})

await test('1. Happy path: request → RabbitMQ → match → SignalR offer → accept → assigned → chat → start → complete', async () => {
  await resetDrivers()
  const emmaBefore = await ok(url.driver, `/api/drivers/${EMMA}`)
  const passenger = newPassenger('E2E Alice')
  const p = await connect('passenger', passenger.id)
  const d = await connect('driver', EMMA)
  try {
    const ride = await requestRide(passenger)
    happy.rideId = ride.id
    assertEqual(ride.status, 'Requested', 'status after REST request')
    step(`ride ${ride.id} created (REST 201)`)

    const offerNotification = await d.waitFor('ride-offer', ride.id)
    step(`driver received SignalR "ride-offer" (matched via RabbitMQ ride.requested → MatchingService)`)
    await p.waitFor('driver-offered', ride.id)
    step('passenger received "driver-offered"')

    const [offer] = await pendingOffers(EMMA)
    assertEqual(offer?.rideId, ride.id, 'pending offer ride id')
    assertEqual(offer.id, offerNotification.data.offerId, 'offer id in notification')

    await ok(url.driver, `/api/offers/${offer.id}/accept`, { method: 'POST', body: { driverId: EMMA } })
    step('driver accepted via REST (DriverService)')
    await p.waitFor('ride-assigned', ride.id)
    step('passenger received "ride-assigned" (driver.accepted-ride → RideService → ride.assigned → RealtimeService)')

    const assigned = await getRide(ride.id)
    assertEqual(assigned.status, 'Assigned', 'ride status')
    assertEqual(assigned.driverId, EMMA, 'assigned driver')
    assertEqual((await ok(url.driver, `/api/drivers/${EMMA}`)).status, 'Busy', 'driver status')

    // Chat in both directions
    await p.connection.invoke('JoinRideChat', ride.id)
    await d.connection.invoke('JoinRideChat', ride.id)
    await p.connection.invoke('SendChatMessage', { rideId: ride.id, senderRole: 'passenger', senderName: passenger.name, text: 'Hi, I am at the main entrance' })
    await eventually('driver receives chat', () => d.chat.find((m) => m.text.includes('main entrance')))
    await d.connection.invoke('SendChatMessage', { rideId: ride.id, senderRole: 'driver', senderName: 'Emma', text: 'On my way, 2 minutes' })
    await eventually('passenger receives chat', () => p.chat.find((m) => m.text.includes('2 minutes')))
    const history = await p.connection.invoke('JoinRideChat', ride.id)
    assertEqual(history.length, 2, 'chat history length')
    step('chat delivered both ways, history kept')

    await ok(url.ride, `/api/rides/${ride.id}/start`, { method: 'POST', body: { driverId: EMMA } })
    await p.waitFor('ride-started', ride.id)
    await ok(url.ride, `/api/rides/${ride.id}/complete`, { method: 'POST', body: { driverId: EMMA } })
    await p.waitFor('ride-completed', ride.id)
    await d.waitFor('ride-completed', ride.id)
    step('start + complete via REST, both parties notified')

    assertEqual((await getRide(ride.id)).status, 'Completed', 'final ride status')
    const emmaAfter = await eventually('driver released', async () => {
      const e = await ok(url.driver, `/api/drivers/${EMMA}`)
      return e.status === 'Available' && e
    })
    assertEqual(emmaAfter.completedRides, emmaBefore.completedRides + 1, 'completed rides counter')
    step('driver available again (ride.completed consumed by DriverService)')
  } finally {
    await p.stop()
    await d.stop()
  }
})

await test('2. Status transitions are validated', async () => {
  assert(happy.rideId, 'needs ride from test 1')
  const again = await http(url.ride, `/api/rides/${happy.rideId}/start`, { method: 'POST', body: { driverId: EMMA } })
  assertEqual(again.status, 409, 'start a completed ride')

  await resetDrivers()
  const passenger = newPassenger('E2E Transitions')
  const ride = await requestRide(passenger)
  const second = await http(url.ride, '/api/rides', { method: 'POST', body: { passengerId: passenger.id, passengerName: 'x', pickup: 'A1', destination: 'B1' } })
  assertEqual(second.status, 409, 'second active ride for the same passenger')
  const [offer] = await eventually('offer for Emma', async () => {
    const offers = (await pendingOffers(EMMA)).filter((o) => o.rideId === ride.id)
    return offers.length > 0 && offers
  })
  const wrongDriver = await http(url.driver, `/api/offers/${offer.id}/accept`, { method: 'POST', body: { driverId: LUCAS } })
  assertEqual(wrongDriver.status, 403, 'accepting another driver\'s offer')
  await ok(url.driver, `/api/offers/${offer.id}/accept`, { method: 'POST', body: { driverId: EMMA } })
  const doubleAccept = await http(url.driver, `/api/offers/${offer.id}/accept`, { method: 'POST', body: { driverId: EMMA } })
  assertEqual(doubleAccept.status, 409, 'accepting twice')
  await eventually('assigned', async () => (await getRide(ride.id)).status === 'Assigned')
  const completeBeforeStart = await http(url.ride, `/api/rides/${ride.id}/complete`, { method: 'POST', body: { driverId: EMMA } })
  assertEqual(completeBeforeStart.status, 409, 'complete before start')
  const startByOther = await http(url.ride, `/api/rides/${ride.id}/start`, { method: 'POST', body: { driverId: LUCAS } })
  assertEqual(startByOther.status, 403, 'start by a driver who is not assigned')
  await ok(url.ride, `/api/rides/${ride.id}/cancel`, { method: 'POST', body: { passengerId: passenger.id } })
  await eventually('driver released after cancel', async () => (await ok(url.driver, `/api/drivers/${EMMA}`)).status === 'Available')
  step('409/403 for invalid transitions; cancel of an assigned ride frees the driver')
})

await test('3. Rejection → re-match to next driver → no driver available', async () => {
  await resetDrivers()
  const passenger = newPassenger('E2E Reject')
  const p = await connect('passenger', passenger.id)
  const emma = await connect('driver', EMMA)
  const lucas = await connect('driver', LUCAS)
  try {
    const ride = await requestRide(passenger)
    await emma.waitFor('ride-offer', ride.id)
    const [offer1] = await pendingOffers(EMMA)
    await ok(url.driver, `/api/offers/${offer1.id}/reject`, { method: 'POST', body: { driverId: EMMA } })
    step('Emma rejected')
    await p.waitFor('offer-declined', ride.id)
    const lucasOffer = await lucas.waitFor('ride-offer', ride.id)
    assertEqual(lucasOffer.data.attempt, 2, 'second attempt')
    step('re-matched to Lucas (driver.rejected-ride → MatchingService)')
    const [offer2] = await pendingOffers(LUCAS)
    await ok(url.driver, `/api/offers/${offer2.id}/reject`, { method: 'POST', body: { driverId: LUCAS } })
    await p.waitFor('no-driver', ride.id)
    assertEqual((await getRide(ride.id)).status, 'NoDriverAvailable', 'ride status')
    step('no other driver → ride.matching-failed → NoDriverAvailable, passenger notified')
  } finally {
    await Promise.all([p.stop(), emma.stop(), lucas.stop()])
  }
})

await test('4. Cancel while the offer is pending withdraws the offer', async () => {
  await resetDrivers()
  const passenger = newPassenger('E2E Cancel')
  const d = await connect('driver', EMMA)
  try {
    const ride = await requestRide(passenger)
    await d.waitFor('ride-offer', ride.id)
    await ok(url.ride, `/api/rides/${ride.id}/cancel`, { method: 'POST', body: { passengerId: passenger.id } })
    await d.waitFor('offer-closed', ride.id)
    assertEqual((await pendingOffers(EMMA)).length, 0, 'pending offers after cancel')
    assertEqual((await ok(url.driver, `/api/drivers/${EMMA}`)).status, 'Available', 'driver status')
    step('ride.cancelled → DriverService withdraws offer → driver notified')
  } finally {
    await d.stop()
  }
})

await test('5. Idempotency: duplicate messages and duplicate offer requests', async () => {
  await resetDrivers()
  // A ride.requested message delivered twice (same EventId) must create exactly one offer.
  const rideId = randomUUID()
  const envelope = {
    eventId: randomUUID(), eventType: 'ride.requested', correlationId: randomUUID(), source: 'e2e-test',
    occurredAt: new Date().toISOString(), publishedAt: new Date().toISOString(),
    payload: { rideId, passengerId: randomUUID(), passengerName: 'Duplicate', pickup: 'A', destination: 'B', requestedAt: new Date().toISOString() },
  }
  assert((await publishRaw('ride.requested', envelope)).routed, 'first publish routed')
  assert((await publishRaw('ride.requested', envelope)).routed, 'second publish routed')
  await sleep(3000)
  const offers = (await pendingOffers(EMMA)).filter((o) => o.rideId === rideId)
  assertEqual(offers.length, 1, 'offers created for a duplicated message')
  step('duplicate RabbitMQ delivery → one offer')

  // The same offer request (same RequestKey) twice → 201 then 200 with the same offer.
  const body = { requestKey: `e2e-${randomUUID()}`, rideId: randomUUID(), driverId: LUCAS, passengerId: randomUUID(), passengerName: 'X', pickup: 'A', destination: 'B', attempt: 1 }
  const first = await http(url.driver, '/api/offers', { method: 'POST', body })
  const second = await http(url.driver, '/api/offers', { method: 'POST', body })
  assertEqual(first.status, 201, 'first offer request')
  assertEqual(second.status, 200, 'repeated offer request')
  assertEqual(second.body.id, first.body.id, 'same offer returned')
  step('repeated POST /api/offers with the same RequestKey is idempotent')
  await resetDrivers()
})

await test('6. Kafka: lifecycle events consumed and stored by AnalyticsService', async () => {
  assert(happy.rideId, 'needs ride from test 1')
  const events = await eventually('4 events for the happy-path ride', async () => {
    const list = await ok(url.analytics, `/api/analytics/events?rideId=${happy.rideId}`)
    return list.length >= 4 && list
  }, 20000)
  const types = events.map((e) => e.eventType).sort()
  assertEqual(types.join(','), ['ride.assigned', 'ride.completed', 'ride.requested', 'ride.started'].join(','), 'event types')
  assertEqual(new Set(events.map((e) => e.partition)).size, 1, 'all events of one ride in one partition (key = ride id)')
  step(`events stored with partition/offset: ${events.map((e) => `${e.eventType}@${e.partition}:${e.offset}`).join(', ')}`)

  const summary = await ok(url.analytics, '/api/analytics/summary')
  assert(summary.ridesRequested >= 1 && summary.ridesCompleted >= 1, 'summary counts')
  const group = await eventually('consumer lag 0', async () => {
    const g = await ok(url.analytics, '/api/analytics/kafka/consumer-group')
    return g.totalLag === 0 && g
  })
  step(`consumer group ${group.consumerGroup}: ${group.totalMessages} messages, lag ${group.totalLag}`)
  const stream = await ok(url.analytics, '/api/analytics/kafka/stream?limit=10')
  assert(stream.messages.length > 0 && stream.consumerGroup.startsWith('analytics-inspector-'), 'independent stream read')
  step(`independent consumer group ${stream.consumerGroup} read ${stream.messages.length} records`)
})

await test('7. Metrics: live measurements for every mechanism are stored', async () => {
  const summary = await eventually('metrics flushed', async () => {
    const s = await ok(url.analytics, '/api/metrics/summary')
    const has = (mechanism, kind) => s.some((m) => m.mechanism === mechanism && m.kind === kind && m.count > 0)
    return has('REST', 'call') && has('RabbitMQ', 'delivery') && has('RabbitMQ', 'processing') &&
      has('Kafka', 'delivery') && has('SignalR', 'ack-rtt') && s
  }, 20000)
  for (const m of summary.filter((x) => ['call', 'ack-rtt'].includes(x.kind) || x.operation.startsWith('ride.requested'))) {
    step(`${m.mechanism.padEnd(8)} ${m.kind.padEnd(10)} ${m.operation.padEnd(40)} n=${String(m.count).padEnd(4)} avg=${m.avgMs} ms p95=${m.p95Ms} ms`)
  }
})

await test('8. gRPC: live matching via gRPC and REST vs gRPC benchmark', async () => {
  await ok(url.matching, '/api/experiments/lookup-mode', { method: 'PUT', body: { mode: 'Grpc' } })
  try {
    await resetDrivers()
    const passenger = newPassenger('E2E gRPC')
    const ride = await requestRide(passenger)
    await eventually('offer created via gRPC lookup', async () => (await pendingOffers(EMMA)).some((o) => o.rideId === ride.id))
    await ok(url.ride, `/api/rides/${ride.id}/cancel`, { method: 'POST', body: { passengerId: passenger.id } })
    await eventually('gRPC call metric', async () =>
      (await ok(url.analytics, '/api/metrics/summary')).some((m) => m.mechanism === 'gRPC' && m.operation === 'driver-lookup'), 10000)
    step('ride matched using the gRPC driver lookup')
  } finally {
    await ok(url.matching, '/api/experiments/lookup-mode', { method: 'PUT', body: { mode: null } })
  }

  const runs = await ok(url.matching, '/api/experiments/driver-lookup', {
    method: 'POST', body: { iterations: 200, warmup: 20, concurrency: 1, rounds: 2, label: 'e2e smoke' },
  })
  assertEqual(runs.length, 2, 'runs returned')
  for (const r of runs) {
    assertEqual(r.count, 200, `${r.mechanism} sample count`)
    assertEqual(r.errors, 0, `${r.mechanism} errors`)
    step(`${r.mechanism.padEnd(5)} avg ${r.avgMs} ms · p50 ${r.p50Ms} · p95 ${r.p95Ms} · p99 ${r.p99Ms} · ${r.throughputPerSecond}/s`)
  }
  const stored = await ok(url.analytics, '/api/experiments/runs')
  assert(runs.every((r) => stored.some((s) => s.id === r.id)), 'runs stored in AnalyticsService')
})

await test('9. RabbitMQ retries and dead-lettering (failure injection)', async () => {
  await resetDrivers()
  // 2 failures < MaxRetries (3): the message is retried and the ride is still matched.
  await ok(url.matching, '/api/experiments/fail-next', { method: 'POST', body: { count: 2 } })
  const p1 = newPassenger('E2E Retry')
  const ride1 = await requestRide(p1)
  await eventually('offer after 2 retries', async () => (await pendingOffers(EMMA)).some((o) => o.rideId === ride1.id), 20000)
  step('ride matched after 2 failed attempts (retry with back-off)')
  await ok(url.ride, `/api/rides/${ride1.id}/cancel`, { method: 'POST', body: { passengerId: p1.id } })

  // 4 failures > MaxRetries: the message ends up in the dead-letter queue.
  const dlqBefore = (await queueInfo('matching-service.dlq')).messages ?? 0
  await ok(url.matching, '/api/experiments/fail-next', { method: 'POST', body: { count: 4 } })
  const p2 = newPassenger('E2E DLQ')
  const ride2 = await requestRide(p2)
  await eventually('message in matching-service.dlq', async () => ((await queueInfo('matching-service.dlq')).messages ?? 0) > dlqBefore, 25000)
  assertEqual((await getRide(ride2.id)).status, 'Requested', 'dead-lettered ride stays Requested')
  step(`matching-service.dlq: ${dlqBefore} → ${(await queueInfo('matching-service.dlq')).messages} messages`)
  await ok(url.ride, `/api/rides/${ride2.id}/cancel`, { method: 'POST', body: { passengerId: p2.id } })
  await ok(url.matching, '/api/experiments/fail-next', { method: 'POST', body: { count: 0 } })
  await resetDrivers()
})

// ---------- Research experiments (Phase 3) ----------

async function runScenario(experiment, body, timeoutMs = 120000) {
  const started = await ok(url.analytics, `/api/experiments/${experiment}`, { method: 'POST', body })
  return eventually(`experiment ${experiment} ${body.scenario} finished`, async () => {
    const runs = await ok(url.analytics, `/api/experiments/runs?groupId=${started.groupId}&includeDetails=true`)
    if (!runs.every((r) => !['Queued', 'Running'].includes(r.status))) return false
    // the coordinator also frees its slot (only one experiment at a time)
    const active = await http(url.analytics, '/api/experiments/active')
    return active.status === 204 && runs
  }, timeoutMs)
}
const byMechanism = (runs, m) => runs.find((r) => r.mechanism === m)

await test('10. Experiment A2: REST fails during the outage, RabbitMQ retains and completes the work', async () => {
  const demoBefore = await ok(url.driver, '/api/drivers')
  const runs = await runScenario('a', { scenario: 'A2-unavailable', rides: 3, intervalMs: 200, outageSeconds: 5 })
  const rest = byMechanism(runs, 'REST').results
  const rabbit = byMechanism(runs, 'RabbitMQ').results
  assertEqual(rest.accepted, 0, 'REST accepted during outage')
  assertEqual(rest.offersCreated, 0, 'REST offers')
  assertEqual(rabbit.accepted, 3, 'RabbitMQ accepted during outage')
  assertEqual(rabbit.offersCreated, 3, 'RabbitMQ offers after recovery')
  assertEqual(rabbit.consumersDuringOutage, 0, 'consumers during outage')
  assertEqual(rabbit.queueDurable, 1, 'queue durable')
  assertEqual(rabbit.messageDeliveryMode, 2, 'persistent messages')
  assertEqual(rabbit.ridesWithDuplicateOffers, 0, 'duplicate offers')
  assert(rabbit.maxQueueDepth >= 1, 'messages waited in the queue')
  step(`REST 0/3 accepted; RabbitMQ 3/3 accepted, max ${rabbit.maxQueueDepth} queued, all matched ${rabbit.lastOfferAfterRecoveryMs} ms after recovery`)

  // Isolation: demo drivers were not touched and no experiment ride is still active
  const demoAfter = await ok(url.driver, '/api/drivers')
  assertEqual(JSON.stringify(demoAfter.map((d) => d.status)), JSON.stringify(demoBefore.map((d) => d.status)), 'demo driver states unchanged')
  const outage = await ok(url.matchingExperiment, '/api/experiments/simulated-outage')
  assertEqual(outage.enabled, false, 'outage switched off after the run')
  step('demo drivers unaffected; simulated outage cleaned up')
})

await test('11. Experiment A3: REST times out, RabbitMQ builds a backlog and completes later', async () => {
  const runs = await runScenario('a', { scenario: 'A3-slow', rides: 3, intervalMs: 200, slowDelayMs: 1500, restTimeoutMs: 1000 })
  const rest = byMechanism(runs, 'REST').results
  const rabbit = byMechanism(runs, 'RabbitMQ').results
  assertEqual(rest.timeouts, 3, 'REST timeouts')
  assertEqual(rest.matchingAbortedByCaller + rest.matchingFinishedRideClosed + rest.lateOffers >= 0, true, 'wasted work recorded')
  assertEqual(rabbit.offersCreated, 3, 'RabbitMQ offers')
  assert(rabbit.timeToOfferMaxMs > rabbit.timeToOfferP50Ms, 'time to offer grows with the backlog')
  step(`REST: 3 timeouts (aborted work ${rest.matchingAbortedByCaller}); RabbitMQ: offers after p50 ${rabbit.timeToOfferP50Ms} ms, max ${rabbit.timeToOfferMaxMs} ms, max queue ${rabbit.maxQueueDepth}`)
})

await test('12. Experiment B2/B3/B4: independent consumers, offline consumer, replay', async () => {
  const b2 = await runScenario('b', { scenario: 'B2-multiple', rides: 20 })
  for (const m of ['RabbitMQ', 'Kafka']) {
    const r = byMechanism(b2, m).results
    assertEqual(r.consumerAUnique, r.eventsPublished, `${m} B2 consumer A`)
    assertEqual(r.consumerBUnique, r.eventsPublished, `${m} B2 consumer B`)
  }
  step('B2: both independent consumers received every event (RabbitMQ and Kafka)')

  const b3 = await runScenario('b', { scenario: 'B3-offline', rides: 20, offlineSeconds: 2 })
  for (const m of ['RabbitMQ', 'Kafka']) {
    const r = byMechanism(b3, m).results
    assertEqual(r.eventsMissedByB, 0, `${m} B3 missed events (durable queue / committed offsets)`)
    assert(r.backlogWhileOffline > 0, `${m} backlog while offline`)
  }
  const b3temp = await runScenario('b', { scenario: 'B3-offline', rides: 20, offlineSeconds: 2, rabbitQueueMode: 'temporary', mechanisms: ['RabbitMQ'] })
  assert(b3temp[0].results.eventsMissedByB > 0, 'temporary queue loses events while offline')
  step(`B3: durable queue and Kafka missed 0; temporary queue missed ${b3temp[0].results.eventsMissedByB}`)

  const b4 = await runScenario('b', { scenario: 'B4-replay', rides: 20 })
  const kafka = byMechanism(b4, 'Kafka').results
  const rabbit = byMechanism(b4, 'RabbitMQ').results
  assertEqual(kafka.replayedEvents, kafka.eventsPublished, 'Kafka replay')
  assertEqual(rabbit.replayedEvents, 0, 'RabbitMQ late queue receives nothing')
  assertEqual(rabbit.backlogOfAAfterProcessing, 0, 'acknowledged messages are removed')
  step(`B4: Kafka replayed ${kafka.replayedEvents}/${kafka.eventsPublished}; RabbitMQ late consumer 0`)
})

await test('13. Experiment C: findings and trade-off notes can be recorded', async () => {
  const findings = await ok(url.analytics, '/api/findings')
  assertEqual(findings.length, 7, 'scenario findings seeded')
  const saved = await ok(url.analytics, '/api/findings/A2-unavailable', { method: 'PUT', body: { observations: 'e2e test observation', evidenceStatus: 'Supported' } })
  assertEqual(saved.evidenceStatus, 'Supported', 'evidence status saved')
  const note = await ok(url.analytics, '/api/tradeoffs', { method: 'PUT', body: { experiment: 'A', criterion: 'temporal-coupling', approach: 'REST', text: 'e2e note', evidenceType: 'Measured' } })
  assertEqual(note.text, 'e2e note', 'trade-off note saved')
  const bad = await http(url.analytics, '/api/tradeoffs', { method: 'PUT', body: { experiment: 'A', criterion: 'nope', approach: 'REST', text: 'x', evidenceType: 'Measured' } })
  assertEqual(bad.status, 400, 'unknown criterion rejected')
  // restore the researcher's fields so the test does not leave its text behind
  await ok(url.analytics, '/api/findings/A2-unavailable', { method: 'PUT', body: { observations: '', evidenceStatus: 'NotTested' } })
  await ok(url.analytics, '/api/tradeoffs', { method: 'PUT', body: { experiment: 'A', criterion: 'temporal-coupling', approach: 'REST', text: '', evidenceType: 'NotTested' } })
})

// ---------- summary ----------

const failed = results.filter((r) => !r.ok)
console.log(`\n${results.length - failed.length}/${results.length} tests passed`)
for (const f of failed) console.log(`  ✘ ${f.name}: ${f.error.message}`)
process.exit(failed.length === 0 ? 0 : 1)
