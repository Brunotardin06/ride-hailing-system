import { useState } from 'react'
import { Link } from 'react-router-dom'
import { ridesApi } from '../../api/client'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import LiveFeed from '../../components/ui/LiveFeed'
import PageHeader from '../../components/ui/PageHeader'
import RideStatusBadge from '../../components/ui/RideStatusBadge'
import RideTimeline from '../../components/ui/RideTimeline'
import { useRealtime } from '../../realtime/realtimeContext'
import { usePassenger } from '../../state/areaContexts'
import { errorMessage, useToast } from '../../state/toastContext'

export default function CurrentRidePage() {
  const { passenger, currentRide: ride, loading, refresh } = usePassenger()
  const { notifications } = useRealtime()
  const toast = useToast()
  const [cancelling, setCancelling] = useState(false)

  const cancel = async () => {
    if (!ride) return
    setCancelling(true)
    try {
      await ridesApi.cancel(ride.id, passenger.id)
      toast('Ride cancelled', 'success')
      await refresh()
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setCancelling(false)
    }
  }

  if (!ride) {
    return (
      <>
        <PageHeader eyebrow="Passenger" title="Current ride" />
        <Card>
          <EmptyState icon="car" title={loading ? 'Loading…' : 'No rides yet'} message="Request a ride to see its live status here." />
          <p className="center"><Link to="/passenger/request" className="button button--primary">Request a ride</Link></p>
        </Card>
      </>
    )
  }

  const finished = !['Requested', 'Assigned', 'InProgress'].includes(ride.status)
  const rideNotifications = notifications.filter((n) => n.rideId === ride.id)

  return (
    <>
      <PageHeader
        eyebrow="Passenger"
        title={finished ? 'Last ride' : 'Current ride'}
        description={`${ride.pickup} → ${ride.destination}`}
        actions={
          finished ? (
            <Link to="/passenger/request" className="button button--primary">Request another ride</Link>
          ) : (
            (ride.status === 'Requested' || ride.status === 'Assigned') && (
              <button className="button" onClick={cancel} disabled={cancelling}>{cancelling ? 'Cancelling…' : 'Cancel ride'}</button>
            )
          )
        }
      />

      <div className="grid grid--split">
        <Card title="Ride status" aside={<RideStatusBadge status={ride.status} />}>
          {ride.status === 'Requested' && (
            <div className="searching">
              <span className="pulse" />
              {ride.offerCreatedAt
                ? `Offer sent to ${ride.offeredDriverName ?? 'a driver'} — waiting for the answer…`
                : ride.matchingMode === 'RabbitMq'
                  ? 'Request received — looking for a driver…'
                  : 'Looking for a driver…'}
            </div>
          )}
          <RideTimeline ride={ride} />
        </Card>

        <Card title="Your driver">
          {ride.driverName ? (
            <div className="person">
              <span className="avatar">{ride.driverName[0]}</span>
              <div>
                <p className="person-name">{ride.driverName}</p>
                <p className="card-subtitle">{ride.vehicle}</p>
                <p className="plate">{ride.licensePlate}</p>
              </div>
              {(ride.status === 'Assigned' || ride.status === 'InProgress') && (
                <Link to="/passenger/chat" className="button">Chat</Link>
              )}
            </div>
          ) : (
            <EmptyState icon="user" title="No driver assigned yet" message="Driver details appear once a driver accepts." />
          )}
        </Card>
      </div>

      <Card title="Live updates">
        <LiveFeed notifications={rideNotifications} empty="Updates will appear here in real time." />
      </Card>
    </>
  )
}
