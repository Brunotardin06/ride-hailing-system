import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { driversApi } from '../../api/client'
import type { Offer } from '../../api/types'
import Badge from '../../components/ui/Badge'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import PageHeader from '../../components/ui/PageHeader'
import { useDriver } from '../../state/areaContexts'
import { errorMessage, useToast } from '../../state/toastContext'

export default function IncomingRequestsPage() {
  const { driver, pendingOffers } = useDriver()

  return (
    <>
      <PageHeader eyebrow="Driver" title="Incoming requests" />
      {pendingOffers.length === 0 ? (
        <Card>
          <EmptyState
            icon="inbox"
            title="No incoming requests"
            message={driver?.status === 'Offline' ? 'You are offline. Go online on the dashboard to receive rides.' : 'New offers appear here in real time.'}
          />
        </Card>
      ) : (
        pendingOffers.map((offer) => <OfferCard key={offer.id} offer={offer} />)
      )}
    </>
  )
}

function OfferCard({ offer }: { offer: Offer }) {
  const { driver, refresh } = useDriver()
  const toast = useToast()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const secondsLeft = useCountdown(offer.expiresAt)
  const totalSeconds = Math.max(1, (new Date(offer.expiresAt).getTime() - new Date(offer.createdAt).getTime()) / 1000)

  const respond = async (accept: boolean) => {
    if (!driver) return
    setBusy(true)
    try {
      if (accept) {
        await driversApi.accept(offer.id, driver.id)
        toast('Ride accepted — the passenger is being notified', 'success')
      } else {
        await driversApi.reject(offer.id, driver.id)
        toast('Ride rejected — it will be offered to another driver', 'info')
      }
      await refresh()
      if (accept) navigate('/driver/active')
    } catch (error) {
      toast(errorMessage(error), 'error')
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card title={`${offer.pickup} → ${offer.destination}`} subtitle={`Passenger: ${offer.passengerName} · attempt ${offer.attempt}`}
      aside={<Badge tone={secondsLeft <= 10 ? 'danger' : 'warning'}>{secondsLeft > 0 ? `${secondsLeft}s to respond` : 'Expiring…'}</Badge>}
      className="offer-card">
      <div className="progress"><span style={{ width: `${Math.min(100, (secondsLeft / totalSeconds) * 100)}%` }} /></div>
      <div className="button-row">
        <button className="button button--primary" onClick={() => respond(true)} disabled={busy || secondsLeft <= 0}>Accept</button>
        <button className="button" onClick={() => respond(false)} disabled={busy || secondsLeft <= 0}>Reject</button>
      </div>
    </Card>
  )
}

function useCountdown(until: string) {
  const compute = () => Math.max(0, Math.ceil((new Date(until).getTime() - Date.now()) / 1000))
  const [seconds, setSeconds] = useState(compute)
  useEffect(() => {
    const timer = setInterval(() => setSeconds(compute()), 500)
    return () => clearInterval(timer)
  })
  return seconds
}
