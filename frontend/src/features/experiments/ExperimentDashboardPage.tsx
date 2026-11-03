import { useState } from 'react'
import PageHeader from '../../components/ui/PageHeader'
import BusinessTab from './BusinessTab'
import ExperimentATab from './ExperimentATab'
import ExperimentBTab from './ExperimentBTab'
import ExperimentCTab from './ExperimentCTab'
import KafkaTab from './KafkaTab'
import MetricsTab from './MetricsTab'
import OverviewTab from './OverviewTab'
import RunsTab from './RunsTab'

type TabId = 'overview' | 'a' | 'b' | 'c' | 'tools'

const tabs: { id: TabId; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'a', label: 'A · Sync vs async' },
  { id: 'b', label: 'B · Messaging vs streaming' },
  { id: 'c', label: 'C · Architectural decisions' },
  { id: 'tools', label: 'Additional tools' },
]

/** Supplementary tools from the first version of the dashboard (kept, but not the focus of the research). */
const tools = [
  { id: 'metrics', label: 'Live communication metrics', component: MetricsTab },
  { id: 'benchmarks', label: 'REST vs gRPC benchmark & k6 runs', component: RunsTab },
  { id: 'business', label: 'Business event analytics', component: BusinessTab },
  { id: 'kafka', label: 'Kafka stream inspector', component: KafkaTab },
] as const

/**
 * Experiment dashboard, organised around the research scenarios. Data comes from AnalyticsService
 * (which also runs the scenario experiments); tool controls go to MatchingService.
 */
export default function ExperimentDashboardPage() {
  const [active, setActive] = useState<TabId>('overview')
  const [tool, setTool] = useState<(typeof tools)[number]['id']>('metrics')
  const Tool = tools.find((t) => t.id === tool)!.component

  return (
    <>
      <PageHeader
        eyebrow="Experiments"
        title="Communication experiments"
        description="Scenario-based investigation of communication decisions in a distributed ride-hailing system."
      />
      <div className="tabs" role="tablist">
        {tabs.map((t) => (
          <button key={t.id} role="tab" aria-selected={active === t.id}
            className={`tab ${active === t.id ? 'tab--active' : ''}`} onClick={() => setActive(t.id)}>
            {t.label}
          </button>
        ))}
      </div>

      {active === 'overview' && <OverviewTab onNavigate={setActive} />}
      {active === 'a' && <ExperimentATab />}
      {active === 'b' && <ExperimentBTab />}
      {active === 'c' && <ExperimentCTab />}
      {active === 'tools' && (
        <>
          <p className="hint">
            Supplementary technical tools. They measure individual mechanisms (e.g. REST vs gRPC latency); they support, but do not answer, the research question.
          </p>
          <div className="segmented" role="tablist" aria-label="Tool">
            {tools.map((t) => (
              <button key={t.id} role="tab" aria-selected={tool === t.id} className={tool === t.id ? 'active' : ''} onClick={() => setTool(t.id)}>{t.label}</button>
            ))}
          </div>
          <Tool />
        </>
      )}
    </>
  )
}
