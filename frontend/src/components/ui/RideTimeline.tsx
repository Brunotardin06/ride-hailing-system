import type { Ride, RideStatus } from '../../api/types'

const steps: { status: RideStatus; label: string; time: (r: Ride) => string | null }[] = [
  { status: 'Requested', label: 'Requested', time: (r) => r.requestedAt },
  { status: 'Assigned', label: 'Driver assigned', time: (r) => r.assignedAt },
  { status: 'InProgress', label: 'In progress', time: (r) => r.startedAt },
  { status: 'Completed', label: 'Completed', time: (r) => r.completedAt },
]

const order: RideStatus[] = ['Requested', 'Assigned', 'InProgress', 'Completed']

const formatTime = (iso: string | null) => (iso ? new Date(iso).toLocaleTimeString() : '')

/** The ride lifecycle; reached steps are highlighted. Cancelled / no-driver end the timeline early. */
export default function RideTimeline({ ride }: { ride: Ride | null }) {
  const reached = ride ? order.indexOf(ride.status) : -1
  const lastNormal = ride && reached === -1 ? order.indexOf(ride.assignedAt ? 'Assigned' : 'Requested') : reached
  const current = ride && reached !== -1 && ride.status !== 'Completed' ? reached : -1

  return (
    <ol className="timeline">
      {steps.map((step, index) => (
        <li
          key={step.status}
          className={`timeline-step ${index <= lastNormal ? 'timeline-step--done' : ''} ${index === current ? 'timeline-step--current' : ''}`}
        >
          <span className="timeline-dot" />
          <span className="timeline-label">{step.label}</span>
          {ride && index <= lastNormal && <span className="timeline-time">{formatTime(step.time(ride))}</span>}
        </li>
      ))}
      {ride && (ride.status === 'Cancelled' || ride.status === 'NoDriverAvailable') && (
        <li className="timeline-step timeline-step--failed">
          <span className="timeline-dot" />
          <span className="timeline-label">{ride.status === 'Cancelled' ? 'Cancelled' : 'No driver available'}</span>
          <span className="timeline-time">{ride.statusReason}</span>
        </li>
      )}
    </ol>
  )
}
