import * as signalR from '@microsoft/signalr'
import type { ScenarioOutcome } from './client'

// TimeSpan serializes as a "c"-format string ("hh:mm:ss.fffffff") via System.Text.Json's
// built-in converter — kept as a string here rather than parsed into a number, since it's
// only ever displayed, never computed on.
export interface ScenarioResult {
  featureName: string
  scenarioName: string
  outcome: ScenarioOutcome
  duration: string
  errorMessage: string | null
  stackTrace: string | null
  screenshotPath: string | null
}

export interface RunSummary {
  runAtUtc: string
  total: number
  passed: number
  failed: number
  duration: string
  scenarios: ScenarioResult[]
}

export type TestRunStatus = 'Running' | 'Completed' | 'Faulted'

export interface RunResponse {
  runId: string
  status: TestRunStatus
  consoleLines: string[]
  summary: RunSummary | null
  error: string | null
}

async function send<T>(path: string, method: string): Promise<T> {
  const response = await fetch(path, { method })
  if (!response.ok) {
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.error ?? `${method} ${path} returned ${response.status}`)
  }
  return (await response.json()) as T
}

// Fire-and-forget-and-poll: the run keeps going server-side regardless of whether this
// resolves before the run finishes — it just returns the id to subscribe/poll with.
export function startRun(): Promise<{ runId: string }> {
  return send<{ runId: string }>('/api/execution/run', 'POST')
}

export function getRun(runId: string): Promise<RunResponse> {
  return send<RunResponse>(`/api/execution/runs/${runId}`, 'GET')
}

// Lets the Report page work from a direct visit or a refresh, not only right after Run
// started something in the same browser session.
export function getLatestRun(): Promise<RunResponse> {
  return send<RunResponse>('/api/execution/runs/latest', 'GET')
}

export interface RunFeedHandlers {
  onLine: (line: string) => void
  onCompleted: (result: RunResponse) => void
}

// Same shape as connectInspectFeed in inspect.ts: withAutomaticReconnect re-establishes the
// connection but not group membership, so Subscribe is re-sent on reconnect.
export async function connectRunFeed(runId: string, handlers: RunFeedHandlers): Promise<() => Promise<void>> {
  const connection = new signalR.HubConnectionBuilder().withUrl('/hubs/run').withAutomaticReconnect().build()

  connection.on('consoleLine', (line: string) => handlers.onLine(line))
  connection.on('runCompleted', (result: RunResponse) => handlers.onCompleted(result))
  connection.onreconnected(() => {
    void connection.invoke('Subscribe', runId)
  })

  await connection.start()
  await connection.invoke('Subscribe', runId)

  return async () => {
    try {
      await connection.invoke('Unsubscribe', runId)
    } finally {
      await connection.stop()
    }
  }
}
