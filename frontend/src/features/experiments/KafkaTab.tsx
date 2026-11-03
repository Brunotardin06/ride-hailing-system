import { useState } from 'react'
import { analyticsApi } from '../../api/client'
import type { StreamRead } from '../../api/types'
import Card from '../../components/ui/Card'
import EmptyState from '../../components/ui/EmptyState'
import StatCard from '../../components/ui/StatCard'
import { errorMessage, useToast } from '../../state/toastContext'
import { usePolling } from './usePolling'

export default function KafkaTab() {
  const toast = useToast()
  const { data: group, error } = usePolling(analyticsApi.consumerGroup, 5000)
  const [stream, setStream] = useState<StreamRead | null>(null)
  const [reading, setReading] = useState(false)

  const read = async () => {
    setReading(true)
    try {
      setStream(await analyticsApi.stream(25))
    } catch (e) {
      toast(errorMessage(e), 'error')
    } finally {
      setReading(false)
    }
  }

  return (
    <>
      <p className="hint">
        Kafka keeps events in a log. Each consumer group tracks its own position (offset), so several services can read the
        same events independently, and a group can re-read (replay) history. RabbitMQ, by contrast, removes a message once it is acknowledged.
      </p>
      {error && <p className="error-text">AnalyticsService: {error}</p>}

      <div className="grid grid--4">
        <StatCard label="Topic" value={group?.topic ?? '—'} />
        <StatCard label="Messages in topic" value={String(group?.totalMessages ?? '—')} />
        <StatCard label="Consumer group" value={group?.consumerGroup ?? '—'} />
        <StatCard label="Consumer lag" value={String(group?.totalLag ?? '—')} hint="messages not yet processed" />
      </div>

      <Card title="Partitions" subtitle="Committed offset of the analytics-service group vs. end of each partition">
        <div className="table-wrap">
          <table className="table">
            <thead><tr><th>Partition</th><th>First offset</th><th>End offset</th><th>Committed (analytics-service)</th><th>Lag</th></tr></thead>
            <tbody>
              {(group?.partitions ?? []).map((p) => (
                <tr key={p.partition}>
                  <td>{p.partition}</td><td>{p.lowWatermark}</td><td>{p.highWatermark}</td>
                  <td>{p.committedOffset ?? 'none yet'}</td><td>{p.lag}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="hint">Ride id is the message key, so all events of one ride are in the same partition and stay in order.</p>
      </Card>

      <Card title="Read the stream with an independent consumer"
        subtitle="Creates a brand-new consumer group and reads the latest records directly from Kafka. The analytics-service offsets are not affected."
        aside={<button className="button button--primary" onClick={read} disabled={reading}>{reading ? 'Reading…' : 'Read latest 25'}</button>}>
        {!stream ? (
          <EmptyState icon="bolt" title="Not read yet" message="Click the button to read the topic." />
        ) : stream.messages.length === 0 ? (
          <EmptyState icon="bolt" title="Topic is empty" message="Request a ride to publish events." />
        ) : (
          <>
            <p className="hint">Read by temporary group <code>{stream.consumerGroup}</code>.</p>
            <div className="table-wrap">
              <table className="table">
                <thead><tr><th>Timestamp</th><th>Partition · offset</th><th>Key (ride id)</th><th>Event type</th><th>Value</th></tr></thead>
                <tbody>
                  {stream.messages.map((m) => (
                    <tr key={`${m.partition}-${m.offset}`}>
                      <td>{new Date(m.timestamp).toLocaleTimeString()}</td>
                      <td>{m.partition} · {m.offset}</td>
                      <td className="mono">{m.key.slice(0, 8)}</td>
                      <td className="mono">{m.eventType}</td>
                      <td className="mono truncate" title={m.value}>{m.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </Card>
    </>
  )
}
