import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { ChatMessage } from '../../api/types'
import { useRealtime, type Role } from '../../realtime/realtimeContext'
import { errorMessage, useToast } from '../../state/toastContext'
import Badge from './Badge'
import Card from './Card'
import EmptyState from './EmptyState'
import Icon from './Icon'

type ChatPanelProps = {
  role: Role
  /** Ride whose chat to show; null when there is no ride to chat about. */
  rideId: string | null
  counterpart: string
}

/** Two-way chat for one ride, over SignalR (RealtimeService group "ride:<id>"). */
export default function ChatPanel({ role, rideId, counterpart }: ChatPanelProps) {
  const { status, joinRideChat, leaveRideChat, sendChat, onChatMessage } = useRealtime()
  const toast = useToast()
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [text, setText] = useState('')
  const [sending, setSending] = useState(false)
  const listRef = useRef<HTMLDivElement>(null)
  const connected = status === 'connected'

  // Join the ride's chat group (and load its history) when connected.
  useEffect(() => {
    if (!rideId || !connected) return
    let cancelled = false
    joinRideChat(rideId)
      .then((history) => { if (!cancelled) setMessages(history) })
      .catch((error) => toast(errorMessage(error), 'error'))
    return () => {
      cancelled = true
      void leaveRideChat(rideId).catch(() => {})
    }
  }, [rideId, connected, joinRideChat, leaveRideChat, toast])

  useEffect(() => onChatMessage((message) => {
    if (message.rideId === rideId) setMessages((list) => (list.some((m) => m.id === message.id) ? list : [...list, message]))
  }), [onChatMessage, rideId])

  useEffect(() => {
    listRef.current?.scrollTo({ top: listRef.current.scrollHeight, behavior: 'smooth' })
  }, [messages])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!rideId || !text.trim()) return
    setSending(true)
    try {
      const sent = await sendChat(rideId, text.trim())
      setMessages((list) => [...list, sent])
      setText('')
    } catch (error) {
      toast(errorMessage(error), 'error')
    } finally {
      setSending(false)
    }
  }

  return (
    <Card
      className="chat"
      title={`Chat with ${counterpart}`}
      subtitle={rideId ? 'Messages are delivered live' : 'Available while you have an assigned ride'}
      aside={<Badge tone={connected ? 'success' : 'neutral'}>{connected ? 'Connected' : 'Not connected'}</Badge>}
    >
      <div className="chat-messages" ref={listRef}>
        {messages.length === 0 ? (
          <EmptyState
            icon="chat"
            title={rideId ? 'No messages yet' : 'No ride to chat about'}
            message={rideId ? 'Say hello! Messages appear here instantly.' : 'Chat opens as soon as a driver accepts the ride.'}
          />
        ) : (
          messages.map((m) => (
            <div key={m.id} className={`bubble ${m.senderRole === role ? 'bubble--own' : ''}`}>
              <span className="bubble-meta">{m.senderName} · {new Date(m.sentAt).toLocaleTimeString()}</span>
              {m.text}
            </div>
          ))
        )}
      </div>
      <form className="chat-input" onSubmit={submit}>
        <input
          type="text"
          placeholder={rideId ? 'Type a message…' : 'No active ride'}
          value={text}
          maxLength={500}
          onChange={(e) => setText(e.target.value)}
          disabled={!rideId || !connected}
          aria-label="Message"
        />
        <button type="submit" className="button button--primary" disabled={!rideId || !connected || sending || !text.trim()} aria-label="Send">
          <Icon name="send" />
        </button>
      </form>
    </Card>
  )
}
