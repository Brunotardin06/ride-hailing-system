import { useState, type MouseEvent } from 'react'
import { criticalColor, formatNumber, niceTicks, seriesColors, useWidth } from './chartUtils'

export interface LinePoint {
  time: number // epoch ms
  value: number
  failed?: boolean
}

type LineChartProps = {
  points: LinePoint[]
  unit?: string
  ariaLabel: string
}

const LEFT = 48
const RIGHT = 16
const HEIGHT = 220
const BOTTOM = 24

/**
 * Single-series line over time with a hover crosshair + tooltip. Failed samples are drawn as
 * red markers (labelled "failed" in the tooltip and the note below the chart).
 */
export default function LineChart({ points, unit = '', ariaLabel }: LineChartProps) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const [hover, setHover] = useState<number | null>(null)

  if (points.length === 0) return <div className="chart chart--empty" ref={ref}>No data for this metric yet.</div>

  const t0 = points[0].time
  const t1 = points[points.length - 1].time
  const ticks = niceTicks(Math.max(...points.map((p) => p.value)))
  const top = ticks[ticks.length - 1] || 1
  const plotW = width - LEFT - RIGHT
  const plotH = HEIGHT - BOTTOM - 8
  const x = (t: number) => LEFT + (t1 === t0 ? plotW / 2 : ((t - t0) / (t1 - t0)) * plotW)
  const y = (v: number) => 8 + plotH - (v / top) * plotH

  const ok = points.filter((p) => !p.failed)
  const path = ok.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.time).toFixed(1)},${y(p.value).toFixed(1)}`).join(' ')
  const last = ok[ok.length - 1]

  const onMove = (e: MouseEvent<SVGSVGElement>) => {
    const px = e.clientX - e.currentTarget.getBoundingClientRect().left
    let nearest = 0
    points.forEach((p, i) => { if (Math.abs(x(p.time) - px) < Math.abs(x(points[nearest].time) - px)) nearest = i })
    setHover(nearest)
  }
  const h = hover == null ? null : points[hover]
  const failures = points.length - ok.length

  return (
    <div className="chart" ref={ref}>
      <svg width={width} height={HEIGHT} role="img" aria-label={ariaLabel} onMouseMove={onMove} onMouseLeave={() => setHover(null)}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={LEFT} x2={width - RIGHT} y1={y(t)} y2={y(t)} className="chart-grid" />
            <text x={LEFT - 6} y={y(t) + 4} className="chart-tick" textAnchor="end">{formatNumber(t)}</text>
          </g>
        ))}
        <text x={LEFT} y={HEIGHT - 6} className="chart-tick">{new Date(t0).toLocaleTimeString()}</text>
        <text x={width - RIGHT} y={HEIGHT - 6} className="chart-tick" textAnchor="end">{new Date(t1).toLocaleTimeString()}</text>
        <path d={path} fill="none" stroke={seriesColors[0]} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
        {points.filter((p) => p.failed).map((p) => (
          <circle key={p.time} cx={x(p.time)} cy={y(p.value)} r={4} fill={criticalColor} stroke="#fff" strokeWidth={2} />
        ))}
        {last && (
          <>
            <circle cx={x(last.time)} cy={y(last.value)} r={4} fill={seriesColors[0]} stroke="#fff" strokeWidth={2} />
            <text x={Math.min(x(last.time) + 6, width - RIGHT - 40)} y={y(last.value) - 8} className="chart-value">{formatNumber(last.value, unit)}</text>
          </>
        )}
        {h && (
          <>
            <line x1={x(h.time)} x2={x(h.time)} y1={8} y2={8 + plotH} className="chart-crosshair" />
            <circle cx={x(h.time)} cy={y(h.value)} r={5} fill={h.failed ? criticalColor : seriesColors[0]} stroke="#fff" strokeWidth={2} />
          </>
        )}
      </svg>
      {h && (
        <div className="chart-tooltip" style={{ left: Math.min(Math.max(x(h.time) - 70, 0), width - 160), top: 4 }}>
          <strong>{new Date(h.time).toLocaleTimeString()}</strong>
          <span>{formatNumber(h.value, unit)}{h.failed ? ' · failed' : ''}</span>
        </div>
      )}
      {failures > 0 && <p className="chart-note"><span className="legend-swatch" style={{ background: criticalColor }} /> {failures} failed sample(s)</p>}
    </div>
  )
}
