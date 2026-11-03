import { useEffect, useRef, useState } from 'react'

/**
 * Categorical series colours in fixed order (validated for colour-vision deficiencies, light surface).
 * Slot 1 is always the first series, slot 2 the second, … — never cycled or re-ranked.
 * Some slots are below 3:1 contrast, so charts always show direct value labels and a table view.
 */
export const seriesColors = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100'] as const

/** Reserved status colour for failures (always paired with a text label). */
export const criticalColor = '#d03b3b'

/** Width of an element, kept up to date with a ResizeObserver. */
export function useWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(600)
  useEffect(() => {
    if (!ref.current) return
    const observer = new ResizeObserver(([entry]) => setWidth(Math.max(240, Math.floor(entry.contentRect.width))))
    observer.observe(ref.current)
    return () => observer.disconnect()
  }, [])
  return [ref, width] as const
}

/** Clean axis ticks from 0 to ≥ max. */
export function niceTicks(max: number, count = 4): number[] {
  if (!(max > 0)) return [0, 1]
  const rough = max / count
  const magnitude = 10 ** Math.floor(Math.log10(rough))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= rough) ?? rough
  const ticks: number[] = []
  for (let v = 0; v <= max + step * 0.001; v += step) ticks.push(Number(v.toPrecision(12)))
  if (ticks[ticks.length - 1] < max) ticks.push(Number((ticks[ticks.length - 1] + step).toPrecision(12)))
  return ticks
}

export function formatNumber(value: number, unit = ''): string {
  const abs = Math.abs(value)
  const digits = abs >= 100 ? 0 : abs >= 10 ? 1 : 2
  return `${value.toLocaleString(undefined, { maximumFractionDigits: digits })}${unit}`
}

/** SVG path for a horizontal bar with a 4px rounded data end and a square baseline. */
export function hBarPath(x: number, y: number, width: number, height: number, r = 4) {
  const rr = Math.min(r, width, height / 2)
  return `M${x},${y} h${width - rr} a${rr},${rr} 0 0 1 ${rr},${rr} v${height - 2 * rr} a${rr},${rr} 0 0 1 -${rr},${rr} h-${width - rr} z`
}

/** SVG path for a vertical column with a 4px rounded top and a square baseline. */
export function vBarPath(x: number, baseline: number, width: number, height: number, r = 4) {
  const rr = Math.min(r, height, width / 2)
  const top = baseline - height
  return `M${x},${baseline} V${top + rr} a${rr},${rr} 0 0 1 ${rr},-${rr} h${width - 2 * rr} a${rr},${rr} 0 0 1 ${rr},${rr} V${baseline} z`
}
