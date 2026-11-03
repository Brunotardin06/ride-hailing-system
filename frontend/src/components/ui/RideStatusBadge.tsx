import type { RideStatus } from '../../api/types'
import Badge from './Badge'

const labels: Record<RideStatus, [string, 'neutral' | 'info' | 'success' | 'warning' | 'danger']> = {
  Requested: ['Searching driver', 'warning'],
  Assigned: ['Driver on the way', 'info'],
  InProgress: ['In progress', 'info'],
  Completed: ['Completed', 'success'],
  Cancelled: ['Cancelled', 'neutral'],
  NoDriverAvailable: ['No driver', 'danger'],
}

export default function RideStatusBadge({ status }: { status: RideStatus }) {
  const [label, tone] = labels[status]
  return <Badge tone={tone}>{label}</Badge>
}
