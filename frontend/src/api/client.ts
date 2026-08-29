// Thin fetch wrapper. Relative paths so the Vite dev-server proxy (see vite.config.ts)
// and a same-origin production deployment both work without changing this code.

import type { ActionType, CapturedElement } from './inspect'

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

export interface GeneratedFile {
  relativePath: string
  content: string
}

export interface ValidationIssue {
  source: 'static' | 'compiler' | 'transport'
  code: string
  file: string | null
  line: number | null
  message: string
  // Blocking gates the build/repair loop; Advisory (a style nit, e.g. a duplicated
  // interaction block) rides along for display only. Optional because older cached
  // responses predate this field — treat missing as Blocking, the original behavior.
  severity?: 'blocking' | 'advisory'
}

export interface GenerationAttempt {
  number: number
  kind: 'deterministic' | 'llmInitial' | 'llmRepair'
  model: string | null
  succeeded: boolean
  durationMs: number
  promptTokens: number
  completionTokens: number
  issues: ValidationIssue[]
}

export type GenerationSource =
  | 'Deterministic'
  | 'LlmVerified'
  | 'LlmRepaired'
  | 'DeterministicFallback'
  | 'Failed'

export interface GenerateFlowResponse {
  source: GenerationSource
  files: GeneratedFile[]
  deterministicFiles: GeneratedFile[]
  attempts: GenerationAttempt[]
  fallbackReason: string | null
  writtenPaths: string[]
  totalPromptTokens: number
  totalCompletionTokens: number
  totalDurationMs: number
}

// Mirrors backend TestFlow/TestStep (Contracts/Models). Kept here rather than in inspect.ts
// because both the Inspect handoff and the built-in sample flow (sampleFlow.ts) need it, and
// this file is already the shared type home for everything Flows/Generate touches.
export interface TestFlowStep {
  order: number
  actionType: ActionType
  label: string
  pageName: string
  locatorKey?: string
  element?: CapturedElement | null
  inputValue?: string | null
  expectedText?: string | null
}

export interface TestFlow {
  name: string
  startUrl: string
  steps: TestFlowStep[]
}

export interface GenerateFlowRequest {
  flow: TestFlow
  useLlm: boolean
  maxRepairAttempts?: number
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

// Runs the full pipeline including the sandbox compile, but writes nothing — so the user
// can see verified output before it lands in the test project.
export function previewFlow(request: GenerateFlowRequest): Promise<GenerateFlowResponse> {
  return sendJson<GenerateFlowResponse>('/api/flows/preview', 'POST', request)
}

export function generateFlow(request: GenerateFlowRequest): Promise<GenerateFlowResponse> {
  return sendJson<GenerateFlowResponse>('/api/flows/generate', 'POST', request)
}

// --- Edge-case suggestions (P9) ----------------------------------------------------
// Speculative LLM output, reviewed before use — never written or compiled by this call.
// Each option already carries a complete TestFlow (same steps/locators as the original,
// different values/expectations), so "accept" is just calling previewFlow/generateFlow
// with option.flow like any other flow.

export interface EdgeCaseOption {
  nameSuffix: string
  title: string
  rationale: string
  flow: TestFlow
}

export interface EdgeCaseResponse {
  available: boolean
  edgeCases: EdgeCaseOption[]
  unavailableReason: string | null
}

export function suggestEdgeCases(flow: TestFlow): Promise<EdgeCaseResponse> {
  return sendJson<EdgeCaseResponse>('/api/flows/edge-cases', 'POST', { flow })
}

// --- Test case export (P6) --------------------------------------------------------

export type TestCasePriority = 'low' | 'medium' | 'high'
export type TestCaseSource = 'recorded' | 'edgeCase' | 'outline'
export type ScenarioOutcome = 'passed' | 'failed' | 'skipped'

export interface TestCaseStep {
  number: number
  action: string
  testData: string | null
  expectedResult: string
}

export interface TestCaseDocument {
  id: string
  title: string
  precondition: string
  priority: TestCasePriority
  source: TestCaseSource
  lastRunStatus: ScenarioOutcome | null
  steps: TestCaseStep[]
}

export interface TestCaseSuite {
  flowName: string
  startUrl: string
  generatedAtUtc: string
  testCases: TestCaseDocument[]
}

export interface ExportTestCasesRequest {
  flow: TestFlow
  useLlm?: boolean
}

export function previewTestCases(request: ExportTestCasesRequest): Promise<TestCaseSuite> {
  return sendJson<TestCaseSuite>('/api/export/testcases/preview', 'POST', request)
}

// The flow name travels with the request, so the filename is built client-side rather than
// read off the response's Content-Disposition header — that header isn't in the default
// CORS-exposed set, and exposing it is more plumbing than just knowing the name already.
async function downloadFile(path: string, body: unknown, filename: string): Promise<void> {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) {
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.error ?? `${path} returned ${response.status} ${response.statusText}`)
  }

  const blob = await response.blob()
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

export function downloadTestCasesXlsx(request: ExportTestCasesRequest, flowName: string): Promise<void> {
  return downloadFile('/api/export/testcases/xlsx', request, `${flowName}-test-cases.xlsx`)
}

export function downloadTestCasesXml(request: ExportTestCasesRequest, flowName: string): Promise<void> {
  return downloadFile('/api/export/testcases/xml', request, `${flowName}-test-cases.xml`)
}
