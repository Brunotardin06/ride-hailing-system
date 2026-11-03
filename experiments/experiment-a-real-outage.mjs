// Experiment A2 with a REAL outage: the dedicated matching-experiment container is stopped and started again.
//
//   node experiments/experiment-a-real-outage.mjs [--rides 5] [--outage 15] [--approaches REST,RabbitMQ]
//
// The dashboard's A2 scenario simulates the outage (REST → 503, RabbitMQ consumer cancelled). This script checks
// that the conclusions also hold for a real process stop (REST → connection refused; queue keeps messages).
// Only the experiment instance is stopped, so demo rides (matching-service) are not affected.
// Requires the full stack in Docker (docker compose up -d --build).

import { spawnSync } from 'node:child_process'

const args = Object.fromEntries(process.argv.slice(2).join(' ').split('--').filter(Boolean).map((a) => a.trim().split(' ')))
const rides = Number(args.rides ?? 5)
const outage = Number(args.outage ?? 15)
const approaches = (args.approaches ?? 'REST,RabbitMQ').split(',')
const analytics = process.env.ANALYTICS_URL ?? 'http://localhost:5005'
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))
const compose = (...a) => spawnSync('docker', ['compose', ...a], { stdio: 'inherit' })

for (const approach of approaches) {
  console.log(`\n▶ ${approach}: stopping matching-experiment for ${outage}s`)
  compose('stop', 'matching-experiment')
  const stoppedAt = Date.now()

  const response = await fetch(`${analytics}/api/experiments/a`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ scenario: 'A2-unavailable', approaches: [approach], rides, outageSeconds: outage, outageMode: 'external' }),
  })
  const started = await response.json()
  if (!response.ok) {
    compose('start', 'matching-experiment')
    throw new Error(`Could not start experiment: ${JSON.stringify(started)}`)
  }

  await sleep(Math.max(0, outage * 1000 - (Date.now() - stoppedAt)))
  console.log('▶ starting matching-experiment again')
  compose('start', 'matching-experiment')

  let run
  for (;;) {
    await sleep(1000)
    ;[run] = await (await fetch(`${analytics}/api/experiments/runs?groupId=${started.groupId}`)).json()
    if (!['Queued', 'Running'].includes(run.status)) break
  }
  console.log(`\n=== ${run.name} (${run.status})${run.error ? ' — ' + run.error : ''}`)
  for (const o of run.observations ?? []) console.log(` - ${o}`)
  await sleep(2000)
}
console.log('\nResults are stored as Experiment A runs (outageMode = external) and shown on the dashboard.')
