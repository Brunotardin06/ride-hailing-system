import { useState } from 'react'
import BarChart from '../../components/charts/BarChart'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import { metricRowsB, scenariosB } from './research'
import {
  ComparisonTable, Observations, RunHistory, RunProgress, ScenarioExplanation, ScenarioPicker, useScenarioExperiment,
} from './scenarioComponents'

/** Experiment B — traditional message queuing (RabbitMQ) vs event streaming (Kafka) for ride lifecycle events. */
export default function ExperimentBTab() {
  const { history, groupId, setGroupId, groupRuns, active, running, starting, start } = useScenarioExperiment('B')
  const [scenario, setScenario] = useState('B1-single')
  const [mechanisms, setMechanisms] = useState({ RabbitMQ: true, Kafka: true })
  const [form, setForm] = useState({ rides: 50, offlineSeconds: 5, rabbitQueueMode: 'durable', sharedSubscription: false })
  const info = scenariosB.find((s) => s.key === scenario)!
  const shownScenario = scenariosB.find((s) => s.key === groupRuns[0]?.scenarioKey)

  const run = () => start({
    scenario,
    mechanisms: Object.entries(mechanisms).filter(([, on]) => on).map(([m]) => m),
    ...form,
  })

  const consumerNames = [...new Set(groupRuns.flatMap((r) => r.details?.consumers?.map((c) => c.name) ?? []))]

  return (
    <>
      <Card title="Experiment B — Message queuing vs event streaming"
        subtitle="Scenario: distributing ride lifecycle events (requested, assigned, started, completed, cancelled) to interested services. Research sub-questions SQ3 and SQ4.">
        <div className="approach-grid">
          <div className="approach">
            <strong>RabbitMQ (message queuing)</strong>
            <p>Fanout exchange <code>experiment.ride-lifecycle</code>; each subscribing service has its own queue. A message is deleted from a queue once the consumer acknowledges it.</p>
          </div>
          <div className="approach">
            <strong>Kafka (event streaming)</strong>
            <p>Topic <code>experiment.ride-lifecycle</code> (3 partitions, key = ride id); each service has its own consumer group. Records stay in the log; each group only moves its own offset.</p>
          </div>
        </div>
        <p className="hint">The same generated workload is used for both. These are dedicated experiment resources: the application's own RabbitMQ workflow and Kafka business topic are not changed. The goal is not to make them behave identically, but to see how their models differ.</p>
      </Card>

      <Card title="1 · Choose a scenario">
        <ScenarioPicker scenarios={scenariosB} value={scenario} onChange={setScenario} />
        <ScenarioExplanation scenario={info} />
      </Card>

      <Card title="2 · Mechanisms and parameters">
        <div className="button-row">
          {(['RabbitMQ', 'Kafka'] as const).map((m) => (
            <label key={m} className="check">
              <input type="checkbox" checked={mechanisms[m]} onChange={(e) => setMechanisms((x) => ({ ...x, [m]: e.target.checked }))} /> {m}
            </label>
          ))}
        </div>
        <div className="form form--inline">
          <label className="field"><span>Rides (≈4 events each)</span><input type="number" min={1} max={500} value={form.rides} onChange={(e) => setForm((f) => ({ ...f, rides: Number(e.target.value) }))} /></label>
          {scenario === 'B3-offline' && (
            <>
              <label className="field"><span>Offline publishing (s)</span><input type="number" min={1} max={60} value={form.offlineSeconds} onChange={(e) => setForm((f) => ({ ...f, offlineSeconds: Number(e.target.value) }))} /></label>
              <label className="field"><span>RabbitMQ queue for B</span>
                <select value={form.rabbitQueueMode} onChange={(e) => setForm((f) => ({ ...f, rabbitQueueMode: e.target.value }))}>
                  <option value="durable">Durable (kept while offline)</option>
                  <option value="temporary">Temporary (exclusive, auto-delete)</option>
                </select>
              </label>
            </>
          )}
          {scenario === 'B2-multiple' && (
            <label className="check">
              <input type="checkbox" checked={form.sharedSubscription} onChange={(e) => setForm((f) => ({ ...f, sharedSubscription: e.target.checked }))} />
              A and B share one queue / consumer group (competing consumers)
            </label>
          )}
        </div>
        <button className="button button--primary" onClick={run} disabled={running || starting || (!mechanisms.RabbitMQ && !mechanisms.Kafka)}>
          {running ? 'Experiment running…' : `3 · Run ${info.title}`}
        </button>
      </Card>

      <RunProgress active={active} runs={groupRuns} />

      {groupRuns.length === 0 ? (
        <Card><EmptyState icon="chart" title="No results yet" message="Run a scenario to see real measurements here." /></Card>
      ) : (
        <>
          <Card title={`4 · Results — ${shownScenario?.title ?? groupRuns[0].scenarioKey}`}
            subtitle={`Run started ${new Date(groupRuns[0].startedAt).toLocaleString()}. Delivery latency is measured in one process (publish call → consumer callback, monotonic clock).`}>
            <ComparisonTable runs={groupRuns} rows={metricRowsB} />
          </Card>

          {consumerNames.length > 0 && (
            <Card title="Events received per consumer" subtitle="Unique events each subscribing service processed">
              <BarChart ariaLabel="Unique events per consumer" categories={consumerNames}
                series={groupRuns.map((r) => ({ label: r.mechanism, values: consumerNames.map((n) => r.details?.consumers?.find((c) => c.name === n)?.unique ?? null) }))} />
            </Card>
          )}

          {groupRuns.map((r) => r.details?.consumers && (
            <Card key={`c-${r.id}`} title={`${r.mechanism} — consumers`}>
              <div className="table-wrap">
                <table className="table">
                  <thead><tr><th>Consumer</th><th>Subscription</th><th>Received</th><th>Unique</th><th>Missing</th><th>Duplicates</th><th>Out of order</th><th>Delivery p50 / p95</th><th>{r.mechanism === 'Kafka' ? 'Offsets stored' : 'Acks'}</th><th>Note</th></tr></thead>
                  <tbody>
                    {r.details.consumers.map((c) => (
                      <tr key={c.name}>
                        <td><strong>{c.name}</strong></td><td className="mono">{c.subscription}</td><td>{c.received}</td><td>{c.unique}</td>
                        <td>{c.missing}</td><td>{c.duplicates}</td><td>{c.outOfOrder}</td>
                        <td>{c.latencyP50Ms === null ? '—' : `${c.latencyP50Ms.toFixed(1)} / ${c.latencyP95Ms?.toFixed(1)} ms`}</td>
                        <td>{c.acksOrCommits}</td><td className="muted">{c.note ?? ''}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {r.details.implementation && (
                <details>
                  <summary>What this needed to configure ({r.mechanism})</summary>
                  <ol className="steps">{r.details.implementation.map((s) => <li key={s}>{s}</li>)}</ol>
                </details>
              )}
            </Card>
          ))}

          <div className="grid grid--split">
            {groupRuns.map((r) => <Observations key={r.id} run={r} />)}
          </div>
        </>
      )}

      <RunHistory history={history} groupId={groupId} onSelect={setGroupId} />
    </>
  )
}
