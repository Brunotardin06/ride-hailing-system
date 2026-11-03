// Backend service base URLs, read from Vite environment variables (see .env.example).
// Nothing calls these yet; they are here so API/SignalR clients have one place to read from later.
export const serviceUrls = {
  ride: import.meta.env.VITE_RIDE_SERVICE_URL ?? 'http://localhost:5001',
  driver: import.meta.env.VITE_DRIVER_SERVICE_URL ?? 'http://localhost:5002',
  matching: import.meta.env.VITE_MATCHING_SERVICE_URL ?? 'http://localhost:5003',
  realtime: import.meta.env.VITE_REALTIME_SERVICE_URL ?? 'http://localhost:5004',
  analytics: import.meta.env.VITE_ANALYTICS_SERVICE_URL ?? 'http://localhost:5005',
} as const
