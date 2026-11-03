import ChatPanel from '../../components/ui/ChatPanel'
import PageHeader from '../../components/ui/PageHeader'
import { useDriver } from '../../state/areaContexts'

export default function DriverChatPage() {
  const { activeRide } = useDriver()

  return (
    <>
      <PageHeader eyebrow="Driver" title="Chat" description="Talk to your passenger during the ride." />
      <ChatPanel role="driver" rideId={activeRide?.id ?? null} counterpart={activeRide?.passengerName ?? 'your passenger'} />
    </>
  )
}
