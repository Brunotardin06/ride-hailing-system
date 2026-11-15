// Response types of the backend REST APIs (mirrors the C# DTOs).

export type RideStatus = 'Requested' | 'Assigned' | 'InProgress' | 'Completed' | 'Cancelled' | 'NoDriverAvailable'

export interface Ride {
  id: string
  correlationId: string
  passengerId: string
  passengerName: string
  pickup: string
  destination: string
  /** Coordinates picked on the map (null for rides without a known location). */
  pickupLat: number | null
  pickupLng: number | null
  destinationLat: number | null
  destinationLng: number | null
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
  pickupLat: number | null
  pickupLng: number | null
  destinationLat: number | null
  destinationLng: number | null
}

// ---------- Realtime (SignalR) ----------

/** Position of the passenger's driver, pushed every ~2 s while the ride is Assigned / InProgress. */
export interface DriverLocation {
  rideId: string
  driverId: string
  lat: number
  lng: number
  recordedAt: string
}

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

