import { createContext, useContext } from 'react'

export type ToastTone = 'success' | 'error' | 'info'
export interface Toast { id: number; tone: ToastTone; message: string }

export const ToastContext = createContext<((message: string, tone?: ToastTone) => void) | null>(null)

/** Show a short feedback message: `toast('Ride requested', 'success')`. */
export function useToast() {
  const toast = useContext(ToastContext)
  if (!toast) throw new Error('useToast must be used inside <ToastProvider>')
  return toast
}

/** Turn any thrown value into a readable message. */
export const errorMessage = (error: unknown) => (error instanceof Error ? error.message : String(error))
