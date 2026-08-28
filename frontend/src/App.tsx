import { Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { HomePage } from './pages/HomePage'
import { InspectPage } from './pages/InspectPage'
import { FlowsPage } from './pages/FlowsPage'
import { RunPage } from './pages/RunPage'
import { ReportPage } from './pages/ReportPage'
import { FailuresPage } from './pages/FailuresPage'
import { ExportPage } from './pages/ExportPage'
import { SettingsPage } from './pages/SettingsPage'

function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
        <Route path="inspect" element={<InspectPage />} />
        <Route path="flows" element={<FlowsPage />} />
        <Route path="run" element={<RunPage />} />
        <Route path="report" element={<ReportPage />} />
        <Route path="failures" element={<FailuresPage />} />
        <Route path="export" element={<ExportPage />} />
        <Route path="settings" element={<SettingsPage />} />
      </Route>
    </Routes>
  )
}

export default App
