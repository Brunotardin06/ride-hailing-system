import { Link } from 'react-router-dom'
import Icon, { type IconName } from '../components/ui/Icon'
import PageHeader from '../components/ui/PageHeader'

const areas: { to: string; icon: IconName; title: string; text: string }[] = [
  {
    to: '/passenger/request',
    icon: 'user',
    title: 'Passenger',
    text: 'Request a ride, follow the current ride, view history and chat with the driver.',
  },
  {
    to: '/driver/dashboard',
    icon: 'car',
    title: 'Driver',
    text: 'Go online, receive incoming ride requests, manage the active ride and chat.',
  },
  // Experiment dashboard card temporarily hidden (route /experiments still exists). To restore, add back:
  // { to: '/experiments', icon: 'chart', title: 'Experiment dashboard',
  //   text: 'Observe events and metrics and compare REST, gRPC, RabbitMQ, Kafka and SignalR.' },
]

export default function HomePage() {
  return (
    <>
      <PageHeader
        eyebrow="Personal Experiment 1"
        title="Distributed ride-hailing system"
        description="A simplified ride-hailing platform built to study and compare service communication mechanisms."
      />

      <div className="grid grid--split">
        {areas.map((area) => (
          <Link key={area.to} to={area.to} className="card card--link">
            <span className="feature-icon">
              <Icon name={area.icon} size={22} />
            </span>
            <h2 className="card-title">{area.title}</h2>
            <p className="card-subtitle">{area.text}</p>
            <span className="card-cta">
              Open <Icon name="arrow" size={16} />
            </span>
          </Link>
        ))}
      </div>
    </>
  )
}
