import { useSyncExternalStore } from 'react'
import { places, type GeoPoint } from '../../api/places'

// The driver's current (simulated) position, shared by:
//  - useLocationSimulator: moves it every 2 s and sends it to the backend
//  - the "My location" map on the dashboard: shows it, and lets the driver set it (click / real GPS)
// Kept per driver in localStorage, so a reload continues from the same spot.

const listeners = new Set<() => void>()
const cache = new Map<string, GeoPoint>()
const key = (driverId: string) => `driverPosition:${driverId}`

export function getDriverPosition(driverId: string): GeoPoint {
  const cached = cache.get(driverId)
  if (cached) return cached
  let position: GeoPoint | null = null
  try {
    const raw = localStorage.getItem(key(driverId))
    position = raw ? (JSON.parse(raw) as GeoPoint) : null
  } catch {
    // storage unavailable (private mode): fall back to the start position
  }
  position ??= startPosition(driverId)
  cache.set(driverId, position)
  return position
}

export function setDriverPosition(driverId: string, position: GeoPoint) {
  cache.set(driverId, position)
  try {
    localStorage.setItem(key(driverId), JSON.stringify(position))
  } catch {
    // not persisted; the simulation still works in this tab
  }
  listeners.forEach((l) => l())
}

/** React hook: re-renders when the position changes. */
export function useDriverPosition(driverId: string | undefined): GeoPoint | null {
  return useSyncExternalStore(
    (onChange) => { listeners.add(onChange); return () => { listeners.delete(onChange) } },
    () => (driverId ? getDriverPosition(driverId) : null),
  )
}

/** Each driver starts near a different demo place (chosen from the id), so they are at different distances. */
function startPosition(driverId: string): GeoPoint {
  const n = parseInt(driverId.slice(-2), 16) || 0
  const p = places[(n * 3) % places.length]
  return { lat: p.lat + 0.002, lng: p.lng - 0.002 }
}
