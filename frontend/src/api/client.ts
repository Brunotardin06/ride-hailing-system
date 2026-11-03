// One function per backend endpoint. The frontend talks directly to the service that owns the data
// (no API gateway): RideService for rides, DriverService for drivers/offers, AnalyticsService for
// the dashboard, MatchingService only for experiment controls.
import { serviceUrls } from '../config'
import { json, request } from './http'
import type {
  ActiveExperiment, ExperimentStarted, ScenarioFinding, TradeOffMatrix, TradeOffNote,
  BusinessSummary, ConsumerGroupStatus, Driver, LookupMode, MatchingConfig, MeasurementPoint, MetricDefinition,
  MetricSummary, Offer, Ride, RideEvent, RunSummary, StreamRead,
} from './types'

const ride = <T>(path: string, init?: RequestInit) => request<T>(serviceUrls.ride, path, init)
const driver = <T>(path: string, init?: RequestInit) => request<T>(serviceUrls.driver, path, init)
const analytics = <T>(path: string, init?: RequestInit) => request<T>(serviceUrls.analytics, path, init)
const matching = <T>(path: string, init?: RequestInit) => request<T>(serviceUrls.matching, path, init)

export const ridesApi = {
  request: (body: { passengerId: string; passengerName: string; pickup: string; destination: string }) =>
    ride<Ride>('/api/rides', json(body)),
  get: (rideId: string) => ride<Ride>(`/api/rides/${rideId}`),
  list: (filter: { passengerId?: string; driverId?: string; limit?: number }) => {
    const query = new URLSearchParams()
    if (filter.passengerId) query.set('passengerId', filter.passengerId)
    if (filter.driverId) query.set('driverId', filter.driverId)
    query.set('limit', String(filter.limit ?? 50))
    return ride<Ride[]>(`/api/rides?${query}`)
  },
  active: (filter: { passengerId?: string; driverId?: string }) =>
    ride<Ride | undefined>(`/api/rides/active?${new URLSearchParams(filter as Record<string, string>)}`),
  start: (rideId: string, driverId: string) => ride<Ride>(`/api/rides/${rideId}/start`, json({ driverId })),
  complete: (rideId: string, driverId: string) => ride<Ride>(`/api/rides/${rideId}/complete`, json({ driverId })),
  cancel: (rideId: string, passengerId: string) =>
    ride<Ride>(`/api/rides/${rideId}/cancel`, json({ passengerId, reason: 'Cancelled by passenger' })),
}

export const driversApi = {
  list: () => driver<Driver[]>('/api/drivers'),
  get: (driverId: string) => driver<Driver>(`/api/drivers/${driverId}`),
  setAvailability: (driverId: string, isAvailable: boolean) =>
    driver<Driver>(`/api/drivers/${driverId}/availability`, { method: 'PUT', body: JSON.stringify({ isAvailable }) }),
  pendingOffers: (driverId: string) => driver<Offer[]>(`/api/drivers/${driverId}/offers?status=Pending`),
  accept: (offerId: string, driverId: string) => driver<Offer>(`/api/offers/${offerId}/accept`, json({ driverId })),
  reject: (offerId: string, driverId: string) => driver<Offer>(`/api/offers/${offerId}/reject`, json({ driverId })),
  resetDemo: () => driver<void>('/api/dev/reset', { method: 'POST' }),
}

export const analyticsApi = {
  summary: () => analytics<BusinessSummary>('/api/analytics/summary?timelineMinutes=30'),
  events: (limit = 50) => analytics<RideEvent[]>(`/api/analytics/events?limit=${limit}`),
  stream: (limit = 25) => analytics<StreamRead>(`/api/analytics/kafka/stream?limit=${limit}`),
  consumerGroup: () => analytics<ConsumerGroupStatus>('/api/analytics/kafka/consumer-group'),
  metrics: (minutes?: number) => analytics<MetricSummary[]>(`/api/metrics/summary${minutes ? `?minutes=${minutes}` : ''}`),
  series: (m: { mechanism: string; kind: string; operation: string }, limit = 200) =>
    analytics<MeasurementPoint[]>(`/api/metrics/series?${new URLSearchParams({ ...m, limit: String(limit) })}`),
  definitions: () => analytics<MetricDefinition[]>('/api/metrics/definitions'),
  clearLiveMetrics: () => analytics<{ deleted: number }>('/api/metrics/measurements', { method: 'DELETE' }),
  runs: (filter: { experimentType?: string; scenarioKey?: string; groupId?: string; includeDetails?: boolean } = {}) => {
    const query = new URLSearchParams()
    for (const [k, v] of Object.entries(filter)) if (v !== undefined) query.set(k, String(v))
    return analytics<RunSummary[]>(`/api/experiments/runs?${query}`)
  },
  saveRunNotes: (id: string, qualitativeNotes: string) =>
    analytics<RunSummary>(`/api/experiments/runs/${id}/notes`, { method: 'PUT', body: JSON.stringify({ qualitativeNotes }) }),
  startExperimentA: (body: Record<string, unknown>) => analytics<ExperimentStarted>('/api/experiments/a', json(body)),
  startExperimentB: (body: Record<string, unknown>) => analytics<ExperimentStarted>('/api/experiments/b', json(body)),
  activeExperiment: () => analytics<ActiveExperiment | undefined>('/api/experiments/active'),
  findings: () => analytics<ScenarioFinding[]>('/api/findings'),
  saveFinding: (key: string, body: Partial<ScenarioFinding>) =>
    analytics<ScenarioFinding>(`/api/findings/${key}`, { method: 'PUT', body: JSON.stringify(body) }),
  tradeOffs: () => analytics<TradeOffMatrix[]>('/api/tradeoffs'),
  saveTradeOff: (body: { experiment: string; criterion: string; approach: string; text: string; evidenceType: string }) =>
    analytics<TradeOffNote>('/api/tradeoffs', { method: 'PUT', body: JSON.stringify(body) }),
  deleteRun: (id: string) => analytics<void>(`/api/experiments/runs/${id}`, { method: 'DELETE' }),
}

export const experimentsApi = {
  config: () => matching<MatchingConfig>('/api/experiments/config'),
  setLookupMode: (mode: LookupMode | null) =>
    matching<MatchingConfig>('/api/experiments/lookup-mode', { method: 'PUT', body: JSON.stringify({ mode }) }),
  runDriverLookupBenchmark: (body: { iterations: number; concurrency: number; rounds: number; warmup: number; label?: string }) =>
    matching<RunSummary[]>('/api/experiments/driver-lookup', json(body)),
  failNext: (count: number) => matching<MatchingConfig>('/api/experiments/fail-next', json({ count })),
}
