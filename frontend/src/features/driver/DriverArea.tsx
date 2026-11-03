import { useCallback, useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { driversApi, ridesApi } from '../../api/client'
import type { Driver, Offer, Ride } from '../../api/types'
import IdentityBar from '../../components/layout/IdentityBar'
import { RealtimeProvider } from '../../realtime/RealtimeProvider'
import { useRealtime } from '../../realtime/realtimeContext'
import { DriverContext } from '../../state/areaContexts'
import { defaultDriverId, useTabState } from '../../state/identity'
import { errorMessage, useToast } from '../../state/toastContext'

/** Layout for /driver/*: demo driver selection, SignalR connection and shared driver state. */
export default function DriverArea() {
  const [driverId, setDriverId] = useTabState('driverId', defaultDriverId)
  const [drivers, setDrivers] = useState<Driver[]>([])
  const toast = useToast()

  useEffect(() => {
    driversApi.list().then(setDrivers).catch((error) => toast(errorMessage(error), 'error'))
  }, [toast])

  const name = drivers.find((d) => d.id === driverId)?.name ?? 'Driver'

  return (
    <RealtimeProvider key={driverId} role="driver" userId={driverId} userName={name}>
      <DriverStateProvider driverId={driverId} drivers={drivers} setDrivers={setDrivers} setDriverId={setDriverId}>
        <IdentityBar
          label="Driver"
          value={driverId}
          options={drivers.map((d) => ({ value: d.id, label: `${d.name} · ${d.status}` }))}
          onChange={setDriverId}
        />
        <Outlet />
      </DriverStateProvider>
    </RealtimeProvider>
  )
}

function DriverStateProvider({ driverId, drivers, setDrivers, setDriverId, children }: {
  driverId: string
  drivers: Driver[]
  setDrivers: (drivers: Driver[]) => void
  setDriverId: (id: string) => void
  children: React.ReactNode
}) {
  const { onNotification, status } = useRealtime()
  const toast = useToast()
  const [pendingOffers, setPendingOffers] = useState<Offer[]>([])
  const [activeRide, setActiveRide] = useState<Ride | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    try {
      const [all, offers, ride] = await Promise.all([
        driversApi.list(),
        driversApi.pendingOffers(driverId),
        ridesApi.active({ driverId }),
      ])
      setDrivers(all)
      setPendingOffers(offers)
      setActiveRide(ride ?? null)
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setLoading(false)
    }
  }, [driverId, setDrivers, toast])

  useEffect(() => { void refresh() }, [refresh, status])
  useEffect(() => onNotification((n) => {
    toast(`${n.title}: ${n.message}`, n.type === 'ride-cancelled' ? 'error' : 'info')
    void refresh()
  }), [onNotification, refresh, toast])
  useEffect(() => {
    const timer = setInterval(() => void refresh(), 15000)
    return () => clearInterval(timer)
  }, [refresh])

  const driver = drivers.find((d) => d.id === driverId) ?? null

  return (
    <DriverContext.Provider value={{ drivers, driver, setDriverId, pendingOffers, activeRide, loading, refresh }}>
      {children}
    </DriverContext.Provider>
  )
}
