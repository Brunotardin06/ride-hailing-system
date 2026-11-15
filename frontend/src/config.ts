// Backend service base URLs, read from Vite environment variables (see .env.example).
export const serviceUrls = {
  ride: import.meta.env.VITE_RIDE_SERVICE_URL ?? 'http://localhost:5001',
  driver: import.meta.env.VITE_DRIVER_SERVICE_URL ?? 'http://localhost:5002',
  realtime: import.meta.env.VITE_REALTIME_SERVICE_URL ?? 'http://localhost:5004',
} as const
