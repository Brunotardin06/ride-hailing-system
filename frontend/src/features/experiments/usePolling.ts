import { useCallback, useEffect, useState } from 'react'
import { errorMessage } from '../../state/toastContext'

/** Loads data now and every `intervalMs` (when `auto` is on). Returns the latest data and errors. */
export function usePolling<T>(load: () => Promise<T>, intervalMs: number, auto = true) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      setData(await load())
      setError(null)
    } catch (e) {
      setError(errorMessage(e))
    }
  }, [load])

  useEffect(() => {
    void reload()
    if (!auto) return
    const timer = setInterval(() => void reload(), intervalMs)
    return () => clearInterval(timer)
  }, [reload, intervalMs, auto])

  return { data, error, reload }
}
