// One function per backend endpoint. The frontend talks directly to the service that owns the data
// (no API gateway): RideService for rides, DriverService for drivers and offers.
import { serviceUrls } from '../config'
import { json, request } from './http'
import type { Driver, Offer, Ride } from './types'

const ride = <T>(path: string, init?: RequestInit) => request<T>(serviceUrls.ride, path, init)
const driver = <T>(path: string, init?: RequestInit) => request<T>(serviceUrls.driver, path, init)

export const ridesApi = {
  request: (body: {
    passengerId: string; passengerName: string; pickup: string; destination: string
    pickupLat?: number; pickupLng?: number; destinationLat?: number; destinationLng?: number
  }) =>
    ride<Ride>('/api/rides', json(body)),
  get: (rideId: string) => ride<Ride>(`/api/rides/${rideId}`),
  list: (filter: { passengerId?: string; driverId?: string; limit?: number }) => {
    const query = new URLSearchParams()
    if (filter.passengerId) query.set('passengerId', filter.passengerId)
    if (filter.driverId) query.set('driverId', filter.driverId)
    query.set('limit', String(filter.limit ?? 50))
    return ride<Ride[]>(`/api/rides?${query}`)
  },
  active: (filter: { passengerId?: string; driverId?: string }) =>
    ride<Ride | undefined>(`/api/rides/active?${new URLSearchParams(filter as Record<string, string>)}`),
  start: (rideId: string, driverId: string) => ride<Ride>(`/api/rides/${rideId}/start`, json({ driverId })),
  complete: (rideId: string, driverId: string) => ride<Ride>(`/api/rides/${rideId}/complete`, json({ driverId })),
  cancel: (rideId: string, passengerId: string) =>
    ride<Ride>(`/api/rides/${rideId}/cancel`, json({ passengerId, reason: 'Cancelled by passenger' })),
}

export const driversApi = {
  list: () => driver<Driver[]>('/api/drivers'),
  get: (driverId: string) => driver<Driver>(`/api/drivers/${driverId}`),
  setAvailability: (driverId: string, isAvailable: boolean) =>
    driver<Driver>(`/api/drivers/${driverId}/availability`, { method: 'PUT', body: JSON.stringify({ isAvailable }) }),
  pendingOffers: (driverId: string) => driver<Offer[]>(`/api/drivers/${driverId}/offers?status=Pending`),
  accept: (offerId: string, driverId: string) => driver<Offer>(`/api/offers/${offerId}/accept`, json({ driverId })),
  reject: (offerId: string, driverId: string) => driver<Offer>(`/api/offers/${offerId}/reject`, json({ driverId })),
  resetDemo: () => driver<void>('/api/dev/reset', { method: 'POST' }),
}

