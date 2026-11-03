import Icon, { type IconName } from './Icon'

type EmptyStateProps = {
  icon: IconName
  title: string
  message: string
}

// Placeholder shown wherever real data will appear once the backend is connected.
export default function EmptyState({ icon, title, message }: EmptyStateProps) {
  return (
    <div className="empty-state">
      <span className="empty-icon">
        <Icon name={icon} size={22} />
      </span>
      <p className="empty-title">{title}</p>
      <p className="empty-message">{message}</p>
    </div>
  )
}
