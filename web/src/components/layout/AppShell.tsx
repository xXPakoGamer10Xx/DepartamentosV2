import clsx from 'clsx'
import { ChevronLeft, ChevronRight, LogOut, MoreHorizontal, X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useMemo, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { iniciales } from '../../lib/format'
import { useUsaApp } from '../../lib/usaApp'
import { resubscribeIfGranted } from '../../lib/push'
import { connectSSE, disconnectSSE } from '../../lib/sse'
import { getToken } from '../../lib/session'
import { navFor, type NavItem } from './nav'
import { BrandLogo } from './BrandLogo'
import { NotificationBell } from './NotificationBell'

const MAX_BOTTOM = 4 // además del botón "Más"
const SIDEBAR_KEY = 'nethrent_sidebar_collapsed'

export function AppShell() {
  const { user, can, logout } = useAuth()
  const [more, setMore] = useState(false)
  const [collapsed, setCollapsed] = useState(() => {
    try { return localStorage.getItem(SIDEBAR_KEY) === '1' } catch { return false }
  })
  const [navSeq, setNavSeq] = useState(0)
  const { pathname } = useLocation()
  const usaApp = useUsaApp()
  const bumpNav = () => setNavSeq(n => n + 1)

  const toggleSidebar = () => {
    setCollapsed(c => {
      const next = !c
      try { localStorage.setItem(SIDEBAR_KEY, next ? '1' : '0') } catch { /* ignore */ }
      return next
    })
  }

  // Conexión en vivo + restaurar suscripción push (solo si ya había permiso).
  useEffect(() => {
    const t = getToken()
    if (t) connectSSE(t)
    void resubscribeIfGranted()
    return () => disconnectSSE()
  }, [])

  useEffect(() => setMore(false), [pathname])

  const items = useMemo(
    () => navFor(user!.rol).filter(i => (!i.adminOnly || user!.rol === 'admin') && (!i.permiso || can(i.permiso)) && (usaApp || !i.conApp)),
    [user, can, usaApp],
  )
  // En el celular: los ítems prioritarios (Dashboard, Inquilinos, Contratos, Pagos) y el resto en "Más".
  const bottomSet = useMemo(() => {
    if (items.length <= MAX_BOTTOM + 1) return new Set(items)
    const prior = items.filter(i => i.bottom)
    return new Set([...prior, ...items.filter(i => !i.bottom)].slice(0, MAX_BOTTOM))
  }, [items])
  const bottom = items.filter(i => bottomSet.has(i))
  const rest = items.filter(i => !bottomSet.has(i))

  const [hoveredTip, setHoveredTip] = useState<{ label: string; top: number } | null>(null)

  if (!user) return null

  return (
    <div className="flex h-full overflow-hidden">
      {/* ── Sidebar (escritorio) ─────────────────────────────────────────── */}
      <motion.aside
        initial={false}
        animate={{ width: collapsed ? 80 : 256 }}
        transition={{ type: 'spring', stiffness: 360, damping: 34, bounce: 0 }}
        onMouseLeave={() => setHoveredTip(null)}
        className="relative hidden h-full max-h-full shrink-0 flex-col border-r border-line bg-card lg:flex"
      >
        <button
          type="button"
          onClick={() => { setHoveredTip(null); toggleSidebar() }}
          aria-label={collapsed ? 'Mostrar menú lateral' : 'Ocultar menú lateral'}
          title={collapsed ? 'Mostrar menú' : 'Ocultar menú'}
          className="absolute -right-3.5 top-6 z-30 grid size-7 cursor-pointer place-items-center rounded-full border border-line-strong bg-card text-fg-2 shadow-md transition hover:border-primary hover:bg-hover hover:text-fg active:scale-95"
        >
          {collapsed ? <ChevronRight className="size-4" /> : <ChevronLeft className="size-4" />}
        </button>

        <div className={clsx('flex shrink-0 items-center pb-2.5 pt-4', collapsed ? 'justify-center px-2' : 'px-5')}>
          <BrandLogo collapsed={collapsed} />
        </div>

        <nav className="min-h-0 flex-1 space-y-0.5 overflow-y-auto overflow-x-hidden px-3 py-1.5 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          {items.map(i => (
            <SideLink
              key={i.to}
              item={i}
              collapsed={collapsed}
              onClick={bumpNav}
              onHover={(label, top) => setHoveredTip(collapsed && label ? { label, top } : null)}
            />
          ))}
        </nav>

        <AnimatePresence>
          {collapsed && hoveredTip && (
            <motion.span
              key={hoveredTip.label}
              initial={{ opacity: 0, x: -4 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -4 }}
              transition={{ duration: 0.12 }}
              style={{ top: hoveredTip.top }}
              className="pointer-events-none fixed left-[88px] z-50 -translate-y-1/2 whitespace-nowrap rounded-lg border border-line-strong bg-elevated px-2.5 py-1 text-xs font-semibold text-fg shadow-lg"
            >
              {hoveredTip.label}
            </motion.span>
          )}
        </AnimatePresence>

        <div
          className={clsx(
            'shrink-0 rounded-2xl border border-line transition hover:border-line-strong',
            collapsed ? 'm-2.5 flex flex-col items-center gap-1.5 p-2' : 'm-3 flex items-center gap-2 p-2.5',
          )}
        >
          {collapsed ? (
            <>
              <NotificationBell align="left" direction="up" subtle />
              <span
                title={`${user.nombre_completo} · ${user.rol_label || user.rol}`}
                className="grid size-8 shrink-0 place-items-center rounded-xl bg-primary text-xs font-bold text-white shadow-sm shadow-primary/25"
              >
                {iniciales(user.nombre_completo)}
              </span>
              <button
                onClick={logout}
                aria-label="Cerrar sesión"
                title="Cerrar sesión"
                className="rounded-lg p-1.5 text-muted transition hover:bg-hover hover:text-danger active:scale-95"
              >
                <LogOut className="size-4" />
              </button>
            </>
          ) : (
            <>
              <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-primary text-sm font-bold text-white shadow-sm shadow-primary/25">
                {iniciales(user.nombre_completo)}
              </span>
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-semibold">{user.nombre_completo}</p>
                <p className="truncate text-xs capitalize text-muted">{user.rol_label || user.rol}</p>
              </div>
              <div className="flex items-center gap-0.5">
                <NotificationBell align="left" direction="up" subtle />
                <button
                  onClick={logout}
                  aria-label="Cerrar sesión"
                  title="Cerrar sesión"
                  className="rounded-lg p-2 text-muted transition hover:bg-hover hover:text-danger active:scale-95"
                >
                  <LogOut className="size-[18px]" />
                </button>
              </div>
            </>
          )}
        </div>
      </motion.aside>

      <div className="flex min-w-0 flex-1 flex-col">
        {/* ── Barra superior (móvil) ─────────────────────────────────────── */}
        <header className="safe-top flex items-center justify-between border-b border-line bg-card px-4 py-3 lg:hidden">
          <BrandLogo compact />
          <NotificationBell align="right" />
        </header>

        <main className="flex-1 overflow-y-auto">
          <motion.div
            key={`${pathname}-${navSeq}`}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.24, ease: [0.22, 1, 0.36, 1] }}
            className="mx-auto w-full max-w-[1200px] px-4 py-6 pb-28 lg:px-8 lg:pb-10"
          >
            <Outlet />
          </motion.div>
        </main>

        {/* ── Barra inferior (móvil) ─────────────────────────────────────── */}
        <nav className="safe-bottom fixed inset-x-0 bottom-0 z-40 border-t border-line bg-card/95 backdrop-blur lg:hidden">
          <ul className="mx-auto flex max-w-xl items-stretch justify-around px-2 py-1.5">
            {bottom.map(i => <li key={i.to} className="flex-1"><BottomLink item={i} onClick={bumpNav} /></li>)}
            {rest.length > 0 && (
              <li className="flex-1">
                <button onClick={() => setMore(true)} aria-label="Más opciones" className="flex w-full flex-col items-center gap-0.5 rounded-xl py-1.5 transition active:scale-95">
                  <span className="grid size-8 place-items-center"><MoreHorizontal className="size-5" style={{ color: 'var(--text-muted)' }} /></span>
                  <span className="text-[10px] font-semibold text-muted">Más</span>
                </button>
              </li>
            )}
          </ul>
        </nav>

        <AnimatePresence>
          {more && (
            <div className="fixed inset-0 z-50 lg:hidden" role="dialog" aria-modal="true">
              <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.2 }}
                className="absolute inset-0 bg-black/55 backdrop-blur-[2px]"
                onClick={() => setMore(false)}
              />
              <motion.div
                initial={{ y: '100%' }}
                animate={{ y: 0 }}
                exit={{ y: '100%' }}
                transition={{ type: 'spring', stiffness: 360, damping: 32 }}
                className="safe-bottom absolute inset-x-0 bottom-0 rounded-t-3xl border-t border-line bg-card p-5 shadow-2xl"
              >
                <div className="mx-auto mb-3 h-1 w-10 rounded-full bg-line-strong" />
                <div className="mb-4 flex items-center justify-between">
                  <p className="text-sm font-bold">{user.nombre_completo}</p>
                  <button onClick={() => setMore(false)} aria-label="Cerrar" className="rounded-lg p-1.5 text-muted hover:bg-hover"><X className="size-5" /></button>
                </div>
                <div className="grid grid-cols-3 gap-3">
                  {rest.map((i, idx) => (
                    <motion.div
                      key={i.to}
                      initial={{ opacity: 0, y: 12, scale: 0.94 }}
                      animate={{ opacity: 1, y: 0, scale: 1 }}
                      transition={{ delay: idx * 0.04, type: 'spring', stiffness: 340, damping: 24 }}
                    >
                      <NavLink to={i.to} className="flex flex-col items-center gap-2 rounded-2xl bg-hover p-3 text-center transition active:scale-95">
                        <span className="grid size-10 place-items-center rounded-xl text-white shadow-sm" style={{ background: i.color }}><i.icon className="size-5" /></span>
                        <span className="text-xs font-semibold">{i.label}</span>
                      </NavLink>
                    </motion.div>
                  ))}
                </div>
                <button onClick={logout} className="mt-4 flex h-11 w-full items-center justify-center gap-2 rounded-xl border border-line text-sm font-semibold text-danger transition hover:bg-danger-soft active:scale-[0.98]">
                  <LogOut className="size-4" /> Cerrar sesión
                </button>
              </motion.div>
            </div>
          )}
        </AnimatePresence>
      </div>
    </div>
  )
}

function SideLink({
  item,
  collapsed,
  onClick,
  onHover,
}: {
  item: NavItem
  collapsed?: boolean
  onClick?: () => void
  onHover?: (label: string | null, top: number) => void
}) {
  return (
    <NavLink
      to={item.to}
      end={item.to === '/'}
      onClick={onClick}
      onMouseEnter={e => {
        if (collapsed && onHover) {
          const r = e.currentTarget.getBoundingClientRect()
          onHover(item.label, r.top + r.height / 2)
        }
      }}
      onMouseLeave={() => onHover?.(null, 0)}
      aria-label={item.label}
      className={({ isActive }) =>
        clsx(
          'group relative flex items-center rounded-xl py-1.5 text-sm font-semibold transition',
          collapsed ? 'justify-center px-0' : 'gap-3 pr-3 pl-3.5',
          isActive ? 'text-fg' : 'text-fg-2 hover:bg-hover',
        )
      }
    >
      {({ isActive }) => (
        <>
          {isActive && (
            <motion.span
              layoutId="sidebar-active-bg"
              transition={{ type: 'spring', stiffness: 400, damping: 36, bounce: 0 }}
              className="pointer-events-none absolute inset-0 overflow-hidden rounded-xl border border-line-strong bg-hover"
            >
              <span
                className={clsx(
                  'absolute top-2 bottom-2 w-1 rounded-full transition-colors duration-200',
                  collapsed ? 'left-1' : 'left-1.5',
                )}
                style={{ background: item.color }}
              />
            </motion.span>
          )}
          <span
            className="relative z-10 grid size-8 shrink-0 place-items-center rounded-lg transition-transform duration-200 group-hover:scale-105"
            style={isActive ? { background: item.color, color: '#fff' } : undefined}
          >
            <item.icon className="size-[18px]" />
          </span>
          {!collapsed && <span className="relative z-10 truncate">{item.label}</span>}
        </>
      )}
    </NavLink>
  )
}

function BottomLink({ item, onClick }: { item: NavItem; onClick?: () => void }) {
  return (
    <NavLink to={item.to} end={item.to === '/'} onClick={onClick} className="flex flex-col items-center gap-0.5 rounded-xl py-1.5">
      {({ isActive }) => (
        <>
          <span className="relative grid size-8 place-items-center rounded-xl transition">
            {isActive && (
              <motion.span
                layoutId="bottom-active-pill"
                transition={{ type: 'spring', stiffness: 400, damping: 30 }}
                className="absolute inset-0 rounded-xl shadow-sm"
                style={{ background: item.color }}
              />
            )}
            <item.icon className="relative z-10 size-5 transition-transform duration-200" style={{ color: isActive ? '#fff' : 'var(--text-muted)' }} />
          </span>
          <span className={clsx('text-[10px] font-semibold transition-colors', isActive ? 'text-fg' : 'text-muted')}>{item.label}</span>
        </>
      )}
    </NavLink>
  )
}

