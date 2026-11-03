import { useState } from 'react'
import { Link } from 'react-router-dom'
import { ridesApi } from '../../api/client'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import PageHeader from '../../components/ui/PageHeader'
import RideStatusBadge from '../../components/ui/RideStatusBadge'
import RideTimeline from '../../components/ui/RideTimeline'
import { useDriver } from '../../state/areaContexts'
import { errorMessage, useToast } from '../../state/toastContext'

export default function ActiveRidePage() {
  const { driver, activeRide: ride, refresh } = useDriver()
  const toast = useToast()
  const [busy, setBusy] = useState(false)

  const act = async (action: 'start' | 'complete') => {
    if (!ride || !driver) return
    setBusy(true)
    try {
      await (action === 'start' ? ridesApi.start(ride.id, driver.id) : ridesApi.complete(ride.id, driver.id))
      toast(action === 'start' ? 'Ride started' : 'Ride completed — you are available again', 'success')
      await refresh()
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setBusy(false)
    }
  }

  if (!ride) {
    return (
      <>
        <PageHeader eyebrow="Driver" title="Active ride" />
        <Card><EmptyState icon="car" title="No active ride" message="Accept an incoming request to start a ride." /></Card>
      </>
    )
  }

  return (
    <>
      <PageHeader eyebrow="Driver" title="Active ride" description={`${ride.pickup} → ${ride.destination}`} />

      <div className="grid grid--split">
        <Card title="Ride progress" aside={<RideStatusBadge status={ride.status} />}>
          <RideTimeline ride={ride} />
          <div className="button-row">
            <button className="button button--primary" onClick={() => act('start')} disabled={busy || ride.status !== 'Assigned'}>
              Passenger picked up · Start ride
            </button>
            <button className="button button--primary" onClick={() => act('complete')} disabled={busy || ride.status !== 'InProgress'}>
              Complete ride
            </button>
          </div>        </Card>

        <Card title="Passenger">
          <div className="person">
            <span className="avatar">{ride.passengerName[0]}</span>
            <div>
              <p className="person-name">{ride.passengerName}</p>
              <p className="card-subtitle">Pickup: {ride.pickup}</p>
            </div>
            <Link to="/driver/chat" className="button">Chat</Link>
          </div>
        </Card>
      </div>
    </>
  )
}
