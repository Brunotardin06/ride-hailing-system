import { useCallback, useMemo, useState } from 'react'
import { analyticsApi } from '../../api/client'
import type { MetricSummary } from '../../api/types'
import BarChart from '../../components/charts/BarChart'
import LineChart from '../../components/charts/LineChart'
import { formatNumber } from '../../components/charts/chartUtils'
import Badge from '../../components/ui/Badge'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import StatCard from '../../components/ui/StatCard'
import { errorMessage, useToast } from '../../state/toastContext'
import { usePolling } from './usePolling'

const kindLabels: Record<string, string> = {
  call: 'Synchronous call (REST / gRPC)',
  delivery: 'Message delivery (publish → receive)',
  processing: 'Consumer processing',
  'ack-rtt': 'SignalR delivery round trip',
  recovery: 'Recovery after outage',
}

const windows = [
  { label: 'Last 15 min', minutes: 15 },
  { label: 'Last hour', minutes: 60 },
  { label: 'All', minutes: 0 },
]

const key = (m: MetricSummary) => `${m.mechanism}|${m.kind}|${m.operation}`
const ms = (v: number | null) => (v == null ? '—' : formatNumber(v, ' ms'))

export default function MetricsTab() {
  const toast = useToast()
  const [minutes, setMinutes] = useState(60)
  const [kind, setKind] = useState('call')
  const [selected, setSelected] = useState<string | null>(null)

  const loadSummary = useCallback(() => analyticsApi.metrics(minutes || undefined), [minutes])
  const { data: summary, error, reload } = usePolling(loadSummary, 5000)
  const { data: definitions } = usePolling(analyticsApi.definitions, 60_000, false)

  const rows = useMemo(() => summary ?? [], [summary])
  const kinds = useMemo(() => [...new Set(rows.map((r) => r.kind))], [rows])
  const ofKind = rows.filter((r) => r.kind === kind)
  const selectedRow = rows.find((r) => key(r) === selected) ?? ofKind[0] ?? null

  const seriesKey = selectedRow ? key(selectedRow) : null
  const loadSeries = useCallback(() => {
    if (!seriesKey) return Promise.resolve([])
    const [mechanism, kind, operation] = seriesKey.split('|')
    return analyticsApi.series({ mechanism, kind, operation }, 300)
  }, [seriesKey])
  const { data: series } = usePolling(loadSeries, 5000)

  const total = rows.reduce((s, r) => s + r.count, 0)
  const errors = rows.reduce((s, r) => s + r.errors, 0)
  const byMechanism = (m: string) => rows.filter((r) => r.mechanism === m).reduce((s, r) => s + r.count, 0)

  const clear = async () => {
    if (!confirm('Delete all live measurements? Experiment runs are kept.')) return
    try {
      const { deleted } = await analyticsApi.clearLiveMetrics()
      toast(`Deleted ${deleted} live measurements`, 'success')
      await reload()
    } catch (e) {
      toast(errorMessage(e), 'error')
    }
  }

  return (
    <>
      <div className="toolbar">
        <div className="segmented" role="group" aria-label="Time window">
          {windows.map((w) => (
            <button key={w.label} className={minutes === w.minutes ? 'active' : ''} onClick={() => setMinutes(w.minutes)}>{w.label}</button>
          ))}
        </div>
        <span className="hint">Live measurements from the running services · refreshes every 5 s</span>
        <button className="button button--small" onClick={clear}>Clear live metrics</button>
      </div>
      {error && <p className="error-text">AnalyticsService: {error}</p>}

      <div className="grid grid--4">
        <StatCard label="Measurements" value={total.toLocaleString()} hint={`${errors} failed`} />
        <StatCard label="REST / gRPC calls" value={`${byMechanism('REST')} / ${byMechanism('gRPC')}`} hint="driver lookups by MatchingService" />
        <StatCard label="RabbitMQ / Kafka msgs" value={`${byMechanism('RabbitMQ')} / ${byMechanism('Kafka')}`} hint="delivery + processing samples" />
        <StatCard label="SignalR acks" value={String(byMechanism('SignalR'))} hint="notifications & chat acknowledged" />
      </div>

      {rows.length === 0 ? (
        <Card><EmptyState icon="chart" title="No measurements yet" message="Request and complete a ride, or run a benchmark, to generate measurements." /></Card>
      ) : (
        <>
          <Card title="Latency by operation" subtitle="Only operations measuring the same interval are shown together. Pick the interval type:">
            <div className="segmented" role="group" aria-label="Metric kind">
              {kinds.map((k) => (
                <button key={k} className={kind === k ? 'active' : ''} onClick={() => { setKind(k); setSelected(null) }}>{kindLabels[k] ?? k}</button>
              ))}
            </div>
            {ofKind.length > 0 ? (
              <BarChart
                ariaLabel={`Average and p95 latency, ${kindLabels[kind] ?? kind}`}
                categories={ofKind.map((r) => `${r.mechanism} · ${r.operation}`)}
                series={[
                  { label: 'Average', values: ofKind.map((r) => r.avgMs) },
                  { label: 'p95', values: ofKind.map((r) => r.p95Ms) },
                ]}
                unit=" ms"
              />
            ) : <p className="hint">No measurements of this kind in the selected window.</p>}
          </Card>

          <Card title="All live metrics" subtitle="Click a row to plot its latency over time. Latency statistics use successful samples only.">
            <div className="table-wrap">
              <table className="table table--clickable">
                <thead>
                  <tr><th>Mechanism</th><th>Kind</th><th>Operation</th><th>Count</th><th>Errors</th><th>Avg</th><th>p50</th><th>p95</th><th>p99</th><th>Max</th><th>Observed rate</th></tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={key(r)} onClick={() => { setSelected(key(r)); setKind(r.kind) }} className={selectedRow && key(r) === key(selectedRow) ? 'row--highlight' : ''}>
                      <td><strong>{r.mechanism}</strong></td>
                      <td>{r.kind}</td>
                      <td className="mono">{r.operation}</td>
                      <td>{r.count}</td>
                      <td>{r.errors > 0 ? <Badge tone="danger">{r.errors} ({(r.errorRate * 100).toFixed(1)}%)</Badge> : '0'}</td>
                      <td>{ms(r.avgMs)}</td>
                      <td>{ms(r.p50Ms)}</td>
                      <td>{ms(r.p95Ms)}</td>
                      <td>{ms(r.p99Ms)}</td>
                      <td>{ms(r.maxMs)}</td>
                      <td>{r.observedPerSecond == null ? '—' : `${r.observedPerSecond}/s`}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>

          {selectedRow && (
            <Card title={`${selectedRow.mechanism} · ${selectedRow.kind} · ${selectedRow.operation}`} subtitle="Latency of the last 300 samples over time">
              <LineChart
                ariaLabel={`Latency over time for ${selectedRow.operation}`}
                points={(series ?? []).map((p) => ({ time: new Date(p.recordedAt).getTime(), value: p.durationMs, failed: !p.success }))}
                unit=" ms"
              />
            </Card>
          )}
        </>
      )}

      <Card title="What is measured" subtitle="Start and end of every measured interval. Compare only rows with the same mechanism kind and operation.">
        <div className="table-wrap">
          <table className="table table--wrap">
            <thead><tr><th>Mechanism</th><th>Kind</th><th>Operation</th><th>Start</th><th>End</th><th>Notes</th></tr></thead>
            <tbody>
              {(definitions ?? []).map((d) => (
                <tr key={`${d.mechanism}${d.kind}${d.operation}`}>
                  <td><strong>{d.mechanism}</strong></td><td>{d.kind}</td><td className="mono">{d.operation}</td>
                  <td>{d.start}</td><td>{d.end}</td><td className="muted">{d.notes}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </>
  )
}
