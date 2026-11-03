import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import Icon from '../ui/Icon'
import { navSections } from './navigation'

// Shell for every page: sidebar navigation on desktop, slide-in menu on small screens.
export default function AppLayout() {
  const [menuOpen, setMenuOpen] = useState(false)
  const closeMenu = () => setMenuOpen(false)

  return (
    <div className="app-shell">
      <header className="topbar">
        <button
          className="icon-button"
          aria-label="Toggle navigation"
          aria-expanded={menuOpen}
          onClick={() => setMenuOpen((open) => !open)}
        >
          <Icon name="menu" />
        </button>
        <span className="brand-name">RideLab</span>
      </header>

      <aside className={`sidebar ${menuOpen ? 'sidebar--open' : ''}`}>
        <NavLink to="/" className="brand" onClick={closeMenu}>
          <span className="brand-mark">
            <Icon name="car" />
          </span>
          <span>
            <span className="brand-name">RideLab</span>
            <span className="brand-sub">Distributed ride-hailing</span>
          </span>
        </NavLink>

        <nav className="nav">
          {navSections.map((section) => (
            <div key={section.title} className="nav-section">
              <p className="nav-title">{section.title}</p>
              {section.items.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  onClick={closeMenu}
                  className={({ isActive }) => `nav-link ${isActive ? 'nav-link--active' : ''}`}
                >
                  <Icon name={item.icon} />
                  {item.label}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        <p className="sidebar-footer">Fontys · Complex Software Systems</p>
      </aside>

      {menuOpen && <div className="backdrop" onClick={closeMenu} />}

      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
