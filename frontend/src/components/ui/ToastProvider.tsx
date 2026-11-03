import { useCallback, useState, type ReactNode } from 'react'
import { ToastContext, type Toast, type ToastTone } from '../../state/toastContext'

let nextId = 1

export default function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const show = useCallback((message: string, tone: ToastTone = 'info') => {
    const id = nextId++
    setToasts((list) => [...list, { id, tone, message }].slice(-4))
    setTimeout(() => setToasts((list) => list.filter((t) => t.id !== id)), tone === 'error' ? 7000 : 4000)
  }, [])

  return (
    <ToastContext.Provider value={show}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`toast toast--${t.tone}`}>{t.message}</div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
