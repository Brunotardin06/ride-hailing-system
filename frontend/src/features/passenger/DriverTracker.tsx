import { useEffect, useState } from 'react'
import type { DriverLocation, Ride } from '../../api/types'
import { distanceMeters, formatDistance, LOCATION_INTERVAL_MS, ridePoint, SIMULATED_STEP_METERS } from '../../api/places'
import TripMap from '../../components/ui/TripMap'
import { useRealtime } from '../../realtime/realtimeContext'

/**
 * Live position of the passenger's driver (pushed by RealtimeService every ~2 s, coming from Kafka).
 * Assigned → distance to the pickup; InProgress → distance to the destination.
 */
export default function DriverTracker({ ride }: { ride: Ride }) {
  const { onDriverLocation } = useRealtime()
  const [latest, setLatest] = useState<DriverLocation | null>(null)

  useEffect(() => onDriverLocation((l) => { if (l.rideId === ride.id) setLatest(l) }), [onDriverLocation, ride.id])
  const location = latest?.rideId === ride.id ? latest : null // ignore a position left over from a previous ride

  const pickup = ridePoint(ride, 'pickup')
  const destination = ridePoint(ride, 'destination')
  const target = ride.status === 'Assigned' ? pickup : destination
  const name = ride.driverName ?? 'Your driver'

  let text = "Waiting for the driver's location…"
  if (location && target) {
    const meters = distanceMeters(location, target)
    // ETA uses the simulation speed (the demo car is accelerated), so it matches what you see on the map.
    const seconds = Math.round((meters / SIMULATED_STEP_METERS) * (LOCATION_INTERVAL_MS / 1000))
    text = meters < 60
      ? ride.status === 'Assigned' ? `${name} has arrived at the pickup` : 'Arriving at your destination'
      : ride.status === 'Assigned'
        ? `${name} is ${formatDistance(meters)} away · arriving in ~${seconds} s`
        : `${formatDistance(meters)} to your destination · ~${seconds} s`
  } else if (location) {
    text = 'Driver location received'
  }

  return (
    <>
      <p className={`tracker-text${location ? '' : ' tracker-text--muted'}`}>{location ? '🚗 ' : ''}{text}</p>
      <TripMap
        pickup={pickup}
        destination={destination}
        car={location}
        carLabel={name}
        // Fit once the car appears, and when the ride's phase changes — not on every 2 s movement.
        fitKey={`${ride.id}:${ride.status}:${location ? 'car' : 'nocar'}`}
        height={240}
      />
    </>
  )
}
