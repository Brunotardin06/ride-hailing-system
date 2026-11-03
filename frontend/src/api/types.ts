// Response types of the backend REST APIs (mirrors the C# DTOs).

export type RideStatus = 'Requested' | 'Assigned' | 'InProgress' | 'Completed' | 'Cancelled' | 'NoDriverAvailable'

export interface Ride {
  id: string
  correlationId: string
  passengerId: string
  passengerName: string
  pickup: string
  destination: string
  status: RideStatus
  driverId: string | null
  driverName: string | null
  vehicle: string | null
  licensePlate: string | null
  requestedAt: string
  assignedAt: string | null
  startedAt: string | null
  completedAt: string | null
  cancelledAt: string | null
  statusReason: string | null
  /** How matching was requested (Experiment A): asynchronous via RabbitMQ or synchronous via REST. */
  matchingMode: 'RabbitMq' | 'Rest'
  experimentRunId: string | null
  /** When matching produced the first offer; null while matching is pending. */
  offerCreatedAt: string | null
  offeredDriverName: string | null
}

export type DriverStatus = 'Offline' | 'Available' | 'Offered' | 'Busy'

export interface Driver {
  id: string
  name: string
  vehicle: string
  licensePlate: string
  rating: number
  status: DriverStatus
  availableSince: string | null
  currentRideId: string | null
  completedRides: number
}

export type OfferStatus = 'Pending' | 'Accepted' | 'Rejected' | 'Expired' | 'Cancelled'

export interface Offer {
  id: string
  rideId: string
  driverId: string
  passengerId: string
  passengerName: string
  pickup: string
  destination: string
  attempt: number
  status: OfferStatus
  createdAt: string
  expiresAt: string
  respondedAt: string | null
}

// ---------- Realtime (SignalR) ----------

export interface Notification {
  id: string
  type: string
  rideId: string
  title: string
  message: string
  data: unknown
  sentAt: string
}

export interface ChatMessage {
  id: string
  rideId: string
  senderRole: 'passenger' | 'driver'
  senderName: string
  text: string
  sentAt: string
}

// ---------- Analytics ----------

export interface BusinessSummary {
  totalEvents: number
  ridesRequested: number
  ridesAccepted: number
  ridesStarted: number
  ridesCompleted: number
  ridesCancelled: number
  ridesWithoutDriver: number
  completionRate: number | null
  avgSecondsToAccept: number | null
  avgRideSeconds: number | null
  lastEventAt: string | null
  eventsByType: Record<string, number>
  timeline: { minute: string; counts: Record<string, number> }[]
}

export interface RideEvent {
  id: number
  eventId: string
  eventType: string
  rideId: string
  correlationId: string
  source: string
  occurredAt: string
  publishedAt: string | null
  consumedAt: string
  partition: number
  offset: number
  payload: Record<string, unknown>
}

export interface StreamMessage {
  partition: number
  offset: number
  timestamp: string
  key: string
  eventType: string | null
  value: string
}

export interface StreamRead {
  topic: string
  consumerGroup: string
  count: number
  messages: StreamMessage[]
}

export interface ConsumerGroupStatus {
  topic: string
  consumerGroup: string
  totalMessages: number
  totalLag: number
  partitions: { partition: number; lowWatermark: number; highWatermark: number; committedOffset: number | null; lag: number }[]
}

export interface MetricSummary {
  mechanism: string
  kind: string
  operation: string
  count: number
  errors: number
  errorRate: number
  avgMs: number | null
  p50Ms: number | null
  p95Ms: number | null
  p99Ms: number | null
  maxMs: number | null
  observedPerSecond: number | null
  firstAt: string
  lastAt: string
}

export interface MeasurementPoint {
  recordedAt: string
  durationMs: number
  success: boolean
  source: string
  error: string | null
}

export interface MetricDefinition {
  mechanism: string
  kind: string
  operation: string
  start: string
  end: string
  measuredBy: string
  notes: string
}

export interface RunSummary {
  id: string
  name: string
  mechanism: string
  kind: string
  operation: string
  scenario: string
  tool: string
  startedAt: string
  finishedAt: string
  count: number
  errors: number
  errorRate: number
  avgMs: number | null
  p50Ms: number | null
  p95Ms: number | null
  p99Ms: number | null
  maxMs: number | null
  throughputPerSecond: number | null
  parameters: Record<string, string> | null
  notes: string | null
  // Scenario experiments (A/B); tool runs use experimentType 'tool'
  experimentType: string
  scenarioKey: string | null
  status: 'Queued' | 'Running' | 'Completed' | 'Incomplete' | 'Failed'
  groupId: string | null
  results: Record<string, number | null> | null
  observations: string[] | null
  details: RunDetails | null
  qualitativeNotes: string | null
  error: string | null
}

export interface ExperimentARide {
  index: number
  sentAtMs: number
  httpMs: number
  httpStatus: number
  accepted: boolean
  rideId: string | null
  finalStatus: string | null
  outcome: 'offer-created' | 'no-driver' | 'pending' | 'late-offer' | 'failed'
  timeToOfferMs: number | null
  offerAtMs: number | null
  publishConfirmMs: number | null
  offers: number
  reason: string | null
}

export interface ConsumerResult {
  name: string
  subscription: string
  received: number
  unique: number
  duplicates: number
  outOfOrder: number
  missing: number
  latencyP50Ms: number | null
  latencyP95Ms: number | null
  latencyMaxMs: number | null
  acksOrCommits: number
  note: string | null
}

export interface RunDetails {
  rides?: ExperimentARide[]
  queueSamples?: { atMs: number; messages: number; consumers: number }[]
  consumers?: ConsumerResult[]
  implementation?: string[]
  timeline?: string[]
}

export interface ExperimentStarted {
  groupId: string
  runIds: string[]
}

export interface ActiveExperiment {
  experiment: string
  scenario: string
  groupId: string
  startedAt: string
  phase: string
}

export type EvidenceStatus = 'NotTested' | 'Hypothesis' | 'Supported' | 'PartiallySupported' | 'Contradicted'

export interface ScenarioFinding {
  key: string
  experiment: string
  title: string
  researchQuestion: string
  requirements: string
  hypothesis: string
  expectedProperties: string
  observations: string
  advantages: string
  disadvantages: string
  recommendation: string
  limitations: string
  evidenceStatus: EvidenceStatus
  updatedAt: string
}

export interface TradeOffNote {
  experiment: string
  criterion: string
  approach: string
  text: string
  evidenceType: 'Measured' | 'Expected' | 'NotTested'
  updatedAt: string
}

export interface TradeOffMatrix {
  experiment: 'A' | 'B'
  approaches: string[]
  criteria: { key: string; title: string; question: string }[]
  notes: TradeOffNote[]
}

export type LookupMode = 'Rest' | 'Grpc'

export interface MatchingConfig {
  configuredMode: LookupMode
  activeMode: LookupMode
  pendingInjectedFailures: number
}
