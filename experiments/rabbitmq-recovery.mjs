// RabbitMQ failure/recovery experiment.
//
//   node experiments/rabbitmq-recovery.mjs [--rides 5] [--outage 10]
//
// 1. Makes all demo drivers available.
// 2. Stops the MatchingService container (the consumer of ride.requested).
// 3. Requests N rides via REST: RideService still accepts them, the messages wait in the
//    durable "matching-service" queue (visible in the RabbitMQ management UI).
// 4. After the outage, starts MatchingService again and measures how long until every ride has an offer.
// 5. Stores the per-ride results in AnalyticsService as an experiment run and cleans up.
//
// Measured per ride ("recovery" kind): start = POST /api/rides returned 201,
// end = the ride's offer is visible via DriverService's REST API (polled every 100 ms, so ±100 ms).
// Requires the full stack running in Docker (docker compose up -d --build).

import { spawnSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const args = Object.fromEntries(process.argv.slice(2).join(' ').split('--').filter(Boolean).map((a) => a.trim().split(' ')))
const rideCount = Number(args.rides ?? 5)
const outageSeconds = Number(args.outage ?? 10)

const url = { ride: 'http://localhost:5001', driver: 'http://localhost:5002', analytics: 'http://localhost:5005', rabbit: 'http://localhost:15672' }
const rabbitAuth = 'Basic ' + Buffer.from('ridehailing:ridehailing').toString('base64')
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const here = dirname(fileURLToPath(import.meta.url))

async function call(base, path, method = 'GET', body) {
  const r = await fetch(base + path, { method, headers: { 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) })
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${await r.text()}`)
  const text = await r.text()
  return text ? JSON.parse(text) : undefined
}
const queueStats = async () => {
  const r = await fetch(`${url.rabbit}/api/queues/%2F/matching-service`, { headers: { Authorization: rabbitAuth } })
  return r.json()
}
const compose = (...a) => spawnSync('docker', ['compose', ...a], { stdio: 'inherit' })

console.log(`RabbitMQ recovery experiment: ${rideCount} rides, MatchingService down for ${outageSeconds}s\n`)

await call(url.driver, '/api/dev/reset', 'POST')
const drivers = await call(url.driver, '/api/drivers')
for (const d of drivers.filter((d) => d.status === 'Offline')) await call(url.driver, `/api/drivers/${d.id}/availability`, 'PUT', { isAvailable: true })
console.log(`· ${drivers.length} drivers available`)

compose('stop', 'matching-service')
const outageStart = Date.now()
console.log('· matching-service stopped')

const rides = []
for (let i = 0; i < rideCount; i++) {
  const passenger = { id: randomUUID(), name: `Recovery ${i + 1}` }
  const ride = await call(url.ride, '/api/rides', 'POST', { passengerId: passenger.id, passengerName: passenger.name, pickup: 'Strijp-S', destination: 'Eindhoven Airport' })
  rides.push({ ride, passenger, requestedAt: Date.now() })
}
// The management API's statistics lag several seconds behind, so poll until they show the stopped
// consumer and the queued messages (at most 15 s).
let queued = 0
for (const until = Date.now() + 15000; Date.now() < until; await sleep(500)) {
  const q = await queueStats()
  queued = Math.max(queued, q.messages ?? 0)
  if (q.consumers === 0 && queued >= rideCount) break
}
console.log(`· ${rideCount} rides requested (all returned 201); messages waiting in queue "matching-service": ${queued}`)

await sleep(Math.max(0, outageSeconds * 1000 - (Date.now() - outageStart)))
compose('start', 'matching-service')
const restartedAt = Date.now()
console.log('· matching-service started again')

const offeredAt = new Map()
const deadline = Date.now() + 60_000
while (offeredAt.size < rides.length && Date.now() < deadline) {
  const offers = (await Promise.all(drivers.map((d) => call(url.driver, `/api/drivers/${d.id}/offers?status=Pending`)))).flat()
  for (const o of offers) if (!offeredAt.has(o.rideId)) offeredAt.set(o.rideId, Date.now())
  await sleep(100)
}

const samples = rides.map((r) => {
  const at = offeredAt.get(r.ride.id)
  return at ? { durationMs: at - r.requestedAt, success: true } : { durationMs: Date.now() - r.requestedAt, success: false, error: 'No offer within 60 s after restart' }
})
const drained = Math.max(...[...offeredAt.values()]) - restartedAt
const lost = samples.filter((s) => !s.success).length
console.log(`\nResults: ${rides.length - lost}/${rides.length} rides matched, ${lost} lost; queue drained ${drained} ms after restart`)
samples.forEach((s, i) => console.log(`  ride ${i + 1}: request → offer ${s.durationMs} ms${s.success ? '' : ' (FAILED)'}`))

const run = await call(url.analytics, '/api/experiments/runs', 'POST', {
  name: `RabbitMQ recovery · ${outageSeconds}s outage · ${rideCount} rides`,
  mechanism: 'RabbitMQ',
  kind: 'recovery',
  operation: 'ride request → offer (matching-service down)',
  scenario: `MatchingService stopped for ${outageSeconds}s while ${rideCount} rides were requested`,
  tool: 'experiments/rabbitmq-recovery.mjs',
  startedAt: new Date(outageStart).toISOString(),
  finishedAt: new Date().toISOString(),
  wallTimeMs: Date.now() - outageStart,
  parameters: { rides: String(rideCount), outageSeconds: String(outageSeconds), queuedDuringOutage: String(queued), drainAfterRestartMs: String(drained), lostMessages: String(lost) },
  samples,
  notes: 'start = POST /api/rides returned; end = offer visible via DriverService (100 ms polling). Includes the outage itself.',
})
writeFileSync(join(here, 'results', 'rabbitmq-recovery.json'), JSON.stringify({ run, samples }, null, 2))
console.log(`\nStored as experiment run ${run.id} (avg ${run.avgMs} ms, p95 ${run.p95Ms} ms)`)

for (const r of rides) await call(url.ride, `/api/rides/${r.ride.id}/cancel`, 'POST', { passengerId: r.passenger.id }).catch(() => {})
await sleep(1500)
await call(url.driver, '/api/dev/reset', 'POST')
console.log('· cleaned up (rides cancelled, drivers reset)')
