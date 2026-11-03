// k6 load test: REST driver-availability lookup (GET /api/drivers/available).
// Same operation and parameters as driver-lookup-grpc.js, so the two runs are comparable.
// Interval measured by k6 (http_req_duration): request sent → response received.
import http from 'k6/http'
import { check } from 'k6'
import { Rate } from 'k6/metrics'

const BASE = __ENV.DRIVER_REST_URL || 'http://driver-service:8080'
export const req_failed = new Rate('req_failed')

export const options = {
  vus: Number(__ENV.VUS || 10),
  duration: __ENV.DURATION || '30s',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
}

export default function () {
  const res = http.get(`${BASE}/api/drivers/available?max=5`)
  const ok = check(res, { 'status 200': (r) => r.status === 200, 'is array': (r) => Array.isArray(r.json()) })
  req_failed.add(!ok)
}
