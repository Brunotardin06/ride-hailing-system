import { useCallback } from 'react'
import { analyticsApi } from '../../api/client'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import { researchQuestion, scenariosA, scenariosB, subQuestions } from './research'
import { StatusBadge } from './scenarioComponents'
import { usePolling } from './usePolling'

type TabId = 'overview' | 'a' | 'b' | 'c' | 'tools'

/** Starting point for a demo: research question, how the experiments answer it, latest runs. */
export default function OverviewTab({ onNavigate }: { onNavigate: (tab: TabId) => void }) {
  const loadRuns = useCallback(async () => {
    const [a, b] = await Promise.all([analyticsApi.runs({ experimentType: 'A' }), analyticsApi.runs({ experimentType: 'B' })])
    return [...a, ...b].sort((x, y) => y.startedAt.localeCompare(x.startedAt)).slice(0, 10)
  }, [])
  const { data: latest } = usePolling(loadRuns, 5000)

  const experiments = [
    { id: 'a' as const, title: 'Experiment A — Synchronous vs asynchronous', text: 'REST vs RabbitMQ for the same interaction (Ride Service → Matching Service), in normal operation, while Matching Service is down, and while it is slow.', scenarios: scenariosA.map((s) => s.title), sq: 'SQ1, SQ2, SQ4' },
    { id: 'b' as const, title: 'Experiment B — Messaging vs event streaming', text: 'RabbitMQ vs Kafka for distributing ride lifecycle events: one consumer, independent consumers, a consumer that is offline, and replay for a new consumer.', scenarios: scenariosB.map((s) => s.title), sq: 'SQ3, SQ4' },
    { id: 'c' as const, title: 'Experiment C — Architectural decisions', text: 'Requirements, hypotheses, measured evidence and your conclusions per scenario, plus trade-off matrices over eight decision criteria.', scenarios: ['Findings per scenario', 'Trade-off matrices A and B'], sq: 'SQ4 → main question' },
  ]

  return (
    <>
      <Card title="Research question">
        <p className="research-question">{researchQuestion}</p>
        <div className="table-wrap">
          <table className="table">
            <thead><tr><th>Sub-question</th><th>Answered by</th></tr></thead>
            <tbody>{subQuestions.map((q) => <tr key={q.id}><td><strong>{q.id}</strong> {q.text}</td><td>{q.experiments}</td></tr>)}</tbody>
          </table>
        </div>
        <p className="hint">
          Principle: communication approaches are evaluated <em>in the context of a scenario and its requirements</em>. Performance numbers are
          supporting evidence, not the answer. The goal is not to find a technology that is always better.
        </p>
      </Card>

      <div className="grid grid--3">
        {experiments.map((e) => (
          <button key={e.id} className="card card--link overview-card" onClick={() => onNavigate(e.id)}>
            <h2 className="card-title">{e.title}</h2>
            <p className="card-subtitle">{e.text}</p>
            <ul className="overview-list">{e.scenarios.map((s) => <li key={s}>{s}</li>)}</ul>
            <span className="muted">Answers {e.sq}</span>
            <span className="card-cta">Open →</span>
          </button>
        ))}
      </div>

      <Card title="Method in one minute">
        <ol className="steps">
          <li><strong>Controlled scenarios</strong>: the same business operation is run through each approach, with the same workload and isolated experiment resources (separate matching instance, queue, driver pool, exchange, topic).</li>
          <li><strong>Equivalent outcome</strong>: Experiment A compares whether and when a ride offer was created; the HTTP response time is recorded separately because it measures something else.</li>
          <li><strong>Failure situations</strong>: unavailable or slow receivers and offline consumers are part of the scenarios, because that is where the approaches differ most.</li>
          <li><strong>Evidence first</strong>: observations are generated from the stored measurements; conclusions are written in Experiment C and labelled by evidence strength.</li>
        </ol>
      </Card>

      <Card title="Latest experiment runs" aside={<button className="button button--small" onClick={() => onNavigate('tools')}>Additional tools →</button>}>
        {!latest || latest.length === 0 ? (
          <EmptyState icon="chart" title="No experiments run yet" message="Open Experiment A or B and run a scenario." />
        ) : (
          <div className="table-wrap">
            <table className="table table--clickable">
              <thead><tr><th>Started</th><th>Experiment</th><th>Scenario</th><th>Approach</th><th>Status</th><th>Key result</th></tr></thead>
              <tbody>
                {latest.map((r) => (
                  <tr key={r.id} onClick={() => onNavigate(r.experimentType === 'A' ? 'a' : 'b')}>
                    <td>{new Date(r.startedAt).toLocaleString()}</td>
                    <td>{r.experimentType}</td>
                    <td className="mono">{r.scenarioKey}</td>
                    <td>{r.mechanism}</td>
                    <td><StatusBadge status={r.status} /></td>
                    <td className="muted">{r.observations?.[r.experimentType === 'A' ? 2 : 0] ?? r.error ?? ''}</td>
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
