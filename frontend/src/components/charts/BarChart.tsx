import { useState } from 'react'
import { formatNumber, hBarPath, niceTicks, seriesColors, useWidth } from './chartUtils'
import Legend from './Legend'

export interface BarSeries {
  label: string
  /** One value per category (null = no data). */
  values: (number | null)[]
}

type BarChartProps = {
  categories: string[]
  series: BarSeries[]
  unit?: string
  ariaLabel: string
}

const LABEL_WIDTH = 120
const BAR = 14
const GAP = 2

/**
 * Horizontal (grouped) bar chart: one row per category, one bar per series, all on ONE value axis.
 * Values are labelled at the bar tips; hovering a bar shows a tooltip.
 */
export default function BarChart({ categories, series, unit = '', ariaLabel }: BarChartProps) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const [hover, setHover] = useState<{ c: number; s: number; x: number; y: number } | null>(null)

  const max = Math.max(0, ...series.flatMap((s) => s.values.map((v) => v ?? 0)))
  const ticks = niceTicks(max)
  const top = ticks[ticks.length - 1] || 1
  const plotWidth = Math.max(80, width - LABEL_WIDTH - 56)
  const groupHeight = series.length * (BAR + GAP) - GAP
  const rowHeight = groupHeight + 16
  const height = categories.length * rowHeight + 24
  const x = (v: number) => LABEL_WIDTH + (v / top) * plotWidth

  return (
    <div className="chart" ref={ref}>
      {series.length > 1 && <Legend items={series.map((s, i) => ({ label: s.label, color: seriesColors[i] }))} />}
      <svg width={width} height={height} role="img" aria-label={ariaLabel}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={x(t)} x2={x(t)} y1={0} y2={height - 20} className="chart-grid" />
            <text x={x(t)} y={height - 6} className="chart-tick" textAnchor="middle">{formatNumber(t)}</text>
          </g>
        ))}
        {categories.map((category, c) => {
          const y0 = c * rowHeight + 8
          return (
            <g key={category}>
              <text x={LABEL_WIDTH - 8} y={y0 + groupHeight / 2 + 4} className="chart-label" textAnchor="end">
                {category.length > 18 ? category.slice(0, 17) + '…' : category}
                <title>{category}</title>
              </text>
              {series.map((s, i) => {
                const value = s.values[c]
                if (value == null) return null
                const y = y0 + i * (BAR + GAP)
                const w = Math.max(1, x(value) - LABEL_WIDTH)
                return (
                  <g key={s.label}
                    onMouseEnter={() => setHover({ c, s: i, x: x(value), y })}
                    onMouseLeave={() => setHover(null)}>
                    <rect x={LABEL_WIDTH} y={y - 1} width={plotWidth + 50} height={BAR + 2} fill="transparent" />
                    <path d={hBarPath(LABEL_WIDTH, y, w, BAR)} fill={seriesColors[i]} />
                    <text x={x(value) + 4} y={y + BAR - 3} className="chart-value">{formatNumber(value, unit)}</text>
                  </g>
                )
              })}
            </g>
          )
        })}
        <line x1={LABEL_WIDTH} x2={LABEL_WIDTH} y1={0} y2={height - 20} className="chart-axis" />
      </svg>
      {hover && (
        <div className="chart-tooltip" style={{ left: Math.min(hover.x, width - 160), top: hover.y + BAR + 6 }}>
          <strong>{categories[hover.c]}</strong>
          <span>{series[hover.s].label}: {formatNumber(series[hover.s].values[hover.c] ?? 0, unit)}</span>
        </div>
      )}
    </div>
  )
}
