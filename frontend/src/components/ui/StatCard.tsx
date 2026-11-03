type StatCardProps = {
  label: string
  value?: string
  hint?: string
}

// Metric tile. Shows an em dash until a real value is wired in.
export default function StatCard({ label, value = '—', hint }: StatCardProps) {
  return (
    <div className="stat-card">
      <p className="stat-label">{label}</p>
      <p className="stat-value">{value}</p>
      {hint && <p className="stat-hint">{hint}</p>}
    </div>
  )
}
