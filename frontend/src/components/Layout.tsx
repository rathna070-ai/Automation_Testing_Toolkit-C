import { NavLink, Outlet } from 'react-router-dom'
import './Layout.css'

const links = [
  { to: '/', label: 'Home', end: true },
  { to: '/inspect', label: 'Inspect' },
  { to: '/flows', label: 'Flows' },
  { to: '/run', label: 'Run' },
  { to: '/report', label: 'Report' },
  { to: '/failures', label: 'Failures' },
  { to: '/export', label: 'Export' },
  { to: '/settings', label: 'Settings' },
]

export function Layout() {
  return (
    <div className="layout">
      <nav className="layout-nav">
        <div className="layout-brand">Web Test Toolkit</div>
        <ul>
          {links.map((link) => (
            <li key={link.to}>
              <NavLink to={link.to} end={link.end}>
                {link.label}
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>
      <main className="layout-content">
        <Outlet />
      </main>
    </div>
  )
}
