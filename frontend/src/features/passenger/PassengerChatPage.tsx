import ChatPanel from '../../components/ui/ChatPanel'
import PageHeader from '../../components/ui/PageHeader'
import { usePassenger } from '../../state/areaContexts'

export default function PassengerChatPage() {
  const { currentRide } = usePassenger()
  const chatOpen = currentRide && (currentRide.status === 'Assigned' || currentRide.status === 'InProgress')

  return (
    <>
      <PageHeader eyebrow="Passenger" title="Chat" description="Talk to your driver during the ride." />
      <ChatPanel role="passenger" rideId={chatOpen ? currentRide.id : null} counterpart={currentRide?.driverName ?? 'your driver'} />
    </>
  )
}
