import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ridesApi } from '../../api/client'
import Card from '../../components/ui/Card'
import PageHeader from '../../components/ui/PageHeader'
import RideStatusBadge from '../../components/ui/RideStatusBadge'
import { usePassenger } from '../../state/areaContexts'
import { errorMessage, useToast } from '../../state/toastContext'

const places = [
  'Fontys Rachelsmolen (R1)', 'Eindhoven Centraal', 'Strijp-S', 'Philips Stadion',
  'Eindhoven Airport', 'High Tech Campus', 'Van Abbemuseum', 'TU/e Campus',
]

const activeStatuses = ['Requested', 'Assigned', 'InProgress']

export default function RequestRidePage() {
  const { passenger, currentRide, refresh } = usePassenger()
  const toast = useToast()
  const navigate = useNavigate()
  const [pickup, setPickup] = useState(places[0])
  const [destination, setDestination] = useState(places[1])
  const [submitting, setSubmitting] = useState(false)

  const activeRide = currentRide && activeStatuses.includes(currentRide.status) ? currentRide : null

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    try {
      await ridesApi.request({ passengerId: passenger.id, passengerName: passenger.name, pickup, destination })
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
              <label className="field">
                <span>Pickup location</span>
                <input list="places" value={pickup} onChange={(e) => setPickup(e.target.value)} required minLength={2} maxLength={200} />
              </label>
              <label className="field">
                <span>Destination</span>
                <input list="places" value={destination} onChange={(e) => setDestination(e.target.value)} required minLength={2} maxLength={200} />
              </label>
              <datalist id="places">
                {places.map((p) => <option key={p} value={p} />)}
              </datalist>
              <button type="submit" className="button button--primary" disabled={submitting || pickup.trim() === destination.trim()}>
                {submitting ? 'Requesting…' : 'Request ride'}
              </button>
              {pickup.trim() === destination.trim() && <p className="hint">Pickup and destination must differ.</p>}
            </form>
          )}
        </Card>
      </div>
    </>
  )
}
