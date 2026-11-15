import { Navigate, Route, Routes } from 'react-router-dom'
import AppLayout from './components/layout/AppLayout'
import ToastProvider from './components/ui/ToastProvider'
import HomePage from './pages/HomePage'
import NotFoundPage from './pages/NotFoundPage'
import PassengerArea from './features/passenger/PassengerArea'
import RequestRidePage from './features/passenger/RequestRidePage'
import CurrentRidePage from './features/passenger/CurrentRidePage'
import RideHistoryPage from './features/passenger/RideHistoryPage'
import PassengerChatPage from './features/passenger/PassengerChatPage'
import DriverArea from './features/driver/DriverArea'
import DriverDashboardPage from './features/driver/DriverDashboardPage'
import IncomingRequestsPage from './features/driver/IncomingRequestsPage'
import ActiveRidePage from './features/driver/ActiveRidePage'
import DriverChatPage from './features/driver/DriverChatPage'

export default function App() {
  return (
    <ToastProvider>
      <Routes>
        <Route element={<AppLayout />}>
          <Route index element={<HomePage />} />

          {/* Each area keeps one SignalR connection and shared state for its pages. */}
          <Route path="passenger" element={<PassengerArea />}>
            <Route index element={<Navigate to="request" replace />} />
            <Route path="request" element={<RequestRidePage />} />
            <Route path="current" element={<CurrentRidePage />} />
            <Route path="history" element={<RideHistoryPage />} />
            <Route path="chat" element={<PassengerChatPage />} />
          </Route>

          <Route path="driver" element={<DriverArea />}>
            <Route index element={<Navigate to="dashboard" replace />} />
            <Route path="dashboard" element={<DriverDashboardPage />} />
            <Route path="requests" element={<IncomingRequestsPage />} />
            <Route path="active" element={<ActiveRidePage />} />
            <Route path="chat" element={<DriverChatPage />} />
          </Route>


          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    </ToastProvider>
  )
}
