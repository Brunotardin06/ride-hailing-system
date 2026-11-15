import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type { ChatMessage, DriverLocation, Notification } from '../api/types'
import { serviceUrls } from '../config'
import { RealtimeContext, type ConnectionStatus, type RealtimeContextValue, type Role } from './realtimeContext'

type Listener<T> = (value: T) => void

/**
 * One SignalR (WebSocket) connection to RealtimeService per browser tab.
 * - Registers the tab as a passenger or driver, so the server can push to "passenger:<id>" / "driver:<id>".
 * - Acknowledges every notification and chat message it receives; RealtimeService uses the
 *   acknowledgement to measure the delivery round trip.
 * - Re-registers and re-joins chats after an automatic reconnect (a new connection has no groups).
 * - Driver: sends its simulated GPS position (UpdateLocation). Passenger: receives the driver's position.
 */
export function RealtimeProvider({ role, userId, userName, children }: {
  role: Role
  userId: string
  userName: string
  children: ReactNode
}) {
  const [status, setStatus] = useState<ConnectionStatus>('connecting')
  const [notifications, setNotifications] = useState<Notification[]>([])
  const connectionRef = useRef<HubConnection | null>(null)
  const notificationListeners = useRef(new Set<Listener<Notification>>())
  const chatListeners = useRef(new Set<Listener<ChatMessage>>())
  const locationListeners = useRef(new Set<Listener<DriverLocation>>())
  const joinedChats = useRef(new Set<string>())

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${serviceUrls.realtime}/hubs/rides`)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 10000])
      .configureLogging(LogLevel.Warning)
      .build()
    connectionRef.current = connection
    let disposed = false
    let starting: Promise<void> | null = null

    const acknowledge = (id: string) => {
      connection.invoke('Acknowledge', id).catch(() => { /* connection lost; nothing to measure */ })
    }

    connection.on('notification', (notification: Notification) => {
      acknowledge(notification.id)
      setNotifications((list) => [notification, ...list].slice(0, 30))
      notificationListeners.current.forEach((listener) => listener(notification))
    })
    connection.on('chatMessage', (message: ChatMessage) => {
      acknowledge(message.id)
      chatListeners.current.forEach((listener) => listener(message))
    })
    // Not acknowledged: a new position arrives every ~2 s anyway.
    connection.on('driverLocation', (location: DriverLocation) => {
      locationListeners.current.forEach((listener) => listener(location))
    })

    const register = async () => {
      await connection.invoke('Register', role, userId)
      for (const rideId of joinedChats.current) await connection.invoke('JoinRideChat', rideId)
    }

    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(async () => {
      await register()
      setStatus('connected')
    })
    connection.onclose(() => { if (!disposed) setStatus('disconnected') })

    const start = async () => {
      try {
        starting = connection.start()
        await starting
        if (disposed) return
        await register()
        setStatus('connected')
      } catch {
        if (disposed) return
        setStatus('disconnected')
        setTimeout(() => { if (!disposed) void start() }, 3000) // RealtimeService not up yet: keep trying
      }
    }
    // Start on the next tick: React StrictMode (dev) mounts, unmounts and re-mounts immediately,
    // and stopping a connection during negotiation logs errors.
    const startTimer = setTimeout(() => void start(), 0)

    return () => {
      clearTimeout(startTimer)
      disposed = true
      connectionRef.current = null
      // Stopping while the connection is still negotiating logs errors, so wait for start() to settle.
      void (starting ?? Promise.resolve()).catch(() => {}).finally(() => connection.stop())
    }
  }, [role, userId])

  const ensureConnected = () => {
    const connection = connectionRef.current
    if (!connection || connection.state !== HubConnectionState.Connected) throw new Error('Not connected. Please try again in a moment.')
    return connection
  }

  const onNotification = useCallback((listener: Listener<Notification>) => {
    notificationListeners.current.add(listener)
    return () => { notificationListeners.current.delete(listener) }
  }, [])

  const onChatMessage = useCallback((listener: Listener<ChatMessage>) => {
    chatListeners.current.add(listener)
    return () => { chatListeners.current.delete(listener) }
  }, [])

  const joinRideChat = useCallback(async (rideId: string) => {
    joinedChats.current.add(rideId)
    return ensureConnected().invoke<ChatMessage[]>('JoinRideChat', rideId)
  }, [])

  const leaveRideChat = useCallback(async (rideId: string) => {
    joinedChats.current.delete(rideId)
    const connection = connectionRef.current
    if (connection?.state === HubConnectionState.Connected) await connection.invoke('LeaveRideChat', rideId)
  }, [])

  const sendChat = useCallback((rideId: string, text: string) =>
    ensureConnected().invoke<ChatMessage>('SendChatMessage', { rideId, senderRole: role, senderName: userName, text }),
  [role, userName])

  const onDriverLocation = useCallback((listener: Listener<DriverLocation>) => {
    locationListeners.current.add(listener)
    return () => { locationListeners.current.delete(listener) }
  }, [])

  const sendLocation = useCallback((lat: number, lng: number) => {
    const connection = connectionRef.current
    if (connection?.state !== HubConnectionState.Connected) return // offline: skip this tick, the next one follows
    connection.invoke('UpdateLocation', userId, lat, lng).catch(() => { /* lost position is fine */ })
  }, [userId])

  const value = useMemo<RealtimeContextValue>(() => ({
    status, notifications, onNotification, onChatMessage, joinRideChat, leaveRideChat, sendChat, onDriverLocation, sendLocation,
  }), [status, notifications, onNotification, onChatMessage, joinRideChat, leaveRideChat, sendChat, onDriverLocation, sendLocation])

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>
}
