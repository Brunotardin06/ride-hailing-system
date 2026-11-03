import { useCallback, useEffect, useRef, useState } from 'react'
import { analyticsApi } from '../../api/client'
import type { ActiveExperiment, RunSummary } from '../../api/types'
import Badge from '../../components/ui/Badge'
import Card from '../../components/ui/Card'
import { errorMessage, useToast } from '../../state/toastContext'
import { formatValue, type MetricRow, type ScenarioInfo } from './research'

/**
 * Starts a scenario experiment and follows it until all its runs are finished.
 * Also loads the history of this experiment type, so earlier results can be inspected.
 */
export function useScenarioExperiment(experimentType: 'A' | 'B') {
  const toast = useToast()
  const [history, setHistory] = useState<RunSummary[]>([])
  const [groupId, setGroupId] = useState<string | null>(null)
  const [active, setActive] = useState<ActiveExperiment | null>(null)
  const [starting, setStarting] = useState(false)
  const timer = useRef<ReturnType<typeof setInterval> | null>(null)

  const loadHistory = useCallback(async () => {
    try {
      const runs = await analyticsApi.runs({ experimentType, includeDetails: true })
      setHistory(runs)
      return runs
    } catch (e) {
      toast(errorMessage(e), 'error')
      return []
    }
  }, [experimentType, toast])

  useEffect(() => {
    void loadHistory().then((runs) => { if (runs[0]?.groupId) setGroupId((g) => g ?? runs[0].groupId) })
    void analyticsApi.activeExperiment().then((a) => setActive(a ?? null)).catch(() => {})
  }, [loadHistory])

  // While something runs, poll progress and results every second.
  const running = active !== null || history.some((r) => r.groupId === groupId && ['Queued', 'Running'].includes(r.status))
  useEffect(() => {
    if (!running) return
    timer.current = setInterval(async () => {
      const a = await analyticsApi.activeExperiment().catch(() => undefined)
      setActive(a ?? null)
      await loadHistory()
    }, 1000)
    return () => { if (timer.current) clearInterval(timer.current) }
  }, [running, loadHistory])

  const start = async (body: Record<string, unknown>) => {
    setStarting(true)
    try {
      const started = await (experimentType === 'A' ? analyticsApi.startExperimentA(body) : analyticsApi.startExperimentB(body))
      setGroupId(started.groupId)
      setActive({ experiment: experimentType, scenario: String(body.scenario), groupId: started.groupId, startedAt: new Date().toISOString(), phase: 'Starting' })
      await loadHistory()
      toast('Experiment started — results appear below when it finishes', 'info')
    } catch (e) {
      toast(errorMessage(e), 'error')
    } finally {
      setStarting(false)
    }
  }

  const groupRuns = history.filter((r) => r.groupId === groupId).sort((a, b) => a.mechanism.localeCompare(b.mechanism))
  return { history, groupId, setGroupId, groupRuns, active, running, starting, start, reload: loadHistory }
}

export function ScenarioPicker({ scenarios, value, onChange }: { scenarios: ScenarioInfo[]; value: string; onChange: (key: string) => void }) {
  return (
    <div className="scenario-picker" role="radiogroup" aria-label="Scenario">
      {scenarios.map((s) => (
        <button key={s.key} role="radio" aria-checked={value === s.key} className={`scenario-option ${value === s.key ? 'scenario-option--active' : ''}`}
          onClick={() => onChange(s.key)}>
          <span className="scenario-title">{s.title}</span>
          <span className="scenario-short">{s.short}</span>
        </button>
      ))}
    </div>
  )
}

export function ScenarioExplanation({ scenario }: { scenario: ScenarioInfo }) {
  return (
    <div className="explain">
      <p><strong>What this scenario investigates:</strong> {scenario.investigates}</p>
      <p className="explain-watch"><strong>Watch:</strong> {scenario.watch.join(' · ')}</p>
    </div>
  )
}

export function StatusBadge({ status }: { status: RunSummary['status'] }) {
  const tone = { Queued: 'neutral', Running: 'info', Completed: 'success', Incomplete: 'warning', Failed: 'danger' } as const
  return <Badge tone={tone[status] ?? 'neutral'}>{status}</Badge>
}

export function RunProgress({ active, runs }: { active: ActiveExperiment | null; runs: RunSummary[] }) {
  if (!active && !runs.some((r) => ['Queued', 'Running'].includes(r.status))) return null
  return (
    <div className="progress-panel">
      <span className="pulse" />
      <div>
        <strong>Experiment running…</strong>
        <p className="hint">{active?.phase ?? 'Waiting for the experiment runner'}</p>
        <p className="hint">{runs.map((r) => `${r.mechanism}: ${r.status}`).join(' · ')}</p>
      </div>
    </div>
  )
}

/** Side-by-side table of the same metrics for each approach of one experiment group. */
export function ComparisonTable({ runs, rows }: { runs: RunSummary[]; rows: MetricRow[] }) {
  const visible = rows.filter((row) => runs.some((run) => run.results?.[row.key] !== undefined && run.results?.[row.key] !== null))
  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            <th>Measurement</th>
            {runs.map((r) => <th key={r.id}>{r.mechanism} <StatusBadge status={r.status} /></th>)}
          </tr>
        </thead>
        <tbody>
          {visible.map((row) => (
            <tr key={row.key}>
              <td>{row.label}{row.hint && <span className="muted"> · {row.hint}</span>}</td>
              {runs.map((r) => <td key={r.id}><strong>{formatValue(r.results?.[row.key], row.unit)}</strong></td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function Observations({ run }: { run: RunSummary }) {
  return (
    <Card title={`${run.mechanism} — what happened`} subtitle="Derived automatically from the measurements of this run" aside={<StatusBadge status={run.status} />}>
      {run.error && <p className="error-text">Run failed: {run.error}</p>}
      <ul className="observations">
        {(run.observations ?? []).map((o) => <li key={o}>{o}</li>)}
      </ul>
      {run.parameters && (
        <p className="hint">Parameters: {Object.entries(run.parameters).map(([k, v]) => `${k} = ${v}`).join(' · ')}</p>
      )}
      {run.details?.timeline && (
        <details>
          <summary>Timeline</summary>
          <ol className="timeline-log">{run.details.timeline.map((t) => <li key={t}>{t}</li>)}</ol>
        </details>
      )}
      <NotesEditor run={run} />
    </Card>
  )
}

/** Qualitative observations written by the researcher, stored with the run. */
export function NotesEditor({ run }: { run: RunSummary }) {
  const toast = useToast()
  const [text, setText] = useState(run.qualitativeNotes ?? '')
  const [saving, setSaving] = useState(false)
  const changed = text !== (run.qualitativeNotes ?? '')
  const save = async () => {
    setSaving(true)
    try {
      await analyticsApi.saveRunNotes(run.id, text)
      toast('Notes saved', 'success')
    } catch (e) {
      toast(errorMessage(e), 'error')
    } finally {
      setSaving(false)
    }
  }
  return (
    <div className="notes">
      <label className="field">
        <span>Your qualitative observations</span>
        <textarea rows={3} value={text} onChange={(e) => setText(e.target.value)}
          placeholder="e.g. what the passenger would experience, implementation effort, surprises…" />
      </label>
      <button className="button button--small" onClick={save} disabled={!changed || saving}>{saving ? 'Saving…' : 'Save notes'}</button>
    </div>
  )
}

/** Earlier runs of this experiment, grouped; select one to inspect it. */
export function RunHistory({ history, groupId, onSelect }: { history: RunSummary[]; groupId: string | null; onSelect: (groupId: string) => void }) {
  const groups = [...new Map(history.filter((r) => r.groupId).map((r) => [r.groupId!, r])).values()]
  if (groups.length === 0) return null
  return (
    <Card title="Earlier runs" subtitle="Select a run to inspect its results">
      <div className="table-wrap">
        <table className="table table--clickable">
          <thead><tr><th>Started</th><th>Scenario</th><th>Approaches</th><th>Status</th><th>Parameters</th></tr></thead>
          <tbody>
            {groups.slice(0, 15).map((first) => {
              const runs = history.filter((r) => r.groupId === first.groupId)
              return (
                <tr key={first.groupId} onClick={() => onSelect(first.groupId!)} className={first.groupId === groupId ? 'row--highlight' : ''}>
                  <td>{new Date(first.startedAt).toLocaleString()}</td>
                  <td className="mono">{first.scenarioKey}</td>
                  <td>{runs.map((r) => r.mechanism).join(', ')}</td>
                  <td>{runs.map((r) => <StatusBadge key={r.id} status={r.status} />)}</td>
                  <td className="muted">{first.parameters ? Object.entries(first.parameters).map(([k, v]) => `${k}=${v}`).join(', ') : ''}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </Card>
  )
}
