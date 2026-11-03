// Research design shown on the dashboard. Descriptions explain WHAT is investigated; outcomes always
// come from stored runs, never from this file.
import type { RunSummary } from '../../api/types'

export const researchQuestion =
  'How do different communication approaches between services affect the behaviour and design of a distributed ride-hailing system?'

export const subQuestions = [
  { id: 'SQ1', text: 'When is synchronous communication appropriate?', experiments: 'Experiment A' },
  { id: 'SQ2', text: 'When is asynchronous communication more appropriate?', experiments: 'Experiment A' },
  { id: 'SQ3', text: 'What are the practical differences between RabbitMQ and Kafka?', experiments: 'Experiment B' },
  { id: 'SQ4', text: 'What trade-offs do different communication approaches introduce?', experiments: 'Experiments A, B → C' },
]

export interface ScenarioInfo {
  key: string
  title: string
  short: string
  investigates: string
  watch: string[]
}

export const scenariosA: ScenarioInfo[] = [
  {
    key: 'A1-normal',
    title: 'A1 · Normal operation',
    short: 'Both services available',
    investigates: 'How the passenger-facing response and the time until a ride offer exists differ when Ride Service waits for Matching Service (REST) versus hands the work to a queue (RabbitMQ).',
    watch: ['HTTP response time vs. time until the offer is created', 'Pending state with RabbitMQ (accepted, not matched yet)', 'Duplicate offers'],
  },
  {
    key: 'A2-unavailable',
    title: 'A2 · Matching Service unavailable',
    short: 'Receiver down for a while',
    investigates: 'What happens to ride requests while the matching instance is unavailable, and after it comes back. Durability of the queue and messages is checked, not assumed.',
    watch: ['Accepted vs. failed requests during the outage', 'Messages retained in the queue (and their durability)', 'What happens after recovery: processed, lost, duplicated?'],
  },
  {
    key: 'A3-slow',
    title: 'A3 · Slow processing',
    short: 'Matching takes longer than the timeout',
    investigates: 'The consequences of slow processing: synchronous callers hit their timeout, asynchronous requests queue up and are completed later.',
    watch: ['REST timeouts and what happened to the work', 'Queue backlog and growing time to offer', 'Consequences of deferred processing for the passenger'],
  },
]

export const scenariosB: ScenarioInfo[] = [
  {
    key: 'B1-single',
    title: 'B1 · Single consumer',
    short: 'One service processes ride events',
    investigates: 'Delivery and processing of ride lifecycle events to one consumer: acknowledgements (RabbitMQ) vs. committed offsets (Kafka).',
    watch: ['All events delivered, duplicates, ordering per ride', 'Delivery latency', 'What the broker does after processing (delete vs. keep)'],
  },
  {
    key: 'B2-multiple',
    title: 'B2 · Multiple consumers',
    short: 'Two independent services need every event',
    investigates: 'How each mechanism delivers the same events to two independent services (separate queues vs. separate consumer groups) — and what happens when they share one subscription.',
    watch: ['Does each service get every event?', 'Shared subscription = competing consumers', 'Per-ride ordering across consumers'],
  },
  {
    key: 'B3-offline',
    title: 'B3 · Consumer temporarily offline',
    short: 'One consumer stops while events keep coming',
    investigates: 'What happens to events published while a consumer is offline, and how it recovers. The RabbitMQ queue can be durable or temporary.',
    watch: ['Backlog / consumer lag while offline', 'Missed events after reconnecting', 'Recovery time'],
  },
  {
    key: 'B4-replay',
    title: 'B4 · Replay historical events',
    short: 'A new service needs past events',
    investigates: 'Whether a consumer that starts after the events were published (and already processed by others) can still read them.',
    watch: ['Events available to the late consumer', 'Backlog of the original consumer after processing', 'Which configuration enables replay'],
  },
]

export interface MetricRow {
  key: string
  label: string
  unit?: 'ms' | '%' | ''
  hint?: string
}

export const metricRowsA: MetricRow[] = [
  { key: 'requests', label: 'Ride requests sent' },
  { key: 'accepted', label: 'Accepted by RideService (HTTP 2xx)' },
  { key: 'failedRequests', label: 'Failed requests', hint: 'HTTP error or no response' },
  { key: 'timeouts', label: 'Of which timeouts' },
  { key: 'offersCreated', label: 'Ride offers created (business outcome)' },
  { key: 'completionRate', label: 'Completion rate', unit: '%' },
  { key: 'pendingAtEnd', label: 'Still pending at the end' },
  { key: 'httpP50Ms', label: 'HTTP response p50', unit: 'ms', hint: 'passenger-facing' },
  { key: 'httpP95Ms', label: 'HTTP response p95', unit: 'ms' },
  { key: 'timeToOfferP50Ms', label: 'Request → offer p50', unit: 'ms', hint: 'business outcome' },
  { key: 'timeToOfferP95Ms', label: 'Request → offer p95', unit: 'ms' },
  { key: 'timeToOfferMaxMs', label: 'Request → offer max', unit: 'ms' },
  { key: 'publishConfirmP50Ms', label: 'RabbitMQ publish confirm p50', unit: 'ms', hint: 'not the matching time' },
  { key: 'maxQueueDepth', label: 'Max messages waiting in queue' },
  { key: 'lastOfferAfterRecoveryMs', label: 'Last offer after recovery', unit: 'ms' },
  { key: 'queueDurable', label: 'Queue durable (1 = yes)' },
  { key: 'messageDeliveryMode', label: 'Message delivery_mode (2 = persistent)' },
  { key: 'ridesWithDuplicateOffers', label: 'Rides with duplicate offers' },
  { key: 'matchingAbortedByCaller', label: 'Matching work aborted by caller' },
  { key: 'matchingFinishedRideClosed', label: 'Matching finished after ride was cancelled' },
]

export const metricRowsB: MetricRow[] = [
  { key: 'eventsPublished', label: 'Events published' },
  { key: 'consumerAUnique', label: 'Consumer A: unique events' },
  { key: 'consumerBUnique', label: 'Consumer B: unique events' },
  { key: 'lateConsumerUnique', label: 'Late consumer C: events received' },
  { key: 'consumerADuplicates', label: 'Consumer A: duplicates' },
  { key: 'consumerAOutOfOrder', label: 'Consumer A: out of order (per ride)' },
  { key: 'deliveryP50Ms', label: 'Delivery p50 (A)', unit: 'ms', hint: 'publish call → consumer' },
  { key: 'deliveryP95Ms', label: 'Delivery p95 (A)', unit: 'ms' },
  { key: 'publishAckP50Ms', label: 'Broker ack p50', unit: 'ms' },
  { key: 'throughputEventsPerSec', label: 'Throughput (A, events/s)' },
  { key: 'eventsSeenByAnyConsumer', label: 'Distinct events (shared subscription)' },
  { key: 'ridesSplitAcrossConsumers', label: 'Rides split across consumers' },
  { key: 'backlogWhileOffline', label: 'Backlog while B offline' },
  { key: 'eventsMissedByB', label: 'Events missed by B' },
  { key: 'catchUpMs', label: 'B recovery time', unit: 'ms', hint: 'restart → backlog processed' },
  { key: 'backlogOfAAfterProcessing', label: 'Backlog of A after processing' },
  { key: 'replayedEvents', label: 'Events replayed to late consumer' },
]

export function formatValue(value: number | null | undefined, unit?: MetricRow['unit']): string {
  if (value === null || value === undefined) return '—'
  if (unit === '%') return `${(value * 100).toFixed(0)}%`
  if (unit === 'ms') return value >= 1000 ? `${(value / 1000).toFixed(2)} s` : `${value.toFixed(value < 10 ? 1 : 0)} ms`
  return Number.isInteger(value) ? String(value) : value.toFixed(1)
}

/** Latest finished run per approach for a scenario (from a list ordered newest first). */
export function latestByApproach(runs: RunSummary[], scenarioKey: string): Record<string, RunSummary> {
  const result: Record<string, RunSummary> = {}
  for (const run of runs) {
    if (run.scenarioKey !== scenarioKey || ['Queued', 'Running'].includes(run.status)) continue
    result[run.mechanism] ??= run
  }
  return result
}

type Evidence = Record<string, Record<string, string | null>>

const r = (run: RunSummary | undefined, key: string) => run?.results?.[key] ?? null
const f = formatValue

/**
 * Measured evidence for the trade-off matrices, derived ONLY from stored runs. A cell is null when no
 * run measured it; the researcher then marks the criterion as expected / not tested.
 */
export function evidenceA(runs: RunSummary[]): Evidence {
  const a1 = latestByApproach(runs, 'A1-normal')
  const a2 = latestByApproach(runs, 'A2-unavailable')
  const a3 = latestByApproach(runs, 'A3-slow')
  const cell = (approach: string, build: () => string | null) => [approach, build()] as const
  const both = (build: (approach: string) => string | null) =>
    Object.fromEntries(['REST', 'RabbitMQ'].map((a) => cell(a, () => build(a))))
  return {
    'immediate-response': both((a) => a1[a] ? `A1: HTTP p50 ${f(r(a1[a], 'httpP50Ms'), 'ms')}; offer p50 ${f(r(a1[a], 'timeToOfferP50Ms'), 'ms')}` : null),
    'temporal-coupling': both((a) => a2[a] ? `A2: ${f(r(a2[a], 'accepted'))}/${f(r(a2[a], 'requests'))} requests accepted while Matching was down` : null),
    unavailability: both((a) => a2[a] ? `A2: ${f(r(a2[a], 'offersCreated'))}/${f(r(a2[a], 'requests'))} matched in the end` +
      (r(a2[a], 'lastOfferAfterRecoveryMs') !== null ? `, last ${f(r(a2[a], 'lastOfferAfterRecoveryMs'), 'ms')} after recovery` : '') : null),
    guarantees: both((a) => (a1[a] || a3[a]) ? `Duplicate offers: A1 ${f(r(a1[a], 'ridesWithDuplicateOffers'))}, A3 ${f(r(a3[a], 'ridesWithDuplicateOffers'))}` +
      (a === 'REST' && a3[a] ? `; A3 timeouts ${f(r(a3[a], 'timeouts'))}, aborted work ${f(r(a3[a], 'matchingAbortedByCaller'))}` : '') : null),
    'persistence-replay': both((a) => a === 'RabbitMQ' && a2[a] ? `A2: queue durable=${f(r(a2[a], 'queueDurable'))}, delivery_mode=${f(r(a2[a], 'messageDeliveryMode'))}, max ${f(r(a2[a], 'maxQueueDepth'))} waiting` :
      a === 'REST' && a2[a] ? `A2: failed requests were not stored (${f(r(a2[a], 'failedRequests'))} failed, 0 processed later)` : null),
    complexity: both(() => null),
    performance: both((a) => a1[a] ? `A1: request → offer p50 ${f(r(a1[a], 'timeToOfferP50Ms'), 'ms')}, p95 ${f(r(a1[a], 'timeToOfferP95Ms'), 'ms')}` : null),
    consistency: both((a) => a3[a] ? `A3: ${f(r(a3[a], 'offersCreated'))}/${f(r(a3[a], 'requests'))} offers; max time to offer ${f(r(a3[a], 'timeToOfferMaxMs'), 'ms')}` : null),
  }
}

export function evidenceB(runs: RunSummary[]): Evidence {
  const b1 = latestByApproach(runs, 'B1-single')
  const b2 = latestByApproach(runs, 'B2-multiple')
  const b3 = latestByApproach(runs, 'B3-offline')
  const b4 = latestByApproach(runs, 'B4-replay')
  const both = (build: (m: string) => string | null) => Object.fromEntries(['RabbitMQ', 'Kafka'].map((m) => [m, build(m)]))
  return {
    'immediate-response': both(() => null),
    'temporal-coupling': both((m) => b3[m] ? `B3: producer unaffected; ${f(r(b3[m], 'backlogWhileOffline'))} events waited for offline B` : null),
    unavailability: both((m) => b3[m] ? `B3 (${b3[m].parameters?.rabbitQueueMode ?? 'durable'}): B missed ${f(r(b3[m], 'eventsMissedByB'))}, recovered in ${f(r(b3[m], 'catchUpMs'), 'ms')}` : null),
    guarantees: both((m) => (b1[m] || b2[m]) ? `Duplicates ${f(r(b1[m], 'consumerADuplicates'))}, out of order ${f(r(b1[m], 'consumerAOutOfOrder'))}` +
      (r(b2[m], 'ridesSplitAcrossConsumers') !== null ? `; shared subscription split ${f(r(b2[m], 'ridesSplitAcrossConsumers'))} rides` : '') : null),
    'persistence-replay': both((m) => b4[m] ? `B4: late consumer received ${f(r(b4[m], 'replayedEvents'))}/${f(r(b4[m], 'eventsPublished'))} earlier events` : null),
    complexity: both(() => null),
    performance: both((m) => b1[m] ? `B1: delivery p50 ${f(r(b1[m], 'deliveryP50Ms'), 'ms')}, ${f(r(b1[m], 'throughputEventsPerSec'))} events/s` : null),
    consistency: both((m) => b3[m] ? `B3: events reached B up to ${f(b3[m].details?.consumers?.[1]?.latencyMaxMs ?? null, 'ms')} late` : null),
  }
}
