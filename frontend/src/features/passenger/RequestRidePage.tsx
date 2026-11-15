import { useState, type FormEvent, type KeyboardEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ridesApi } from '../../api/client'
import { reverseGeocode, searchAddress } from '../../api/geocoding'
import { distanceMeters, places, type GeoPoint, type Place } from '../../api/places'
import Card from '../../components/ui/Card'
import PageHeader from '../../components/ui/PageHeader'
import RideStatusBadge from '../../components/ui/RideStatusBadge'
import TripMap from '../../components/ui/TripMap'
import { usePassenger } from '../../state/areaContexts'
import { errorMessage, useToast } from '../../state/toastContext'

const activeStatuses = ['Requested', 'Assigned', 'InProgress']
type Which = 'pickup' | 'destination'

export default function RequestRidePage() {
  const { passenger, currentRide, refresh } = usePassenger()
  const toast = useToast()
  const navigate = useNavigate()
  const [pickup, setPickup] = useState<Place | null>(places[0])
  const [destination, setDestination] = useState<Place | null>(places[1])
  const [active, setActive] = useState<Which>('pickup') // which field a map click fills
  const [submitting, setSubmitting] = useState(false)

  const activeRide = currentRide && activeStatuses.includes(currentRide.status) ? currentRide : null
  const set = (which: Which, place: Place | null) => (which === 'pickup' ? setPickup : setDestination)(place)
  const samePlace = !!pickup && !!destination && distanceMeters(pickup, destination) < 50

  // Map click: place the point immediately, then replace the coordinates with a readable address.
  const pickOnMap = async (point: GeoPoint) => {
    const which = active
    set(which, { name: `${point.lat.toFixed(5)}, ${point.lng.toFixed(5)}`, ...point })
    if (which === 'pickup' && !destination) setActive('destination')
    const name = await reverseGeocode(point.lat, point.lng)
    const update = (current: Place | null) => (current?.lat === point.lat && current?.lng === point.lng ? { ...current, name } : current)
    if (which === 'pickup') setPickup(update)
    else setDestination(update)
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!pickup || !destination) return
    setSubmitting(true)
    try {
      await ridesApi.request({
        passengerId: passenger.id, passengerName: passenger.name,
        pickup: pickup.name, destination: destination.name,
        pickupLat: pickup.lat, pickupLng: pickup.lng, destinationLat: destination.lat, destinationLng: destination.lng,
      })
      toast('Ride requested — looking for a driver…', 'success')
      await refresh()
      navigate('/passenger/current')
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      <PageHeader eyebrow="Passenger" title="Request a ride" />

      <div className="grid grid--split">
        <Card title="Trip details">
          {activeRide ? (
            <div className="notice">
              <p>You already have an active ride: <strong>{activeRide.pickup} → {activeRide.destination}</strong> <RideStatusBadge status={activeRide.status} /></p>
              <Link to="/passenger/current" className="button button--primary">Go to current ride</Link>
            </div>
          ) : (
            <form className="form" onSubmit={submit}>
              <LocationField
                label="Pickup location" value={pickup} active={active === 'pickup'}
                onFocus={() => setActive('pickup')} onChange={(p) => setPickup(p)}
              />
              <LocationField
                label="Destination" value={destination} active={active === 'destination'}
                onFocus={() => setActive('destination')} onChange={(p) => setDestination(p)}
              />
              <button type="submit" className="button button--primary" disabled={submitting || !pickup || !destination || samePlace}>
                {submitting ? 'Requesting…' : 'Request ride'}
              </button>
              {samePlace && <p className="hint">Pickup and destination must differ.</p>}
            </form>
          )}
        </Card>

        {!activeRide && (
          <Card title="Map">
            <p className="map-hint">
              Click the map to set the <strong>{active === 'pickup' ? 'pickup' : 'destination'}</strong>
              {' '}(select a field on the left to change which one).
            </p>
            <TripMap pickup={pickup ?? undefined} destination={destination ?? undefined} onPick={pickOnMap} height={360} />
          </Card>
        )}
      </div>
    </>
  )
}

/** Address search (Enter / Search button), quick picks, and the chosen point. */
function LocationField({ label, value, active, onFocus, onChange }: {
  label: string
  value: Place | null
  active: boolean
  onFocus: () => void
  onChange: (place: Place | null) => void
}) {
  const toast = useToast()
  const [query, setQuery] = useState(value?.name ?? '')
  const [results, setResults] = useState<Place[]>([])
  const [searching, setSearching] = useState(false)

  // Show the chosen place's name when it changes from outside (map click, reverse geocoding, quick pick).
  const [shownName, setShownName] = useState(value?.name)
  if (value?.name !== shownName) {
    setShownName(value?.name)
    setQuery(value?.name ?? '')
  }

  const search = async () => {
    if (query.trim().length < 3) return
    setSearching(true)
    try {
      const found = await searchAddress(query.trim())
      setResults(found)
      if (found.length === 0) toast('No address found. Try adding the city, e.g. "Stratumseind, Eindhoven".', 'info')
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setSearching(false)
    }
  }

  const choose = (place: Place) => {
    onChange(place)
    setResults([])
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.preventDefault() // Enter searches instead of submitting the form
      void search()
    }
  }

  const edited = query !== (value?.name ?? '')

  return (
    <div className={`location-field${active ? ' location-field--active' : ''}`} onFocus={onFocus}>
      <span className="field-label">{label}</span>
      <div className="location-row">
        <input
          value={query}
          onChange={(e) => { setQuery(e.target.value); if (value) onChange(null) }}
          onKeyDown={onKeyDown}
          placeholder="Type an address and press Enter, or click the map"
          maxLength={200}
          aria-label={label}
        />
        <button type="button" className="button" onClick={() => void search()} disabled={searching || query.trim().length < 3}>
          {searching ? '…' : 'Search'}
        </button>
      </div>
      {results.length > 0 && (
        <ul className="location-results">
          {results.map((r) => (
            <li key={`${r.lat},${r.lng}`}><button type="button" onClick={() => choose(r)}>{r.name}</button></li>
          ))}
        </ul>
      )}
      <div className="location-chips">
        {places.map((p) => <button key={p.name} type="button" className="chip" onClick={() => choose(p)}>{p.name}</button>)}
      </div>
      <span className="location-chosen">
        {value ? `📍 ${value.lat.toFixed(5)}, ${value.lng.toFixed(5)}` : edited && query ? 'Press Enter to search this address' : 'No location chosen'}
      </span>
    </div>
  )
}
