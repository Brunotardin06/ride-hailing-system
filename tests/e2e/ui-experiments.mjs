// Browser test of the research dashboard: runs Experiment A2 and B4 from the UI and records an analysis in C.
//
//   Backend running (docker compose up -d --build), frontend dev server running (cd frontend && npm run dev)
//   cd tests/e2e && npm run test:ui-experiments          (screenshots → tests/e2e/screenshots)

import { mkdirSync } from 'node:fs'
import { chromium } from 'playwright'

const app = process.env.APP_URL ?? 'http://localhost:5173'
const shots = new URL('./screenshots/', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')
mkdirSync(shots, { recursive: true })

const browser = await chromium.launch()
const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage()
const errors = []
page.on('pageerror', (e) => errors.push(e.message))
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()) })
const step = (t) => console.log(`· ${t}`)
const shot = (name) => page.screenshot({ path: `${shots}${name}.png`, fullPage: true })

try {
  await page.goto(`${app}/experiments`)
  await page.getByText('Research question').waitFor()
  await shot('20-research-overview')
  step('overview shows the research question')

  // Experiment A2 from the UI (both approaches, short outage)
  await page.getByRole('tab', { name: 'A · Sync vs async' }).click()
  await page.getByRole('radio', { name: /A2 · Matching Service unavailable/ }).click()
  await page.getByText('What this scenario investigates').waitFor()
  await page.getByLabel('Ride requests').fill('3')
  await page.getByLabel('Outage (s)').fill('5')
  await page.getByRole('button', { name: /Run A2/ }).click()
  await page.getByText('Experiment running…').first().waitFor({ timeout: 10000 })
  step('Experiment A2 started from the dashboard')
  await page.getByRole('button', { name: /Run A2/ }).waitFor({ timeout: 90000 })
  await page.getByText(/Results — A2/).waitFor()
  await page.getByText('Durability check during the outage').waitFor()
  await shot('21-experiment-a2-results')
  step('A2 results shown (incl. durability check)')

  // Save a qualitative note on the first run
  const notes = page.getByLabel('Your qualitative observations').first()
  await notes.fill('UI test: passenger got an immediate error with REST; RabbitMQ accepted the request.')
  await page.getByRole('button', { name: 'Save notes' }).first().click()
  await page.getByText('Notes saved').waitFor()
  step('qualitative notes saved')

  // Experiment B4 from the UI
  await page.getByRole('tab', { name: 'B · Messaging vs streaming' }).click()
  await page.getByRole('radio', { name: /B4 · Replay/ }).click()
  await page.getByRole('button', { name: /Run B4/ }).click()
  await page.getByText('Experiment running…').first().waitFor({ timeout: 10000 })
  await page.getByRole('button', { name: /Run B4/ }).waitFor({ timeout: 90000 })
  await page.getByText(/Results — B4/).waitFor()
  await page.getByText('Late consumer C').first().waitFor()
  await shot('22-experiment-b4-results')
  step('B4 results shown (late consumer)')

  // Experiment C: evidence visible, save an analysis
  await page.getByRole('tab', { name: 'C · Architectural decisions' }).click()
  await page.getByText('A2 · Matching Service unavailable').waitFor()
  await page.getByText('Trade-off matrix — Experiment A').waitFor()
  const card = page.locator('section.card', { hasText: 'A2 · Matching Service unavailable' }).first()
  await card.getByText('Measured evidence (latest run)').waitFor()
  await shot('23-experiment-c')
  step('Experiment C shows findings, measured evidence and trade-off matrices')

  if (errors.length) throw new Error(`Browser errors:\n${errors.join('\n')}`)
  console.log('\nResearch dashboard UI test passed. Screenshots in tests/e2e/screenshots/')
} catch (e) {
  await shot('failure-experiments').catch(() => {})
  console.error(`\nUI test FAILED: ${e.message}`)
  if (errors.length) console.error(errors.join('\n'))
  process.exitCode = 1
} finally {
  await browser.close()
}
