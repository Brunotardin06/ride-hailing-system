import { useState } from 'react'
import BarChart from '../../components/charts/BarChart'
import LineChart from '../../components/charts/LineChart'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import { metricRowsA, scenariosA } from './research'
import {
  ComparisonTable, Observations, RunHistory, RunProgress, ScenarioExplanation, ScenarioPicker, useScenarioExperiment,
} from './scenarioComponents'

const outcomeLabel: Record<string, string> = {
  'offer-created': 'Offer created',
  'no-driver': 'No driver',
  pending: 'Pending (not matched yet)',
  'late-offer': 'Late offer after failure',
  failed: 'Failed',
}

/** Experiment A — synchronous (REST) vs asynchronous (RabbitMQ) communication for Ride Service → Matching Service. */
export default function ExperimentATab() {
  const { history, groupId, setGroupId, groupRuns, active, running, starting, start } = useScenarioExperiment('A')
  const [scenario, setScenario] = useState('A1-normal')
  const [approaches, setApproaches] = useState({ REST: true, RabbitMQ: true })
  const [form, setForm] = useState({ rides: 5, intervalMs: 300, slowDelayMs: 4000, restTimeoutMs: 3000, outageSeconds: 10 })
  const info = scenariosA.find((s) => s.key === scenario)!
  const shownScenario = scenariosA.find((s) => s.key === groupRuns[0]?.scenarioKey)
  const num = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [k]: Number(e.target.value) }))

  const run = () => start({
    scenario,
    approaches: Object.entries(approaches).filter(([, on]) => on).map(([a]) => a),
    ...form,
  })

  const rideRuns = groupRuns.filter((r) => r.details?.rides?.length)
  const maxRides = Math.max(0, ...rideRuns.map((r) => r.details!.rides!.length))
  const rideLabels = Array.from({ length: maxRides }, (_, i) => `Ride ${i + 1}`)
  const queueRun = groupRuns.find((r) => r.mechanism === 'RabbitMQ' && r.details?.queueSamples?.length)

  return (
    <>
      <Card title="Experiment A — Synchronous vs asynchronous communication"
        subtitle="Interaction: Ride Service → Matching Service when a passenger requests a ride. Research sub-questions SQ1, SQ2 and SQ4.">
        <div className="approach-grid">
          <div className="approach">
            <strong>REST (synchronous)</strong>
            <p>Ride Service calls <code>POST /api/matching/requests</code> and waits (timeout {form.restTimeoutMs} ms). The HTTP response tells the passenger whether a driver offer was created.</p>
          </div>
          <div className="approach">
            <strong>RabbitMQ (asynchronous)</strong>
            <p>Ride Service stores the ride, publishes <code>experiment.ride-requested</code> and answers immediately. Matching Service consumes the message whenever it is able to; the result follows as an event.</p>
          </div>
        </div>
        <p className="hint">Both use exactly the same matching logic. Experiment rides go to a dedicated matching instance, queue and driver pool, so demo rides are not affected. The comparison focuses on the business outcome: a ride offer was created.</p>
      </Card>

      <Card title="1 · Choose a scenario">
        <ScenarioPicker scenarios={scenariosA} value={scenario} onChange={setScenario} />
        <ScenarioExplanation scenario={info} />
      </Card>

      <Card title="2 · Approaches and parameters">
        <div className="button-row">
          {(['REST', 'RabbitMQ'] as const).map((a) => (
            <label key={a} className="check">
              <input type="checkbox" checked={approaches[a]} onChange={(e) => setApproaches((x) => ({ ...x, [a]: e.target.checked }))} /> {a}
            </label>
          ))}
        </div>
        <div className="form form--inline">
          <label className="field"><span>Ride requests</span><input type="number" min={1} max={20} value={form.rides} onChange={num('rides')} /></label>
          <label className="field"><span>Interval (ms)</span><input type="number" min={0} max={5000} value={form.intervalMs} onChange={num('intervalMs')} /></label>
          <label className="field"><span>REST timeout (ms)</span><input type="number" min={200} max={30000} value={form.restTimeoutMs} onChange={num('restTimeoutMs')} /></label>
          {scenario === 'A2-unavailable' && <label className="field"><span>Outage (s)</span><input type="number" min={2} max={120} value={form.outageSeconds} onChange={num('outageSeconds')} /></label>}
          {scenario === 'A3-slow' && <label className="field"><span>Matching delay (ms)</span><input type="number" min={100} max={30000} value={form.slowDelayMs} onChange={num('slowDelayMs')} /></label>}
        </div>
        <button className="button button--primary" onClick={run} disabled={running || starting || (!approaches.REST && !approaches.RabbitMQ)}>
          {running ? 'Experiment running…' : `3 · Run ${info.title}`}
        </button>
        <p className="hint">Approaches run one after another. Each run resets the experiment driver pool and cleans up its rides afterwards.</p>
      </Card>

      <RunProgress active={active} runs={groupRuns} />

      {groupRuns.length === 0 ? (
        <Card><EmptyState icon="chart" title="No results yet" message="Run a scenario to see real measurements here." /></Card>
      ) : (
        <>
          <Card title={`4 · Results — ${shownScenario?.title ?? groupRuns[0].scenarioKey}`}
            subtitle={`Run started ${new Date(groupRuns[0].startedAt).toLocaleString()}. HTTP response time and time to offer are different intervals and are shown separately.`}>
            <ComparisonTable runs={groupRuns} rows={metricRowsA} />
          </Card>

          {rideRuns.length > 0 && (
            <div className="grid grid--split">
              <Card title="Passenger-facing HTTP response time" subtitle="Per ride request (ms) — what the passenger waits for">
                <BarChart ariaLabel="HTTP response time per ride" categories={rideLabels} unit=" ms"
                  series={rideRuns.map((r) => ({ label: r.mechanism, values: rideLabels.map((_, i) => r.details!.rides![i]?.httpMs ?? null) }))} />
              </Card>
              <Card title="Time until a ride offer exists" subtitle="Per ride (ms) — the business outcome; empty = no offer">
                <BarChart ariaLabel="Time until offer per ride" categories={rideLabels} unit=" ms"
                  series={rideRuns.map((r) => ({ label: r.mechanism, values: rideLabels.map((_, i) => r.details!.rides![i]?.timeToOfferMs ?? null) }))} />
              </Card>
            </div>
          )}

          {queueRun && (
            <Card title="Messages waiting in queue matching-experiment (RabbitMQ run)" subtitle="Sampled every 250 ms via AMQP (ready messages)">
              <LineChart ariaLabel="Queue depth over time" unit=""
                points={queueRun.details!.queueSamples!.map((s) => ({ time: new Date(queueRun.startedAt).getTime() + s.atMs, value: s.messages }))} />
            </Card>
          )}

          <div className="grid grid--split">
            {groupRuns.map((r) => <Observations key={r.id} run={r} />)}
          </div>

          {rideRuns.length > 0 && (
            <Card title="Per-ride details">
              <div className="table-wrap">
                <table className="table">
                  <thead><tr><th>Approach</th><th>Ride</th><th>Sent at</th><th>HTTP</th><th>Response</th><th>Outcome</th><th>Request → offer</th><th>Offers</th><th>Reason</th></tr></thead>
                  <tbody>
                    {rideRuns.flatMap((r) => r.details!.rides!.map((x) => (
                      <tr key={`${r.id}-${x.index}`}>
                        <td>{r.mechanism}</td><td>{x.index}</td><td>{(x.sentAtMs / 1000).toFixed(1)} s</td>
                        <td>{x.httpStatus || 'error'}</td><td>{x.httpMs.toFixed(0)} ms</td>
                        <td>{outcomeLabel[x.outcome] ?? x.outcome}</td>
                        <td>{x.timeToOfferMs === null ? '—' : `${x.timeToOfferMs.toFixed(0)} ms`}</td>
                        <td>{x.offers}</td><td className="muted">{x.reason ?? ''}</td>
                      </tr>
                    )))}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
        </>
      )}

      <RunHistory history={history} groupId={groupId} onSelect={setGroupId} />
    </>
  )
}
