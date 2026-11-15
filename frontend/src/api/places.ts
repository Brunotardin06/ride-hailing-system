// Demo places with coordinates. MUST match src/shared/RideHailing.Shared/Contracts/Places.cs
// (same names, same points), so a place means the same location in the browser and in the backend.

export interface GeoPoint {
  lat: number
  lng: number
}

export interface Place extends GeoPoint {
  name: string
}

export const places: Place[] = [
  { name: 'Fontys Rachelsmolen (R1)', lat: 51.4512, lng: 5.4810 },
  { name: 'Eindhoven Centraal', lat: 51.4433, lng: 5.4797 },
  { name: 'Strijp-S', lat: 51.4480, lng: 5.4570 },
  { name: 'Philips Stadion', lat: 51.4417, lng: 5.4673 },
  { name: 'Eindhoven Airport', lat: 51.4501, lng: 5.3745 },
  { name: 'High Tech Campus', lat: 51.4110, lng: 5.4590 },
  { name: 'Van Abbemuseum', lat: 51.4347, lng: 5.4818 },
  { name: 'TU/e Campus', lat: 51.4480, lng: 5.4900 },
]

export const placeNames = places.map((p) => p.name)

/** Coordinates of a known place; undefined for free text. */
export function findPlace(name: string | null | undefined): Place | undefined {
  const key = name?.trim().toLowerCase()
  return places.find((p) => p.name.toLowerCase() === key)
}

/** Pickup / destination of a ride: the coordinates picked on the map, or (older rides) a known place name. */
export function ridePoint(
  ride: { pickup: string; destination: string; pickupLat: number | null; pickupLng: number | null; destinationLat: number | null; destinationLng: number | null },
  which: 'pickup' | 'destination',
): GeoPoint | undefined {
  const lat = which === 'pickup' ? ride.pickupLat : ride.destinationLat
  const lng = which === 'pickup' ? ride.pickupLng : ride.destinationLng
  if (lat != null && lng != null) return { lat, lng }
  return findPlace(which === 'pickup' ? ride.pickup : ride.destination)
}

/** Distance in metres "as the crow flies" (haversine), same formula as the backend. */
export function distanceMeters(a: GeoPoint, b: GeoPoint): number {
  const rad = (d: number) => (d * Math.PI) / 180
  const dLat = rad(b.lat - a.lat)
  const dLng = rad(b.lng - a.lng)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2
  return 2 * 6_371_000 * Math.asin(Math.sqrt(h))
}

/** Move from `from` towards `to` by at most `meters` (straight line). Returns the new point. */
export function stepTowards(from: GeoPoint, to: GeoPoint, meters: number): GeoPoint {
  const total = distanceMeters(from, to)
  if (total <= meters) return { lat: to.lat, lng: to.lng }
  const f = meters / total
  return { lat: from.lat + (to.lat - from.lat) * f, lng: from.lng + (to.lng - from.lng) * f }
}

export function formatDistance(meters: number): string {
  return meters < 1000 ? `${Math.round(meters / 10) * 10} m` : `${(meters / 1000).toFixed(1)} km`
}

/**
 * Simulation speed: the driver tab moves the car SIMULATED_STEP_METERS every LOCATION_INTERVAL_MS
 * (≈ 360 km/h — accelerated so a demo trip takes under a minute instead of 10 minutes).
 */
export const LOCATION_INTERVAL_MS = 2000
export const SIMULATED_STEP_METERS = 200
