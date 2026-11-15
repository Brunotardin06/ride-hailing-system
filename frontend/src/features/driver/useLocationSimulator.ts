import { useEffect, useRef } from 'react'
import type { Driver, Ride } from '../../api/types'
import { LOCATION_INTERVAL_MS, ridePoint, SIMULATED_STEP_METERS, stepTowards, type GeoPoint } from '../../api/places'
import { useRealtime } from '../../realtime/realtimeContext'
import { getDriverPosition, setDriverPosition } from './driverPosition'

/**
 * Simulated GPS: the driver's tab plays the role of the driver's phone.
 * Every 2 s while the driver is online it moves the car one step and sends the position over SignalR:
 *   Available            → drifts a little around where the car is (cruising)
 *   Assigned             → drives in a straight line to the pickup
 *   InProgress           → drives in a straight line to the destination
 *   Offline              → sends nothing
 * The driver can also place the car on the dashboard map or use the device's real GPS (driverPosition.ts).
 */
export function useLocationSimulator(driver: Driver | null, activeRide: Ride | null) {
  const { sendLocation } = useRealtime()

  // Keep the latest values in a ref, so the 2 s timer does not restart on every refresh.
  const state = useRef({ driver, activeRide })
  useEffect(() => { state.current = { driver, activeRide } }, [driver, activeRide])

  const driverId = driver?.id
  useEffect(() => {
    if (!driverId) return

    const tick = () => {
      const { driver: d, activeRide: ride } = state.current
      if (!d || d.status === 'Offline') return

      const target =
        ride?.status === 'Assigned' ? ridePoint(ride, 'pickup')
        : ride?.status === 'InProgress' ? ridePoint(ride, 'destination')
        : undefined

      const current = getDriverPosition(driverId)
      const next = target ? stepTowards(current, target, SIMULATED_STEP_METERS) : drift(current)
      setDriverPosition(driverId, next)
      sendLocation(next.lat, next.lng)
    }

    tick()
    const timer = setInterval(tick, LOCATION_INTERVAL_MS)
    return () => clearInterval(timer)
  }, [driverId, sendLocation])
}

/** Small random movement (≈ 20–40 m) while waiting for a ride. */
function drift(p: GeoPoint): GeoPoint {
  return { lat: p.lat + (Math.random() - 0.5) * 0.0006, lng: p.lng + (Math.random() - 0.5) * 0.0009 }
}
