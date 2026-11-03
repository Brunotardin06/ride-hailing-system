import { useCallback, useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { ridesApi } from '../../api/client'
import type { Ride } from '../../api/types'
import IdentityBar from '../../components/layout/IdentityBar'
import { RealtimeProvider } from '../../realtime/RealtimeProvider'
import { useRealtime } from '../../realtime/realtimeContext'
import { PassengerContext } from '../../state/areaContexts'
import { demoPassengers, useTabState } from '../../state/identity'
import { errorMessage, useToast } from '../../state/toastContext'

/** Layout for /passenger/*: identity, SignalR connection and shared ride state for all passenger pages. */
export default function PassengerArea() {
  const [passengerId, setPassengerId] = useTabState('passengerId', demoPassengers[0].id)
  const passenger = demoPassengers.find((p) => p.id === passengerId) ?? demoPassengers[0]

  return (
    <RealtimeProvider key={passenger.id} role="passenger" userId={passenger.id} userName={passenger.name}>
      <PassengerStateProvider passenger={passenger}>
        <IdentityBar
          label="Passenger"
          value={passenger.id}
          options={demoPassengers.map((p) => ({ value: p.id, label: p.name }))}
          onChange={setPassengerId}
        />
        <Outlet />
      </PassengerStateProvider>
    </RealtimeProvider>
  )
}

function PassengerStateProvider({ passenger, children }: { passenger: { id: string; name: string }; children: React.ReactNode }) {
  const { onNotification, status } = useRealtime()
  const toast = useToast()
  const [currentRide, setCurrentRide] = useState<Ride | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    try {
      const [latest] = await ridesApi.list({ passengerId: passenger.id, limit: 1 })
      setCurrentRide(latest ?? null)
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setLoading(false)
    }
  }, [passenger.id, toast])

  // Load on start, after (re)connecting, and whenever a notification arrives. Notifications are only
  // a signal; RideService (REST) stays the source of truth. A slow poll covers missed notifications.
  useEffect(() => { void refresh() }, [refresh, status])
  useEffect(() => onNotification((n) => {
    toast(`${n.title}: ${n.message}`, n.type === 'no-driver' || n.type === 'ride-cancelled' ? 'error' : 'info')
    void refresh()
  }), [onNotification, refresh, toast])
  useEffect(() => {
    const timer = setInterval(() => void refresh(), 15000)
    return () => clearInterval(timer)
  }, [refresh])

  return (
    <PassengerContext.Provider value={{ passenger, currentRide, loading, refresh }}>
      {children}
    </PassengerContext.Provider>
  )
}
