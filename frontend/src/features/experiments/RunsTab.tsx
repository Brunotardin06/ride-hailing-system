import { useState } from 'react'
import { analyticsApi, experimentsApi } from '../../api/client'
import type { LookupMode, RunSummary } from '../../api/types'
import BarChart from '../../components/charts/BarChart'
import { formatNumber } from '../../components/charts/chartUtils'
import Badge from '../../components/ui/Badge'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import { errorMessage, useToast } from '../../state/toastContext'
import { usePolling } from './usePolling'

const MAX_COMPARE = 4
const ms = (v: number | null) => (v == null ? '—' : formatNumber(v, ' ms'))

const loadToolRuns = () => analyticsApi.runs({ experimentType: 'tool' })

export default function RunsTab() {
  const toast = useToast()
  const { data: runs, error, reload } = usePolling(loadToolRuns, 10_000)
  const { data: config, reload: reloadConfig } = usePolling(experimentsApi.config, 10_000)
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [form, setForm] = useState({ iterations: 500, concurrency: 1, rounds: 2, warmup: 50, label: '' })
  const [running, setRunning] = useState(false)

  const selected = (runs ?? []).filter((r) => selectedIds.includes(r.id))
  // Comparing is only meaningful when every run measures the same interval.
  const comparable = new Set(selected.map((r) => `${r.kind}|${r.operation}`)).size <= 1

  const toggle = (id: string) =>
    setSelectedIds((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id].slice(-MAX_COMPARE)))

  const runBenchmark = async () => {
    setRunning(true)
    try {
      const results = await experimentsApi.runDriverLookupBenchmark({ ...form, label: form.label || undefined })
      toast(results.map((r) => `${r.mechanism}: avg ${ms(r.avgMs)}, p95 ${ms(r.p95Ms)}`).join(' · '), 'success')
      setSelectedIds(results.map((r) => r.id))
      await reload()
    } catch (e) {
      toast(errorMessage(e), 'error')
    } finally {
      setRunning(false)
    }
  }

  const setMode = async (mode: LookupMode | null) => {
    try {
      await experimentsApi.setLookupMode(mode)
      toast(`MatchingService now uses ${mode ?? 'the configured mode'} for live driver lookups`, 'success')
      await reloadConfig()
    } catch (e) {
      toast(errorMessage(e), 'error')
    }
  }

  const injectFailures = async (count: number) => {
    try {
      await experimentsApi.failNext(count)
      toast(`The next ${count} RabbitMQ message(s) in MatchingService will fail. Request a ride to see retries${count > 3 ? ' and the dead-letter queue' : ''}.`, 'info')
      await reloadConfig()
    } catch (e) {
      toast(errorMessage(e), 'error')
    }
  }

  const remove = async (id: string) => {
    try {
      await analyticsApi.deleteRun(id)
      setSelectedIds((ids) => ids.filter((x) => x !== id))
      await reload()
    } catch (e) {
      toast(errorMessage(e), 'error')
    }
  }

  const num = (field: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [field]: Number(e.target.value) }))

  return (
    <>
      <div className="grid grid--split">
        <Card title="REST vs gRPC benchmark" subtitle="MatchingService calls DriverService's driver-availability lookup through both clients (same query, same parameters).">
          <div className="form form--inline">
            <label className="field"><span>Iterations</span><input type="number" min={10} max={20000} value={form.iterations} onChange={num('iterations')} /></label>
            <label className="field"><span>Concurrency</span><input type="number" min={1} max={64} value={form.concurrency} onChange={num('concurrency')} /></label>
            <label className="field"><span>Rounds</span><input type="number" min={1} max={10} value={form.rounds} onChange={num('rounds')} /></label>
            <label className="field"><span>Warm-up</span><input type="number" min={0} max={1000} value={form.warmup} onChange={num('warmup')} /></label>
          </div>
          <label className="field"><span>Label (optional)</span><input value={form.label} placeholder="e.g. baseline, concurrency 8" onChange={(e) => setForm((f) => ({ ...f, label: e.target.value }))} /></label>
          <button className="button button--primary" onClick={runBenchmark} disabled={running}>{running ? 'Running benchmark…' : 'Run REST vs gRPC benchmark'}</button>
          <p className="hint">Each sample: start = just before the client call, end = response deserialised. Modes alternate per round; warm-up calls are discarded.</p>
        </Card>

        <Card title="Live configuration" subtitle="MatchingService (affects real ride matching)">
          <p>Driver lookup transport: <Badge tone="info">{config?.activeMode ?? '…'}</Badge> <span className="hint">(configured: {config?.configuredMode ?? '…'})</span></p>
          <div className="button-row">
            <button className="button button--small" onClick={() => setMode('Rest')}>Use REST</button>
            <button className="button button--small" onClick={() => setMode('Grpc')}>Use gRPC</button>
            <button className="button button--small" onClick={() => setMode(null)}>Reset to config</button>
          </div>
          <hr />
          <p><strong>RabbitMQ failure injection</strong> · pending failures: {config?.pendingInjectedFailures ?? 0}</p>
          <p className="hint">Makes MatchingService's consumer throw. With 2 failures the message is retried and succeeds; with 4 (&gt; MaxRetries 3) it lands in <code>matching-service.dlq</code>.</p>
          <div className="button-row">
            <button className="button button--small" onClick={() => injectFailures(2)}>Fail next 2 (retry)</button>
            <button className="button button--small" onClick={() => injectFailures(4)}>Fail next 4 (dead-letter)</button>
          </div>
        </Card>
      </div>

      {error && <p className="error-text">AnalyticsService: {error}</p>}

      {selected.length > 0 && (
        <Card title="Comparison of selected runs" subtitle={`${selected.length} run(s) selected (max ${MAX_COMPARE})`}>
          {!comparable ? (
            <p className="error-text">The selected runs measure different intervals (kind/operation) and are not comparable. Select runs of the same operation.</p>
          ) : (
            <div className="grid grid--split">
              <div>
                <h3 className="chart-title">Latency (ms)</h3>
                <BarChart
                  ariaLabel="Latency statistics per selected run"
                  categories={['Average', 'p50', 'p95', 'p99']}
                  series={selected.map((r) => ({ label: r.name, values: [r.avgMs, r.p50Ms, r.p95Ms, r.p99Ms] }))}
                  unit=" ms"
                />
              </div>
              <div>
                <h3 className="chart-title">Throughput (successful calls / s)</h3>
                <BarChart ariaLabel="Throughput per selected run" categories={['Throughput']}
                  series={selected.map((r) => ({ label: r.name, values: [r.throughputPerSecond] }))} unit="/s" />
                <h3 className="chart-title">Error rate (%)</h3>
                <BarChart ariaLabel="Error rate per selected run" categories={['Error rate']}
                  series={selected.map((r) => ({ label: r.name, values: [r.errorRate * 100] }))} unit="%" />
              </div>
            </div>
          )}
        </Card>
      )}

      <Card title="Experiment runs" subtitle="Stored in AnalyticsService. Tick runs to compare them. k6 results can be imported too (see docs/experiments.md).">
        {!runs || runs.length === 0 ? (
          <EmptyState icon="chart" title="No runs yet" message="Run the benchmark above or import k6 results." />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr><th /><th>Name</th><th>Mechanism</th><th>Operation</th><th>Tool</th><th>Scenario</th><th>n</th><th>Errors</th><th>Avg</th><th>p50</th><th>p95</th><th>p99</th><th>Throughput</th><th>Bytes recv/call</th><th /></tr>
              </thead>
              <tbody>
                {runs.map((r: RunSummary) => (
                  <tr key={r.id} className={selectedIds.includes(r.id) ? 'row--highlight' : ''}>
                    <td><input type="checkbox" checked={selectedIds.includes(r.id)} onChange={() => toggle(r.id)} aria-label={`Compare ${r.name}`} /></td>
                    <td>{r.name}<br /><span className="muted">{new Date(r.startedAt).toLocaleString()}</span></td>
                    <td><strong>{r.mechanism}</strong></td>
                    <td className="mono">{r.kind} · {r.operation}</td>
                    <td>{r.tool}</td>
                    <td className="muted">{r.scenario}</td>
                    <td>{r.count}</td>
                    <td>{r.errors}</td>
                    <td>{ms(r.avgMs)}</td>
                    <td>{ms(r.p50Ms)}</td>
                    <td>{ms(r.p95Ms)}</td>
                    <td>{ms(r.p99Ms)}</td>
                    <td>{r.throughputPerSecond == null ? '—' : `${r.throughputPerSecond}/s`}</td>
                    <td>{r.parameters?.bytesReceivedPerCall ?? '—'}</td>
                    <td><button className="button button--small" onClick={() => remove(r.id)} aria-label={`Delete ${r.name}`}>Delete</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  )
}
