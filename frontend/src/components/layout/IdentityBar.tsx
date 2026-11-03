import { useRealtime } from '../../realtime/realtimeContext'
import Icon from '../ui/Icon'

const statusText = {
  connecting: 'Connecting…',
  connected: 'Live',
  reconnecting: 'Reconnecting…',
  disconnected: 'Offline – retrying',
} as const

/** "Who am I in this tab" selector plus the SignalR connection state. */
export default function IdentityBar({ label, value, options, onChange }: {
  label: string
  value: string
  options: { value: string; label: string }[]
  onChange: (value: string) => void
}) {
  const { status } = useRealtime()
  return (
    <div className="identity-bar">
      <label className="identity-select">
        <Icon name="user" />
        <span className="identity-label">{label}</span>
        <select value={value} onChange={(e) => onChange(e.target.value)} aria-label={`Select ${label.toLowerCase()}`}>
          {options.length === 0 && <option value={value}>Loading…</option>}
          {options.map((o) => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
      </label>
      <span className={`connection connection--${status}`}>
        <span className="connection-dot" />
        {statusText[status]}
      </span>
    </div>
  )
}
