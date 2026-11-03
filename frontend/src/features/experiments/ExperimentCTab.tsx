import { useCallback, useEffect, useState } from 'react'
import { analyticsApi } from '../../api/client'
import type { EvidenceStatus, RunSummary, ScenarioFinding, TradeOffMatrix } from '../../api/types'
import Badge from '../../components/ui/Badge'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import { errorMessage, useToast } from '../../state/toastContext'
import { evidenceA, evidenceB, formatValue, latestByApproach, metricRowsA, metricRowsB } from './research'

const evidenceLabels: Record<EvidenceStatus, [string, 'neutral' | 'info' | 'success' | 'warning' | 'danger']> = {
  NotTested: ['Not tested yet', 'neutral'],
  Hypothesis: ['Hypothesis only', 'info'],
  Supported: ['Supported by measurements', 'success'],
  PartiallySupported: ['Partially supported', 'warning'],
  Contradicted: ['Contradicted by measurements', 'danger'],
}

/** Experiment C — structured architectural analysis built on the evidence of Experiments A and B. */
export default function ExperimentCTab() {
  const toast = useToast()
  const [findings, setFindings] = useState<ScenarioFinding[] | null>(null)
  const [matrices, setMatrices] = useState<TradeOffMatrix[]>([])
  const [runs, setRuns] = useState<RunSummary[]>([])

  const load = useCallback(async () => {
    try {
      const [f, m, a, b] = await Promise.all([
        analyticsApi.findings(), analyticsApi.tradeOffs(),
        analyticsApi.runs({ experimentType: 'A', includeDetails: true }), analyticsApi.runs({ experimentType: 'B', includeDetails: true }),
      ])
      setFindings(f)
      setMatrices(m)
      setRuns([...a, ...b])
    } catch (e) {
      toast(errorMessage(e), 'error')
    }
  }, [toast])

  useEffect(() => { void load() }, [load])

  if (!findings) return <Card><EmptyState icon="chart" title="Loading…" message="Loading findings from AnalyticsService." /></Card>

  return (
    <>
      <Card title="Experiment C — Architectural trade-off analysis"
        subtitle="Not another benchmark: this is where the evidence of Experiments A and B is turned into reasoned communication decisions.">
        <ul className="observations">
          <li><strong>Requirements and hypotheses</strong> are prepared per scenario. Hypotheses are expectations, not results.</li>
          <li><strong>Measured evidence</strong> is taken automatically from the latest completed run of each scenario.</li>
          <li><strong>Observations, advantages, disadvantages and the recommendation</strong> are yours to write, based on that evidence. Mark how strong the evidence is.</li>
        </ul>
      </Card>

      {(['A', 'B'] as const).map((experiment) => (
        <section key={experiment} className="section">
          <h2 className="section-title">{experiment === 'A' ? 'Experiment A — synchronous vs asynchronous (REST vs RabbitMQ)' : 'Experiment B — messaging vs event streaming (RabbitMQ vs Kafka)'}</h2>
          {findings.filter((f) => f.experiment === experiment).map((f) => (
            <FindingCard key={f.key} finding={f} runs={runs} onSaved={(saved) => setFindings((list) => list!.map((x) => (x.key === saved.key ? saved : x)))} />
          ))}
          {matrices.filter((m) => m.experiment === experiment).map((m) => (
            <TradeOffCard key={m.experiment} matrix={m} evidence={experiment === 'A' ? evidenceA(runs) : evidenceB(runs)} />
          ))}
        </section>
      ))}
    </>
  )
}

function FindingCard({ finding, runs, onSaved }: { finding: ScenarioFinding; runs: RunSummary[]; onSaved: (f: ScenarioFinding) => void }) {
  const toast = useToast()
  const [draft, setDraft] = useState(finding)
  const [saving, setSaving] = useState(false)
  const latest = latestByApproach(runs, finding.key)
  const rows = finding.experiment === 'A' ? metricRowsA : metricRowsB
  const approaches = Object.keys(latest).sort()
  const keyRows = rows.filter((row) => approaches.some((a) => latest[a].results?.[row.key] !== undefined && latest[a].results?.[row.key] !== null)).slice(0, 8)
  const changed = JSON.stringify(draft) !== JSON.stringify(finding)
  const [label, tone] = evidenceLabels[finding.evidenceStatus] ?? evidenceLabels.NotTested

  const save = async () => {
    setSaving(true)
    try {
      const saved = await analyticsApi.saveFinding(finding.key, {
        observations: draft.observations, advantages: draft.advantages, disadvantages: draft.disadvantages,
        recommendation: draft.recommendation, limitations: draft.limitations, evidenceStatus: draft.evidenceStatus,
      })
      onSaved(saved)
      setDraft(saved)
      toast(`${finding.title} saved`, 'success')
    } catch (e) {
      toast(errorMessage(e), 'error')
    } finally {
      setSaving(false)
    }
  }

  const field = (key: 'observations' | 'advantages' | 'disadvantages' | 'recommendation' | 'limitations', title: string, placeholder: string) => (
    <label className="field">
      <span>{title}</span>
      <textarea rows={key === 'limitations' ? 3 : 2} value={draft[key]} placeholder={placeholder}
        onChange={(e) => setDraft((d) => ({ ...d, [key]: e.target.value }))} />
    </label>
  )

  return (
    <Card title={finding.title} subtitle={finding.researchQuestion} aside={<Badge tone={tone}>{label}</Badge>}>
      <div className="grid grid--split">
        <div className="finding-block">
          <h3>Requirements</h3><p>{finding.requirements}</p>
          <h3>Hypothesis <span className="muted">(not a result)</span></h3><p>{finding.hypothesis}</p>
          <h3>Expected properties <span className="muted">(from theory, not measured here)</span></h3><p>{finding.expectedProperties}</p>
        </div>
        <div className="finding-block">
          <h3>Measured evidence (latest run)</h3>
          {approaches.length === 0 ? (
            <p className="muted">No completed run yet. Run this scenario in Experiment {finding.experiment} first.</p>
          ) : (
            <>
              <table className="table table--compact">
                <thead><tr><th />{approaches.map((a) => <th key={a}>{a}</th>)}</tr></thead>
                <tbody>
                  {keyRows.map((row) => (
                    <tr key={row.key}><td>{row.label}</td>{approaches.map((a) => <td key={a}>{formatValue(latest[a].results?.[row.key], row.unit)}</td>)}</tr>
                  ))}
                </tbody>
              </table>
              <p className="hint">Run of {new Date(latest[approaches[0]].startedAt).toLocaleString()} · full details in Experiment {finding.experiment}.</p>
            </>
          )}
        </div>
      </div>
      <div className="grid grid--split">
        {field('observations', 'Observations (what you saw)', 'Factual: what happened in the runs, incl. surprises')}
        {field('recommendation', 'Architectural recommendation', 'For which requirements would you choose which approach — and why?')}
        {field('advantages', 'Advantages', 'Per approach, backed by evidence')}
        {field('disadvantages', 'Disadvantages', 'Per approach, backed by evidence')}
      </div>
      {field('limitations', 'Limitations and assumptions', '')}
      <div className="button-row">
        <label className="field field--inline">
          <span>Evidence status</span>
          <select value={draft.evidenceStatus} onChange={(e) => setDraft((d) => ({ ...d, evidenceStatus: e.target.value as EvidenceStatus }))}>
            {Object.entries(evidenceLabels).map(([k, [l]]) => <option key={k} value={k}>{l}</option>)}
          </select>
        </label>
        <button className="button button--primary" onClick={save} disabled={!changed || saving}>{saving ? 'Saving…' : 'Save analysis'}</button>
      </div>
    </Card>
  )
}

function TradeOffCard({ matrix, evidence }: { matrix: TradeOffMatrix; evidence: Record<string, Record<string, string | null>> }) {
  return (
    <Card title={`Trade-off matrix — Experiment ${matrix.experiment}`}
      subtitle="Per decision criterion: measured evidence from the runs (if any) and your assessment. Mark whether an assessment is measured, expected (theory) or not tested.">
      <div className="table-wrap">
        <table className="table table--wrap tradeoff">
          <thead><tr><th>Criterion</th>{matrix.approaches.map((a) => <th key={a}>{a}</th>)}</tr></thead>
          <tbody>
            {matrix.criteria.map((c) => (
              <tr key={c.key}>
                <td><strong>{c.title}</strong><br /><span className="muted">{c.question}</span></td>
                {matrix.approaches.map((a) => (
                  <td key={a}>
                    <TradeOffCell experiment={matrix.experiment} criterion={c.key} approach={a}
                      measured={evidence[c.key]?.[a] ?? null}
                      note={matrix.notes.find((n) => n.criterion === c.key && n.approach === a)} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

function TradeOffCell({ experiment, criterion, approach, measured, note }: {
  experiment: string; criterion: string; approach: string; measured: string | null
  note?: { text: string; evidenceType: string }
}) {
  const toast = useToast()
  const [text, setText] = useState(note?.text ?? '')
  const [type, setType] = useState(note?.evidenceType ?? (measured ? 'Measured' : 'NotTested'))
  const [saved, setSaved] = useState({ text: note?.text ?? '', type: note?.evidenceType ?? '' })
  const changed = text !== saved.text || type !== saved.type

  const save = async () => {
    try {
      await analyticsApi.saveTradeOff({ experiment, criterion, approach, text, evidenceType: type })
      setSaved({ text, type })
      toast('Assessment saved', 'success')
    } catch (e) {
      toast(errorMessage(e), 'error')
    }
  }

  return (
    <div className="tradeoff-cell">
      {measured ? <p className="measured"><Badge tone="info">measured</Badge> {measured}</p> : <p className="muted">No measurement for this criterion.</p>}
      <textarea rows={2} value={text} onChange={(e) => setText(e.target.value)} placeholder="Your assessment…" aria-label={`${criterion} ${approach}`} />
      <div className="button-row">
        <select value={type} onChange={(e) => setType(e.target.value)} aria-label="Evidence type">
          <option value="Measured">Measured</option>
          <option value="Expected">Expected (theory)</option>
          <option value="NotTested">Not tested</option>
        </select>
        <button className="button button--small" onClick={save} disabled={!changed}>Save</button>
      </div>
    </div>
  )
}
