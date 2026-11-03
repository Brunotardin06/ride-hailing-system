/// <reference types="vite/client" />

// Typed access to the VITE_* variables defined in .env (see .env.example).
interface ImportMetaEnv {
  readonly VITE_RIDE_SERVICE_URL?: string
  readonly VITE_DRIVER_SERVICE_URL?: string
  readonly VITE_MATCHING_SERVICE_URL?: string
  readonly VITE_REALTIME_SERVICE_URL?: string
  readonly VITE_ANALYTICS_SERVICE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
