import type { Notification } from '../../api/types'
import EmptyState from './EmptyState'

/** List of received SignalR notifications, newest first. */
export default function LiveFeed({ notifications, empty }: { notifications: Notification[]; empty: string }) {
  if (notifications.length === 0) return <EmptyState icon="bolt" title="Nothing yet" message={empty} />
  return (
    <ul className="feed">
      {notifications.map((n) => (
        <li key={n.id} className="feed-item">
          <span className="feed-type">{n.type}</span>
          <div>
            <p className="feed-title">{n.title}</p>
            <p className="feed-message">{n.message}</p>
          </div>
          <time className="feed-time">{new Date(n.sentAt).toLocaleTimeString()}</time>
        </li>
      ))}
    </ul>
  )
}
