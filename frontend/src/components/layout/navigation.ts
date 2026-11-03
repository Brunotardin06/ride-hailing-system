import type { IconName } from '../ui/Icon'

export type NavItem = { to: string; label: string; icon: IconName }
export type NavSection = { title: string; items: NavItem[] }

// Single source of truth for the sidebar navigation.
export const navSections: NavSection[] = [
  {
    title: 'Passenger',
    items: [
      { to: '/passenger/request', label: 'Request ride', icon: 'pin' },
      { to: '/passenger/current', label: 'Current ride', icon: 'car' },
      { to: '/passenger/history', label: 'History', icon: 'clock' },
      { to: '/passenger/chat', label: 'Chat', icon: 'chat' },
    ],
  },
  {
    title: 'Driver',
    items: [
      { to: '/driver/dashboard', label: 'Dashboard', icon: 'grid' },
      { to: '/driver/requests', label: 'Incoming requests', icon: 'inbox' },
      { to: '/driver/active', label: 'Active ride', icon: 'car' },
      { to: '/driver/chat', label: 'Chat', icon: 'chat' },
    ],
  },
  // Experiments section temporarily hidden (the /experiments route and its code still exist). To restore:
  // { title: 'Experiments', items: [{ to: '/experiments', label: 'Experiment dashboard', icon: 'chart' }] },
]
