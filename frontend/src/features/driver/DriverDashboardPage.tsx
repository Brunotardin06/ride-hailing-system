import { useState } from 'react'
import { Link } from 'react-router-dom'
import { driversApi } from '../../api/client'
import type { DriverStatus } from '../../api/types'
import Badge from '../../components/ui/Badge'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import LiveFeed from '../../components/ui/LiveFeed'
import PageHeader from '../../components/ui/PageHeader'
import StatCard from '../../components/ui/StatCard'
import { useRealtime } from '../../realtime/realtimeContext'
import { useDriver } from '../../state/areaContexts'
import { errorMessage, useToast } from '../../state/toastContext'

const statusTone: Record<DriverStatus, 'neutral' | 'success' | 'warning' | 'info'> = {
  Offline: 'neutral', Available: 'success', Offered: 'warning', Busy: 'info',
}

export default function DriverDashboardPage() {
  const { driver, drivers, pendingOffers, activeRide, refresh } = useDriver()
  const { notifications } = useRealtime()
  const toast = useToast()
  const [saving, setSaving] = useState(false)

  if (!driver) {
    return (
      <>
        <PageHeader eyebrow="Driver" title="Dashboard" />
        <Card><EmptyState icon="car" title="Loading drivers…" message="Fetching demo drivers." /></Card>
      </>
    )
  }

  const canToggle = driver.status === 'Offline' || driver.status === 'Available'
  const toggle = async () => {
    setSaving(true)
    try {
      await driversApi.setAvailability(driver.id, driver.status === 'Offline')
      toast(driver.status === 'Offline' ? 'You are online and can receive rides' : 'You are offline', 'success')
      await refresh()
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setSaving(false)
    }
  }

  const resetDemo = async () => {
    try {
      await driversApi.resetDemo()
      toast('Drivers reset to demo state (Emma & Lucas available)', 'success')
      await refresh()
    } catch (error) {
      toast(errorMessage(error), 'error')
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Driver"
        title={driver.name}
        description={`${driver.vehicle} · ${driver.licensePlate}`}
        actions={
          <button className={`button ${driver.status === 'Offline' ? 'button--primary' : ''}`} onClick={toggle} disabled={!canToggle || saving}>
            {driver.status === 'Offline' ? 'Go online' : 'Go offline'}
          </button>
        }
      />

      <div className="grid grid--4">
        <StatCard label="Status" value={driver.status} hint={canToggle ? 'Toggle with the button above' : 'Locked while an offer/ride is active'} />
        <StatCard label="Completed rides" value={String(driver.completedRides)} />
        <StatCard label="Rating" value={driver.rating.toFixed(1)} />
        <StatCard label="Pending offers" value={String(pendingOffers.length)} />
      </div>

      <div className="grid grid--split">
        <Card title="Right now" aside={<Badge tone={statusTone[driver.status]}>{driver.status}</Badge>}>
          {pendingOffers.length > 0 ? (
            <div className="notice">
              <p>You have a new ride request.</p>
              <Link to="/driver/requests" className="button button--primary">View request</Link>
            </div>
          ) : activeRide ? (
            <div className="notice">
              <p>Active ride: <strong>{activeRide.pickup} → {activeRide.destination}</strong></p>
              <Link to="/driver/active" className="button button--primary">Open active ride</Link>
            </div>
          ) : (
            <p className="card-subtitle">
              {driver.status === 'Available'
                ? 'Waiting for ride requests. Offers are pushed to this tab in real time.'
                : 'Go online to receive ride requests.'}
            </p>
          )}
        </Card>

        <Card title="Live notifications">
          <LiveFeed notifications={notifications.slice(0, 6)} empty="Notifications will appear here." />
        </Card>
      </div>

      <Card title="All demo drivers" aside={<button className="button button--small" onClick={resetDemo}>Reset demo</button>}>
        <div className="table-wrap">
          <table className="table">
            <thead><tr><th>Driver</th><th>Vehicle</th><th>Status</th><th>Completed</th></tr></thead>
            <tbody>
              {drivers.map((d) => (
                <tr key={d.id} className={d.id === driver.id ? 'row--highlight' : ''}>
                  <td>{d.name}</td>
                  <td>{d.vehicle}</td>
                  <td><Badge tone={statusTone[d.status]}>{d.status}</Badge></td>
                  <td>{d.completedRides}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </>
  )
}
