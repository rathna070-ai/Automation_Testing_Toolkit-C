import * as signalR from '@microsoft/signalr'

// Client contract for the inspector. Commands are REST (they need validation and status
// codes); captured steps arrive over SignalR, because the backend polls the browser on a
// timer and there is nothing for the client to poll.
//
// The Inspect UI itself is P8 — this is the typed surface it builds on.

export type ActionType = 'navigate' | 'click' | 'type' | 'assertText' | 'assertVisible'

export type InspectorSessionState = 'starting' | 'running' | 'paused' | 'stopped' | 'faulted'

export interface LocatorCandidate {
  strategy: 'id' | 'css' | 'xpath' | 'name'
  value: string
  score: number
}

export interface CapturedElement {
  tagName: string
  id: string | null
  name: string | null
  visibleText: string | null
  candidates: LocatorCandidate[]
  type: string | null
  placeholder: string | null
  ariaLabel: string | null
  associatedLabelText: string | null
  cssClasses: string | null
  outerHtmlSnippet: string | null
  ancestorContext: string | null
  hasLocator: boolean
  bestLocator: LocatorCandidate | null
}

export interface InspectorEvent {
  sessionId: string
  sequence: number
  actionType: ActionType
  url: string
  pageName: string
  locatorKey: string
  suggestedLabel: string
  inputValue: string | null
  expectedText: string | null
  element: CapturedElement | null
  capturedAtUtc: string
  // Below ~60 means the only thing we could find was a structural css/xpath path, which
  // will break on the next markup change — worth warning about while the user can still
  // pick a different element.
  locatorScore: number
}

export interface InspectorSessionInfo {
  id: string
  name: string
  startUrl: string
  state: InspectorSessionState
  stepCount: number
  startedUtc: string
  lastActivityUtc: string
  currentUrl: string | null
  faultReason: string | null
}

export interface InspectSessionResponse {
  session: InspectorSessionInfo
  steps: InspectorEvent[]
}

export interface StartInspectRequest {
  name: string
  startUrl: string
  headless?: boolean
}

export interface UpdateStepRequest {
  actionType?: ActionType
  label?: string
  locatorKey?: string
  inputValue?: string
  expectedText?: string
  locatorStrategy?: string
  locatorValue?: string
}

async function send<T>(path: string, method: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (!response.ok) {
    // The API returns { error } for everything a user can act on — a bad URL, the
    // concurrent-session limit, Chrome failing to launch. Surface that, not a status code.
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.error ?? `${method} ${path} returned ${response.status}`)
  }

  return (await response.json()) as T
}

export function listInspectSessions(): Promise<InspectorSessionInfo[]> {
  return send<InspectorSessionInfo[]>('/api/inspect/sessions', 'GET')
}

export function startInspect(request: StartInspectRequest): Promise<InspectSessionResponse> {
  return send<InspectSessionResponse>('/api/inspect/start', 'POST', request)
}

export function getInspectSession(id: string): Promise<InspectSessionResponse> {
  return send<InspectSessionResponse>(`/api/inspect/${id}`, 'GET')
}

// Pause/resume capture without closing the browser — for dismissing a cookie banner or
// getting the app into the right state without those clicks landing in the flow.
export function setCapture(id: string, enabled: boolean): Promise<InspectorSessionInfo> {
  return send<InspectorSessionInfo>(`/api/inspect/${id}/capture`, 'POST', { enabled })
}

// Stop Inspect. Returns the captured steps, because Generate is the very next thing.
export function stopInspect(id: string): Promise<InspectSessionResponse> {
  return send<InspectSessionResponse>(`/api/inspect/${id}/stop`, 'POST')
}

export function updateInspectStep(
  id: string,
  sequence: number,
  edit: UpdateStepRequest,
): Promise<InspectSessionResponse> {
  return send<InspectSessionResponse>(`/api/inspect/${id}/steps/${sequence}`, 'PATCH', edit)
}

export function deleteInspectStep(id: string, sequence: number): Promise<InspectSessionResponse> {
  return send<InspectSessionResponse>(`/api/inspect/${id}/steps/${sequence}`, 'DELETE')
}

// The handoff: this goes straight to previewFlow/generateFlow in client.ts.
export function getInspectFlow(id: string): Promise<unknown> {
  return send<unknown>(`/api/inspect/${id}/flow`, 'GET')
}

export interface InspectFeedHandlers {
  onStep: (event: InspectorEvent) => void
  onState?: (session: InspectorSessionInfo) => void
}

// Live feed for one session. Resolves once subscribed; call the returned function to leave.
//
// withAutomaticReconnect re-establishes the connection but NOT the group membership, so the
// subscribe has to be re-sent on reconnect or the feed goes quiet without ever erroring.
export async function connectInspectFeed(
  sessionId: string,
  handlers: InspectFeedHandlers,
): Promise<() => Promise<void>> {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/inspect')
    .withAutomaticReconnect()
    .build()

  connection.on('stepCaptured', (event: InspectorEvent) => handlers.onStep(event))
  connection.on('sessionState', (session: InspectorSessionInfo) => handlers.onState?.(session))
  connection.onreconnected(() => {
    void connection.invoke('Subscribe', sessionId)
  })

  await connection.start()
  await connection.invoke('Subscribe', sessionId)

  return async () => {
    try {
      await connection.invoke('Unsubscribe', sessionId)
    } finally {
      await connection.stop()
    }
  }
}
