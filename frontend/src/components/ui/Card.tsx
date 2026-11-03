import type { ReactNode } from 'react'

type CardProps = {
  title?: string
  subtitle?: string
  aside?: ReactNode
  className?: string
  children: ReactNode
}

export default function Card({ title, subtitle, aside, className = '', children }: CardProps) {
  return (
    <section className={`card ${className}`}>
      {(title || aside) && (
        <div className="card-header">
          <div>
            {title && <h2 className="card-title">{title}</h2>}
            {subtitle && <p className="card-subtitle">{subtitle}</p>}
          </div>
          {aside}
        </div>
      )}
      {children}
    </section>
  )
}
