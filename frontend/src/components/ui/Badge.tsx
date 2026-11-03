import type { ReactNode } from 'react'

type BadgeTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger'

export default function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  return <span className={`badge badge--${tone}`}>{children}</span>
}
