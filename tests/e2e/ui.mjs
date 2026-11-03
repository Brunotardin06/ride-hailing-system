// Browser test of the demo: one passenger window and one driver window side by side.
//
//   1. Backend running (docker compose up -d --build) and frontend dev server (cd frontend && npm run dev)
//   2. cd tests/e2e && npx playwright install chromium   (once)
//   3. npm run test:ui                                    (screenshots → tests/e2e/screenshots)
//
// Each window is a separate browser context, i.e. the same as two different users/tabs.

import { mkdirSync } from 'node:fs'
import { chromium } from 'playwright'

const app = process.env.APP_URL ?? 'http://localhost:5173'
const shots = new URL('./screenshots/', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')
mkdirSync(shots, { recursive: true })

await fetch('http://localhost:5002/api/dev/reset', { method: 'POST' })

const browser = await chromium.launch()
const viewport = { width: 1280, height: 860 }
const passenger = await (await browser.newContext({ viewport })).newPage()
const driver = await (await browser.newContext({ viewport })).newPage()
const errors = []
for (const [name, page] of [['passenger', passenger], ['driver', driver]]) {
  page.on('pageerror', (e) => errors.push(`${name}: ${e.message}`))
  page.on('console', (m) => { if (m.type() === 'error') errors.push(`${name} console: ${m.text()}`) })
}
const step = (t) => console.log(`· ${t}`)
const shot = (page, name) => page.screenshot({ path: `${shots}${name}.png`, fullPage: true })

try {
  // Passenger: choose a passenger without an active ride (Chloé), request a ride.
  await passenger.goto(`${app}/passenger/request`)
  await passenger.getByLabel('Select passenger').selectOption({ label: 'Chloé Martin' })
  await passenger.getByText('Live', { exact: true }).waitFor()
  step('passenger window connected to SignalR')

  // Driver: Emma (default), dashboard.
  await driver.goto(`${app}/driver/dashboard`)
  await driver.getByText('Live', { exact: true }).waitFor()
  await driver.getByRole('heading', { name: 'Emma de Vries' }).waitFor()
  step('driver window connected as Emma')

  // If Chloé still has an active ride from an earlier run, cancel it first.
  const active = passenger.getByRole('link', { name: 'Go to current ride' })
  if (await active.isVisible()) {
    await active.click()
    await passenger.getByRole('button', { name: 'Cancel ride' }).click()
    await passenger.goto(`${app}/passenger/request`)
  }

  await passenger.getByRole('button', { name: 'Request ride' }).click()
  await passenger.waitForURL('**/passenger/current')
  await passenger.locator('.searching').waitFor()
  step('ride requested (REST) → passenger sees the pending matching state')

  // Driver gets the offer pushed via SignalR; the dashboard shows it without reloading.
  await driver.getByRole('link', { name: 'View request' }).waitFor({ timeout: 15000 })
  await shot(driver, '1-driver-dashboard-offer')
  await driver.getByRole('link', { name: 'View request' }).click()
  await driver.getByText('Passenger: Chloé Martin').waitFor()
  await shot(driver, '2-driver-incoming-request')
  await shot(passenger, '3-passenger-waiting')
  await driver.getByRole('button', { name: 'Accept' }).click()
  await driver.waitForURL('**/driver/active')
  step('driver accepted (REST to DriverService)')

  await passenger.getByText('Driver on the way').first().waitFor({ timeout: 15000 })
  await passenger.getByText('Emma de Vries').first().waitFor()
  step('passenger notified live: "Driver on the way" with driver details')
  await shot(passenger, '4-passenger-assigned')

  // Chat both ways
  await passenger.getByRole('main').getByRole('link', { name: 'Chat' }).click()
  await driver.getByRole('main').getByRole('link', { name: 'Chat' }).click()
  await passenger.getByLabel('Message').fill('Hi Emma, I am at the main entrance')
  await passenger.getByRole('button', { name: 'Send' }).click()
  await driver.getByText('Hi Emma, I am at the main entrance').waitFor({ timeout: 10000 })
  await driver.getByLabel('Message').fill('Great, see you in 2 minutes!')
  await driver.getByRole('button', { name: 'Send' }).click()
  await passenger.getByText('Great, see you in 2 minutes!').waitFor({ timeout: 10000 })
  step('chat delivered in both directions (SignalR)')
  await shot(passenger, '5-passenger-chat')

  // Start and complete
  await driver.goto(`${app}/driver/active`)
  await driver.getByRole('button', { name: /Start ride/ }).click()
  await driver.getByRole('button', { name: 'Complete ride' }).and(driver.locator(':enabled')).waitFor()
  await passenger.goto(`${app}/passenger/current`)
  await passenger.getByText('In progress').first().waitFor({ timeout: 10000 })
  await driver.getByRole('button', { name: 'Complete ride' }).click()
  await passenger.getByText('Completed').first().waitFor({ timeout: 10000 })
  await passenger.getByRole('link', { name: 'Request another ride' }).waitFor()
  step('ride started and completed; passenger view updated live')
  await shot(passenger, '6-passenger-completed')

  await passenger.goto(`${app}/passenger/history`)
  await passenger.getByRole('cell', { name: 'Emma de Vries' }).first().waitFor()
  step('ride appears in history')

  // Experiment dashboard
  const dash = driver
  await dash.goto(`${app}/experiments`)
  await dash.getByRole('tab', { name: 'Additional tools' }).click()
  await dash.getByText('All live metrics').waitFor({ timeout: 15000 })
  await shot(dash, '7-experiments-metrics')
  await dash.getByRole('tab', { name: 'REST vs gRPC benchmark & k6 runs' }).click()
  await dash.getByRole('heading', { name: 'REST vs gRPC benchmark' }).waitFor()
  await dash.getByRole('checkbox', { name: /Compare k6 .* REST/ }).first().check()
  await dash.getByRole('checkbox', { name: /Compare k6 .* gRPC/ }).first().check()
  await dash.getByText('Latency (ms)').waitFor()
  await shot(dash, '8-experiments-runs')
  await dash.getByRole('tab', { name: 'Business event analytics' }).click()
  await dash.getByText('Event history').waitFor()
  await dash.waitForTimeout(1000)
  await shot(dash, '9-experiments-business')
  await dash.getByRole('tab', { name: 'Kafka stream inspector' }).click()
  await dash.getByRole('button', { name: 'Read latest 25' }).click()
  await dash.getByText(/Read by temporary group/).waitFor({ timeout: 15000 })
  await shot(dash, '10-experiments-kafka')
  step('experiment dashboard tabs render with data')

  // Mobile layout
  await passenger.setViewportSize({ width: 390, height: 844 })
  await passenger.goto(`${app}/passenger/current`)
  await passenger.getByRole('button', { name: 'Toggle navigation' }).waitFor()
  await shot(passenger, '11-mobile-current-ride')
  step('responsive layout (mobile) renders')

  if (errors.length) throw new Error(`Browser errors:\n${errors.join('\n')}`)
  console.log('\nUI test passed. Screenshots in tests/e2e/screenshots/')
} catch (e) {
  await shot(passenger, 'failure-passenger').catch(() => {})
  await shot(driver, 'failure-driver').catch(() => {})
  console.error(`\nUI test FAILED: ${e.message}`)
  if (errors.length) console.error(errors.join('\n'))
  process.exitCode = 1
} finally {
  await browser.close()
}
