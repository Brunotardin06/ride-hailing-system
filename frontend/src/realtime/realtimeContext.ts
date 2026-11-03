import { createContext, useContext } from 'react'
import type { ChatMessage, Notification } from '../api/types'

export type Role = 'passenger' | 'driver'
export type ConnectionStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

export interface RealtimeContextValue {
  status: ConnectionStatus
  /** Most recent notifications, newest first. */
  notifications: Notification[]
  /** Subscribe to incoming notifications; returns an unsubscribe function. */
  onNotification: (listener: (n: Notification) => void) => () => void
  onChatMessage: (listener: (m: ChatMessage) => void) => () => void
  /** Join the chat group of a ride; resolves with the chat history. */
  joinRideChat: (rideId: string) => Promise<ChatMessage[]>
  leaveRideChat: (rideId: string) => Promise<void>
  sendChat: (rideId: string, text: string) => Promise<ChatMessage>
}

export const RealtimeContext = createContext<RealtimeContextValue | null>(null)

export function useRealtime() {
  const value = useContext(RealtimeContext)
  if (!value) throw new Error('useRealtime must be used inside <RealtimeProvider>')
  return value
}
