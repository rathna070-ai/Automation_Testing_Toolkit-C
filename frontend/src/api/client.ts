// Thin fetch wrapper. Relative paths so the Vite dev-server proxy (see vite.config.ts)
// and a same-origin production deployment both work without changing this code.

export interface HealthResponse {
  status: string
  utc: string
}

export interface SettingsResponse {
  groqModel: string
  apiKeyConfigured: boolean
}

export interface UpdateSettingsRequest {
  groqApiKey?: string | null
  groqModel?: string | null
}

export interface LlmStatusResponse {
  apiKeyConfigured: boolean
  model: string
}

export type FailureCategory =
  | 'brokenLocator'
  | 'timing'
  | 'assertionMismatch'
  | 'navigation'
  | 'testData'
  | 'environment'
  | 'applicationBug'
  | 'unknown'

export interface SuggestedLocatorFix {
  page: string
  key: string
  strategy: string
  value: string
  why: string
}

export interface FailureAnalysis {
  category: FailureCategory
  rootCause: string
  suggestedFix: string
  suggestedLocator: SuggestedLocatorFix | null
  isLikelyApplicationBug: boolean
  confidence: number
  model: string | null
}

export interface ScenarioResultInput {
  featureName: string
  scenarioName: string
  outcome: 'passed' | 'failed' | 'skipped'
  duration: string
  errorMessage?: string | null
  stackTrace?: string | null
  screenshotPath?: string | null
}

export interface AnalyzeFailureResponse {
  available: boolean
  analysis: FailureAnalysis | null
  unavailableReason: string | null
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path)
  if (!response.ok) {
    throw new Error(`${path} returned ${response.status} ${response.statusText}`)
  }
  return (await response.json()) as T
}

async function sendJson<T>(path: string, method: string, body: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) {
    throw new Error(`${method} ${path} returned ${response.status} ${response.statusText}`)
  }
  return (await response.json()) as T
}

export function getHealth(): Promise<HealthResponse> {
  return getJson<HealthResponse>('/api/health')
}

export function getSettings(): Promise<SettingsResponse> {
  return getJson<SettingsResponse>('/api/settings')
}

export function updateSettings(request: UpdateSettingsRequest): Promise<SettingsResponse> {
  return sendJson<SettingsResponse>('/api/settings', 'PUT', request)
}

export function getLlmStatus(): Promise<LlmStatusResponse> {
  return getJson<LlmStatusResponse>('/api/llm/status')
}

export function analyzeFailure(scenario: ScenarioResultInput): Promise<AnalyzeFailureResponse> {
  return sendJson<AnalyzeFailureResponse>('/api/failures/analyze', 'POST', scenario)
}
