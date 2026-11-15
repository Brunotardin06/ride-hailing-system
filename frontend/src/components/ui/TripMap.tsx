import { useEffect } from 'react'
import { CircleMarker, MapContainer, Polyline, TileLayer, Tooltip, useMap, useMapEvents } from 'react-leaflet'
import type { LatLngBoundsExpression } from 'leaflet'
import type { GeoPoint } from '../../api/places'

const EINDHOVEN: GeoPoint = { lat: 51.4416, lng: 5.4697 }

interface TripMapProps {
  pickup?: GeoPoint
  destination?: GeoPoint
  car?: GeoPoint | null
  carLabel?: string
  /** If set, clicking the map reports the clicked point (used to pick a location). */
  onPick?: (point: GeoPoint) => void
  /** Re-fit the view when this value changes (not on every car movement). */
  fitKey?: string
  height?: number
}

/**
 * Real map (OpenStreetMap tiles via Leaflet). Pickup = green, destination = red, car = blue.
 * Markers are circles (no image assets). Tiles are loaded from the OpenStreetMap servers.
 */
export default function TripMap({ pickup, destination, car, carLabel = 'Driver', onPick, fitKey, height = 280 }: TripMapProps) {
  const points = [pickup, destination, car].filter((p): p is GeoPoint => !!p)

  return (
    <MapContainer
      className="trip-map"
      style={{ height }}
      center={[points[0]?.lat ?? EINDHOVEN.lat, points[0]?.lng ?? EINDHOVEN.lng]}
      zoom={13}
      scrollWheelZoom
    >
      <TileLayer
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      {pickup && destination && (
        <Polyline positions={[[pickup.lat, pickup.lng], [destination.lat, destination.lng]]} pathOptions={{ className: 'trip-map-route' }} />
      )}
      {pickup && (
        <CircleMarker center={[pickup.lat, pickup.lng]} radius={9} pathOptions={{ className: 'trip-map-pickup' }}>
          <Tooltip>Pickup</Tooltip>
        </CircleMarker>
      )}
      {destination && (
        <CircleMarker center={[destination.lat, destination.lng]} radius={9} pathOptions={{ className: 'trip-map-destination' }}>
          <Tooltip>Destination</Tooltip>
        </CircleMarker>
      )}
      {car && (
        <CircleMarker center={[car.lat, car.lng]} radius={8} pathOptions={{ className: 'trip-map-car' }}>
          <Tooltip>{carLabel}</Tooltip>
        </CircleMarker>
      )}
      {onPick && <ClickToPick onPick={onPick} />}
      <FitBounds points={points} fitKey={fitKey ?? points.map((p) => `${p.lat},${p.lng}`).join('|')} />
    </MapContainer>
  )
}

function ClickToPick({ onPick }: { onPick: (point: GeoPoint) => void }) {
  useMapEvents({ click: (e) => onPick({ lat: e.latlng.lat, lng: e.latlng.lng }) })
  return null
}

/** Zoom/pan so all markers are visible, whenever fitKey changes. */
function FitBounds({ points, fitKey }: { points: GeoPoint[]; fitKey: string }) {
  const map = useMap()
  useEffect(() => {
    if (points.length === 1) map.setView([points[0].lat, points[0].lng], Math.max(map.getZoom(), 14))
    if (points.length > 1) {
      const bounds: LatLngBoundsExpression = points.map((p) => [p.lat, p.lng] as [number, number])
      map.fitBounds(bounds, { padding: [30, 30], maxZoom: 15 })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fitKey, map])
  return null
}
