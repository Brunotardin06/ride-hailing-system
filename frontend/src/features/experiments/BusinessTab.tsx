import { useCallback } from 'react'
import { analyticsApi } from '../../api/client'
import BarChart from '../../components/charts/BarChart'
import ColumnChart from '../../components/charts/ColumnChart'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import StatCard from '../../components/ui/StatCard'
import { usePolling } from './usePolling'

const seconds = (v: number | null) => (v == null ? '—' : `${v.toFixed(1)} s`)

export default function BusinessTab() {
  // The fetch time is kept with the data, so the timeline's end does not depend on render time.
  const loadSummary = useCallback(async () => ({ summary: await analyticsApi.summary(), fetchedAt: Date.now() }), [])
  const { data, error } = usePolling(loadSummary, 5000)
  const summary = data?.summary
  const loadEvents = useCallback(() => analyticsApi.events(50), [])
  const { data: events } = usePolling(loadEvents, 5000)

  // Fill missing minutes so the timeline is continuous.
  const timeline = (() => {
    if (!summary || summary.timeline.length === 0) return { labels: [], values: [] }
    const buckets = new Map(summary.timeline.map((b) => [new Date(b.minute).getTime(), Object.values(b.counts).reduce((a, c) => a + c, 0)]))
    const end = Math.floor(data!.fetchedAt / 60000) * 60000
    const labels: string[] = []
    const values: number[] = []
    for (let t = end - 29 * 60000; t <= end; t += 60000) {
      labels.push(new Date(t).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }))
      values.push(buckets.get(t) ?? 0)
    }
    return { labels, values }
  })()

  const byType = Object.entries(summary?.eventsByType ?? {}).sort((a, b) => b[1] - a[1])

  return (
    <>
      <p className="hint">Computed by AnalyticsService from ride lifecycle events it consumed from Kafka (topic <code>ride-events</code>, consumer group <code>analytics-service</code>).</p>
      {error && <p className="error-text">AnalyticsService: {error}</p>}

      <div className="grid grid--4">
        <StatCard label="Rides requested" value={String(summary?.ridesRequested ?? '—')} />
        <StatCard label="Rides accepted" value={String(summary?.ridesAccepted ?? '—')} hint="ride.assigned events" />
        <StatCard label="Rides completed" value={String(summary?.ridesCompleted ?? '—')}
          hint={summary?.completionRate != null ? `${(summary.completionRate * 100).toFixed(0)}% of requested` : undefined} />
        <StatCard label="Cancelled / no driver" value={summary ? `${summary.ridesCancelled} / ${summary.ridesWithoutDriver}` : '—'} />
      </div>
      <div className="grid grid--4">
        <StatCard label="Avg. time to accept" value={seconds(summary?.avgSecondsToAccept ?? null)} hint="requested → assigned" />
        <StatCard label="Avg. ride duration" value={seconds(summary?.avgRideSeconds ?? null)} hint="started → completed" />
        <StatCard label="Events stored" value={String(summary?.totalEvents ?? '—')} />
        <StatCard label="Last event" value={summary?.lastEventAt ? new Date(summary.lastEventAt).toLocaleTimeString() : '—'} />
      </div>

      <div className="grid grid--split">
        <Card title="Events per minute" subtitle="Last 30 minutes, all event types">
          <ColumnChart ariaLabel="Ride events per minute" labels={timeline.labels} values={timeline.values} />
        </Card>
        <Card title="Events by type" subtitle="All stored events">
          {byType.length > 0
            ? <BarChart ariaLabel="Number of events per event type" categories={byType.map(([t]) => t)} series={[{ label: 'Events', values: byType.map(([, c]) => c) }]} />
            : <EmptyState icon="bolt" title="No events yet" message="Request a ride to produce events." />}
        </Card>
      </div>

      <Card title="Event history" subtitle="Latest 50 events as stored by the analytics consumer">
        {!events || events.length === 0 ? (
          <EmptyState icon="bolt" title="No events" message="Events appear here a moment after they are published to Kafka." />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Occurred</th><th>Event</th><th>Ride</th><th>Status / details</th><th>Partition · offset</th><th>Kafka delay</th></tr></thead>
              <tbody>
                {events.map((e) => {
                  const p = e.payload as Record<string, string | null>
                  const delay = e.publishedAt ? new Date(e.consumedAt).getTime() - new Date(e.publishedAt).getTime() : null
                  return (
                    <tr key={e.id}>
                      <td>{new Date(e.occurredAt).toLocaleTimeString()}</td>
                      <td className="mono">{e.eventType}</td>
                      <td className="mono">{e.rideId.slice(0, 8)}</td>
                      <td>{p.status ?? `${p.pickup} → ${p.destination}`}{p.driverName ? ` · ${p.driverName}` : ''}{p.reason ? ` · ${p.reason}` : ''}</td>
                      <td>{e.partition} · {e.offset}</td>
                      <td>{delay == null ? '—' : `${delay} ms`}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </>
  )
}
