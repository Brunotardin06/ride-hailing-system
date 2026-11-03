import { createContext, useContext } from 'react'
import type { Driver, Offer, Ride } from '../api/types'

export interface PassengerState {
  passenger: { id: string; name: string }
  /** The passenger's most recent ride (active or just finished), null if none yet. */
  currentRide: Ride | null
  loading: boolean
  refresh: () => Promise<void>
}

export interface DriverState {
  drivers: Driver[]
  driver: Driver | null
  setDriverId: (id: string) => void
  pendingOffers: Offer[]
  /** Ride the driver is assigned to or driving. */
  activeRide: Ride | null
  loading: boolean
  refresh: () => Promise<void>
}

export const PassengerContext = createContext<PassengerState | null>(null)
export const DriverContext = createContext<DriverState | null>(null)

export function usePassenger() {
  const value = useContext(PassengerContext)
  if (!value) throw new Error('usePassenger must be used inside the passenger area')
  return value
}

export function useDriver() {
  const value = useContext(DriverContext)
  if (!value) throw new Error('useDriver must be used inside the driver area')
  return value
}
