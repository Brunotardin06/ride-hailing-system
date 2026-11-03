import { useEffect, useState } from 'react'
import { ridesApi } from '../../api/client'
import type { Ride } from '../../api/types'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import PageHeader from '../../components/ui/PageHeader'
import RideStatusBadge from '../../components/ui/RideStatusBadge'
import { usePassenger } from '../../state/areaContexts'
import { errorMessage, useToast } from '../../state/toastContext'

export default function RideHistoryPage() {
  const { passenger, currentRide } = usePassenger()
  const toast = useToast()
  const [rides, setRides] = useState<Ride[] | null>(null)

  // Reload when the current ride changes (e.g. a ride was just completed).
  useEffect(() => {
    ridesApi.list({ passengerId: passenger.id, limit: 100 }).then(setRides).catch((error) => toast(errorMessage(error), 'error'))
  }, [passenger.id, currentRide?.id, currentRide?.status, toast])

  return (
    <>
      <PageHeader eyebrow="Passenger" title="Ride history" />

      <Card>
        {rides && rides.length > 0 ? (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr><th>Requested</th><th>From</th><th>To</th><th>Driver</th><th>Status</th></tr>
              </thead>
              <tbody>
                {rides.map((r) => (
                  <tr key={r.id}>
                    <td>{new Date(r.requestedAt).toLocaleString()}</td>
                    <td>{r.pickup}</td>
                    <td>{r.destination}</td>
                    <td>{r.driverName ?? '—'}</td>
                    <td><RideStatusBadge status={r.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <EmptyState icon="clock" title={rides ? 'No rides yet' : 'Loading…'} message="Your rides will be listed here." />
        )}
      </Card>
    </>
  )
}
