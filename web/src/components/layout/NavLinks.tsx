import clsx from 'clsx'
import { motion } from 'motion/react'
import { NavLink } from 'react-router-dom'
import { type NavItem } from './nav'


export function SideLink({
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


export function BottomLink({ item, onClick }: { item: NavItem; onClick?: () => void }) {
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
