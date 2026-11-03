import { useState } from 'react'
import { formatNumber, niceTicks, seriesColors, useWidth, vBarPath } from './chartUtils'

type ColumnChartProps = {
  labels: string[]
  values: number[]
  ariaLabel: string
}

const LEFT = 36
const HEIGHT = 180
const BOTTOM = 22

/** Single-series column chart (e.g. events per minute) with hover tooltips and a label on the highest column. */
export default function ColumnChart({ labels, values, ariaLabel }: ColumnChartProps) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const [hover, setHover] = useState<number | null>(null)

  if (values.length === 0) return <div className="chart chart--empty" ref={ref}>No events in this period.</div>

  const ticks = niceTicks(Math.max(...values), 3)
  const top = ticks[ticks.length - 1] || 1
  const plotW = width - LEFT - 8
  const plotH = HEIGHT - BOTTOM - 16
  const slot = plotW / values.length
  const bar = Math.min(24, Math.max(3, slot - 2))
  const baseline = 16 + plotH
  const y = (v: number) => baseline - (v / top) * plotH
  const peak = values.indexOf(Math.max(...values))

  return (
    <div className="chart" ref={ref}>
      <svg width={width} height={HEIGHT} role="img" aria-label={ariaLabel}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={LEFT} x2={width - 8} y1={y(t)} y2={y(t)} className="chart-grid" />
            <text x={LEFT - 6} y={y(t) + 4} className="chart-tick" textAnchor="end">{formatNumber(t)}</text>
          </g>
        ))}
        {values.map((v, i) => {
          const cx = LEFT + i * slot + (slot - bar) / 2
          return (
            <g key={labels[i]} onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}>
              <rect x={LEFT + i * slot} y={16} width={slot} height={plotH} fill="transparent" />
              {v > 0 && <path d={vBarPath(cx, baseline, bar, baseline - y(v))} fill={seriesColors[0]} opacity={hover == null || hover === i ? 1 : 0.6} />}
              {i === peak && v > 0 && <text x={cx + bar / 2} y={y(v) - 4} className="chart-value" textAnchor="middle">{v}</text>}
            </g>
          )
        })}
        <text x={LEFT} y={HEIGHT - 6} className="chart-tick">{labels[0]}</text>
        <text x={width - 8} y={HEIGHT - 6} className="chart-tick" textAnchor="end">{labels[labels.length - 1]}</text>
      </svg>
      {hover != null && (
        <div className="chart-tooltip" style={{ left: Math.min(LEFT + hover * slot, width - 140), top: 0 }}>
          <strong>{labels[hover]}</strong>
          <span>{values[hover]} events</span>
        </div>
      )}
    </div>
  )
}
