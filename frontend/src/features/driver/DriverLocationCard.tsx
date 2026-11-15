import { useState } from 'react'
import type { Driver, Ride } from '../../api/types'
import { ridePoint } from '../../api/places'
import Card from '../../components/ui/Card'
import TripMap from '../../components/ui/TripMap'
import { useToast } from '../../state/toastContext'
import { setDriverPosition, useDriverPosition } from './driverPosition'

/**
 * The driver's position on a real map. The simulator (useLocationSimulator) moves the car every 2 s;
 * here the driver can also place it: click the map, or use the device's real GPS (browser Geolocation API).
 * While a ride is active the car drives itself to the pickup / destination, so placing is disabled.
 */
export default function DriverLocationCard({ driver, activeRide }: { driver: Driver; activeRide: Ride | null }) {
  const position = useDriverPosition(driver.id)
  const toast = useToast()
  const [locating, setLocating] = useState(false)
  const driving = activeRide?.status === 'Assigned' || activeRide?.status === 'InProgress'

  const useRealGps = () => {
    if (!('geolocation' in navigator)) {
      toast('This browser has no location support.', 'error')
      return
    }
    setLocating(true)
    navigator.geolocation.getCurrentPosition(
      (p) => {
        setDriverPosition(driver.id, { lat: p.coords.latitude, lng: p.coords.longitude })
        toast('Location set from your device', 'success')
        setLocating(false)
      },
      () => {
        toast('Location permission denied or unavailable.', 'error')
        setLocating(false)
      },
      { enableHighAccuracy: true, timeout: 10000 },
    )
  }

  return (
    <Card
      title="My location"
      aside={!driving && (
        <button className="button button--small" onClick={useRealGps} disabled={locating}>
          {locating ? 'Locating…' : 'Use my real location'}
        </button>
      )}
    >
      <p className="map-hint">
        {driving
          ? 'Driving to the ' + (activeRide?.status === 'Assigned' ? 'pickup' : 'destination') + '…'
          : driver.status === 'Offline'
            ? 'You are offline: your location is not shared. Click the map to place your car.'
            : 'Shared every 2 seconds while you are online. Click the map to move your car.'}
      </p>
      <TripMap
        car={position}
        carLabel="You"
        pickup={driving && activeRide ? ridePoint(activeRide, 'pickup') : undefined}
        destination={driving && activeRide ? ridePoint(activeRide, 'destination') : undefined}
        onPick={driving ? undefined : (point) => setDriverPosition(driver.id, point)}
        fitKey={`${driver.id}:${activeRide?.id ?? 'idle'}:${activeRide?.status ?? ''}`}
        height={280}
      />
    </Card>
  )
}
