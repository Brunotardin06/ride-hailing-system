// k6 load test: gRPC driver-availability lookup (DriverAvailability/GetAvailableDrivers).
// Same operation and parameters as driver-lookup-rest.js, so the two runs are comparable.
// Interval measured by k6 (grpc_req_duration): request sent → response received.
import grpc from 'k6/net/grpc'
import { check } from 'k6'
import { Rate } from 'k6/metrics'

const TARGET = __ENV.DRIVER_GRPC_TARGET || 'driver-service:8081'
export const req_failed = new Rate('req_failed')

const client = new grpc.Client()
client.load(['/protos'], 'driver_availability.proto')

export const options = {
  vus: Number(__ENV.VUS || 10),
  duration: __ENV.DURATION || '30s',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
}

export default function () {
  if (__ITER === 0) client.connect(TARGET, { plaintext: true }) // one HTTP/2 connection per VU
  const res = client.invoke('driveravailability.DriverAvailability/GetAvailableDrivers', { max_results: 5 })
  const ok = check(res, { 'status OK': (r) => r && r.status === grpc.StatusOK })
  req_failed.add(!ok)
}
