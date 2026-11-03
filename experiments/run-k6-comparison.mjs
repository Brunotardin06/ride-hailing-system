// Runs the k6 REST and gRPC driver-lookup load tests (in Docker, on the compose network)
// and imports both summaries into AnalyticsService as experiment runs.
//
//   node experiments/run-k6-comparison.mjs [--vus 10] [--duration 30s] [--label "my run"]
//
// Requires the full stack running in Docker (docker compose up -d --build).
// Both tests call DriverService directly from the k6 container: same query, same parameters,
// same load profile; only the protocol differs (HTTP/1.1 + JSON vs HTTP/2 + Protobuf).

import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const args = Object.fromEntries(process.argv.slice(2).join(' ').split('--').filter(Boolean).map((a) => {
  const [k, ...v] = a.trim().split(' ')
  return [k, v.join(' ')]
}))
const vus = args.vus || '10'
const duration = args.duration || '30s'
const label = args.label || `k6 ${vus} VUs ${duration}`
const analytics = process.env.ANALYTICS_URL ?? 'http://localhost:5005'
const network = process.env.COMPOSE_NETWORK ?? 'ride-hailing_default'

const here = dirname(fileURLToPath(import.meta.url))
const root = resolve(here, '..')
const k6Dir = join(here, 'k6')
const resultsDir = join(here, 'results')
const protoDir = join(root, 'src', 'services', 'DriverService', 'Protos')

const tests = [
  { script: 'driver-lookup-rest.js', mechanism: 'REST', file: 'k6-rest.json' },
  { script: 'driver-lookup-grpc.js', mechanism: 'gRPC', file: 'k6-grpc.json' },
]

for (const t of tests) {
  console.log(`\n▶ k6 ${t.mechanism}: ${vus} VUs for ${duration}`)
  const run = spawnSync('docker', [
    'run', '--rm', '--network', network,
    '-v', `${k6Dir}:/scripts:ro`, '-v', `${protoDir}:/protos:ro`, '-v', `${resultsDir}:/results`,
    '-e', `VUS=${vus}`, '-e', `DURATION=${duration}`,
    'grafana/k6', 'run', '--quiet', '--summary-export', `/results/${t.file}`, `/scripts/${t.script}`,
  ], { stdio: 'inherit' })
  if (run.status !== 0) {
    console.error(`k6 exited with ${run.status}`)
    process.exit(1)
  }

  const summary = JSON.parse(readFileSync(join(resultsDir, t.file), 'utf8'))
  const response = await fetch(`${analytics}/api/experiments/runs/import/k6`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name: `${label} · ${t.mechanism}`,
      mechanism: t.mechanism,
      operation: 'driver-lookup (k6 client)',
      scenario: `k6 constant load: ${vus} VUs for ${duration}, k6 container → driver-service`,
      summary,
    }),
  })
  if (!response.ok) {
    console.error(`Import failed: ${response.status} ${await response.text()}`)
    process.exit(1)
  }
  const r = await response.json()
  console.log(`  imported: n=${r.count} errors=${r.errors} avg=${r.avgMs} ms p50=${r.p50Ms} p95=${r.p95Ms} p99=${r.p99Ms} throughput=${r.throughputPerSecond}/s`)
}
console.log('\nCompare the two runs on the Experiment dashboard → Experiment runs.')
