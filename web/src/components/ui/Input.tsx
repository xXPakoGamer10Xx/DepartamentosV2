import clsx from 'clsx'
import { Calendar, ChevronLeft, ChevronRight, Eye, EyeOff } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useId, useRef, useState, type ChangeEvent, type InputHTMLAttributes, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  icon?: ReactNode
  error?: string
  /** Texto de ayuda bajo la etiqueta: explica para qué sirve el campo. */
  hint?: string
}

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
]
const DIAS_SEMANA = ['Do', 'Lu', 'Ma', 'Mi', 'Ju', 'Vi', 'Sá']

function getTodayISO(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function parseISO(iso?: string): { y: number; m: number; d: number } | null {
  if (!iso || !/^\d{4}-\d{2}-\d{2}$/.test(iso)) return null
  const [y, m, d] = iso.split('-').map(Number)
  if (!y || !m || !d) return null
  return { y, m: m - 1, d }
}

function formatDisplayDate(iso?: string): string {
  const p = parseISO(iso)
  if (!p) return ''
  return `${String(p.d).padStart(2, '0')}/${String(p.m + 1).padStart(2, '0')}/${p.y}`
}

function toISO(y: number, m: number, d: number): string {
  return `${y}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`
}

export function Input({ label, icon, error, hint, className, type, ...rest }: InputProps) {
  const id = useId()
  const [show, setShow] = useState(false)
  const isPassword = type === 'password'

  if (type === 'date') {
    return (
      <DateInput
        id={id}
        label={label}
        icon={icon}
        error={error}
        hint={hint}
        className={className}
        {...rest}
      />
    )
  }

  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      {hint && <p id={`${id}-hint`} className="text-xs leading-snug text-muted">{hint}</p>}
      <div className="relative">
        {icon && <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted [&>svg]:size-[18px]">{icon}</span>}
        <input
          {...rest}
          id={id}
          aria-describedby={hint ? `${id}-hint` : undefined}
          type={isPassword && show ? 'text' : type}
          className={clsx(
            'h-12 w-full rounded-xl border bg-card text-sm text-fg placeholder:text-muted transition focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25',
            icon ? 'pl-11' : 'pl-4', isPassword ? 'pr-11' : 'pr-4',
            error ? 'border-danger' : 'border-line', className,
          )}
        />
        {isPassword && (
          <button type="button" onClick={() => setShow(s => !s)} aria-label={show ? 'Ocultar contraseña' : 'Mostrar contraseña'}
            className="absolute right-3 top-1/2 -translate-y-1/2 rounded-md p-1 text-muted hover:text-fg">
            {show ? <EyeOff className="size-[18px]" /> : <Eye className="size-[18px]" />}
          </button>
        )}
      </div>
      {error && <p className="text-xs text-danger">{error}</p>}
    </div>
  )
}

function DateInput({
  id,
  label,
  icon,
  error,
  hint,
  className,
  value,
  min,
  max,
  disabled,
  placeholder,
  onChange,
  'aria-label': ariaLabel,
}: InputProps & { id: string }) {
  const valStr = typeof value === 'string' ? value : ''
  const minStr = typeof min === 'string' ? min : undefined
  const maxStr = typeof max === 'string' ? max : undefined

  const [todayISO] = useState(getTodayISO)
  const [open, setOpen] = useState(false)
  const [view, setView] = useState(() => {
    const p = parseISO(valStr) ?? parseISO(todayISO) ?? { y: 2026, m: 0, d: 1 }
    return { y: p.y, m: p.m }
  })
  const [pos, setPos] = useState<{ top: number; left: number; placeUp: boolean }>({ top: 0, left: 0, placeUp: false })

  const triggerRef = useRef<HTMLButtonElement>(null)
  const popRef = useRef<HTMLDivElement>(null)

  const computePos = () => {
    const el = triggerRef.current
    if (!el) return
    const r = el.getBoundingClientRect()
    const popHeight = 336
    const popWidth = 288
    const spaceBelow = window.innerHeight - r.bottom
    const placeUp = spaceBelow < popHeight && r.top > popHeight
    const left = Math.max(12, Math.min(r.left, window.innerWidth - popWidth - 12))
    const top = placeUp ? r.top - 8 : r.bottom + 8
    setPos({ top, left, placeUp })
  }

  const toggleOpen = () => {
    if (disabled) return
    if (!open) {
      const p = parseISO(valStr) ?? parseISO(todayISO)
      if (p) setView({ y: p.y, m: p.m })
      computePos()
    }
    setOpen(o => !o)
  }

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node
      if (!triggerRef.current?.contains(t) && !popRef.current?.contains(t)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    const onScrollOrResize = () => computePos()
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    window.addEventListener('resize', onScrollOrResize)
    window.addEventListener('scroll', onScrollOrResize, true)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('resize', onScrollOrResize)
      window.removeEventListener('scroll', onScrollOrResize, true)
    }
  }, [open])

  const emitChange = (nextISO: string) => {
    onChange?.({ target: { value: nextISO }, currentTarget: { value: nextISO } } as ChangeEvent<HTMLInputElement>)
  }

  const isOut = (iso: string) => {
    if (minStr && iso < minStr) return true
    if (maxStr && iso > maxStr) return true
    return false
  }

  const prevMonth = () => setView(v => (v.m === 0 ? { y: v.y - 1, m: 11 } : { y: v.y, m: v.m - 1 }))
  const nextMonth = () => setView(v => (v.m === 11 ? { y: v.y + 1, m: 0 } : { y: v.y, m: v.m + 1 }))

  // Construcción de las 6 filas (42 celdas) del mes visible
  const firstWeekday = new Date(view.y, view.m, 1).getDay()
  const daysInMonth = new Date(view.y, view.m + 1, 0).getDate()
  const daysInPrev = new Date(view.y, view.m, 0).getDate()

  const cells: { iso: string; day: number; inMonth: boolean }[] = []
  for (let i = firstWeekday - 1; i >= 0; i--) {
    const pm = view.m === 0 ? 11 : view.m - 1
    const py = view.m === 0 ? view.y - 1 : view.y
    const d = daysInPrev - i
    cells.push({ iso: toISO(py, pm, d), day: d, inMonth: false })
  }
  for (let d = 1; d <= daysInMonth; d++) {
    cells.push({ iso: toISO(view.y, view.m, d), day: d, inMonth: true })
  }
  const rem = 42 - cells.length
  for (let d = 1; d <= rem; d++) {
    const nm = view.m === 11 ? 0 : view.m + 1
    const ny = view.m === 11 ? view.y + 1 : view.y
    cells.push({ iso: toISO(ny, nm, d), day: d, inMonth: false })
  }

  const display = formatDisplayDate(valStr)
  const years = Array.from({ length: 16 }, (_, i) => view.y - 7 + i)

  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      {hint && <p id={`${id}-hint`} className="text-xs leading-snug text-muted">{hint}</p>}
      <div className="relative">
        {icon && <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted [&>svg]:size-[18px]">{icon}</span>}
        <button
          ref={triggerRef}
          id={id}
          type="button"
          disabled={disabled}
          aria-label={ariaLabel ?? label ?? 'Seleccionar fecha'}
          aria-expanded={open}
          aria-describedby={hint ? `${id}-hint` : undefined}
          onClick={toggleOpen}
          className={clsx(
            'flex h-12 w-full items-center justify-between rounded-xl border bg-card pr-3.5 text-left text-sm transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25',
            icon ? 'pl-11' : 'pl-4',
            open ? 'border-primary ring-2 ring-primary/25' : error ? 'border-danger' : 'border-line',
            display ? 'font-medium text-fg' : 'text-muted',
            className,
          )}
        >
          <span className="truncate">{display || placeholder || 'dd/mm/aaaa'}</span>
          <Calendar className={clsx('size-4 shrink-0 transition', open ? 'text-primary' : 'text-fg-2')} />
        </button>
      </div>
      {error && <p className="text-xs text-danger">{error}</p>}

      {typeof document !== 'undefined' &&
        createPortal(
          <AnimatePresence>
            {open && (
              <motion.div
                ref={popRef}
                role="dialog"
                aria-label="Calendario"
                initial={{ opacity: 0, y: pos.placeUp ? 6 : -6, scale: 0.96 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: pos.placeUp ? 4 : -4, scale: 0.96 }}
                transition={{ type: 'spring', stiffness: 420, damping: 30 }}
                style={{
                  position: 'fixed',
                  top: pos.placeUp ? undefined : pos.top,
                  bottom: pos.placeUp ? window.innerHeight - pos.top : undefined,
                  left: pos.left,
                }}
                className="z-[120] w-72 select-none rounded-2xl border border-line-strong bg-elevated p-3.5 shadow-2xl shadow-black/45"
              >
                {/* Encabezado mes / año */}
                <div className="mb-3 flex items-center justify-between gap-1">
                  <button
                    type="button"
                    onClick={prevMonth}
                    aria-label="Mes anterior"
                    className="grid size-8 place-items-center rounded-lg text-fg-2 transition hover:bg-hover hover:text-fg active:scale-95"
                  >
                    <ChevronLeft className="size-4" />
                  </button>

                  <div className="flex items-center gap-1.5">
                    <select
                      aria-label="Mes"
                      value={view.m}
                      onChange={e => setView(v => ({ ...v, m: Number(e.target.value) }))}
                      className="cursor-pointer appearance-none rounded-lg bg-transparent px-1.5 py-1 text-center text-xs font-extrabold text-fg transition hover:bg-hover focus:outline-none"
                    >
                      {MESES.map((m, idx) => (
                        <option key={m} value={idx} className="bg-card text-fg">{m}</option>
                      ))}
                    </select>
                    <select
                      aria-label="Año"
                      value={view.y}
                      onChange={e => setView(v => ({ ...v, y: Number(e.target.value) }))}
                      className="cursor-pointer appearance-none rounded-lg bg-transparent px-1.5 py-1 text-center text-xs font-extrabold text-fg transition hover:bg-hover focus:outline-none"
                    >
                      {years.map(y => (
                        <option key={y} value={y} className="bg-card text-fg">{y}</option>
                      ))}
                    </select>
                  </div>

                  <button
                    type="button"
                    onClick={nextMonth}
                    aria-label="Mes siguiente"
                    className="grid size-8 place-items-center rounded-lg text-fg-2 transition hover:bg-hover hover:text-fg active:scale-95"
                  >
                    <ChevronRight className="size-4" />
                  </button>
                </div>

                {/* Días de la semana */}
                <div className="mb-1.5 grid grid-cols-7 text-center">
                  {DIAS_SEMANA.map(d => (
                    <span key={d} className="py-1 text-[11px] font-bold uppercase tracking-wider text-muted">
                      {d}
                    </span>
                  ))}
                </div>

                {/* Cuadrícula de días */}
                <div className="grid grid-cols-7 gap-1">
                  {cells.map(c => {
                    const selected = c.iso === valStr
                    const isToday = c.iso === todayISO
                    const disabledDay = isOut(c.iso)
                    return (
                      <button
                        key={c.iso}
                        type="button"
                        disabled={disabledDay}
                        onClick={() => {
                          emitChange(c.iso)
                          setOpen(false)
                        }}
                        className={clsx(
                          'grid h-8 w-full place-items-center rounded-xl text-xs font-semibold transition active:scale-95',
                          selected
                            ? 'bg-primary font-bold text-white shadow-sm shadow-primary/30'
                            : isToday
                              ? 'border border-primary/50 bg-primary-soft font-bold text-primary'
                              : c.inMonth
                                ? 'text-fg hover:bg-hover'
                                : 'text-muted/45 hover:bg-hover',
                          disabledDay && 'pointer-events-none opacity-25',
                        )}
                      >
                        {c.day}
                      </button>
                    )
                  })}
                </div>

                {/* Pie del calendario */}
                <div className="mt-3 flex items-center justify-between border-t border-line pt-2.5">
                  <button
                    type="button"
                    onClick={() => {
                      emitChange('')
                      setOpen(false)
                    }}
                    className="rounded-lg px-2 py-1 text-xs font-semibold text-muted transition hover:bg-hover hover:text-danger"
                  >
                    Borrar
                  </button>
                  <button
                    type="button"
                    disabled={isOut(todayISO)}
                    onClick={() => {
                      emitChange(todayISO)
                      setOpen(false)
                    }}
                    className="rounded-lg px-2.5 py-1 text-xs font-bold text-primary transition hover:bg-primary-soft disabled:opacity-40"
                  >
                    Hoy
                  </button>
                </div>
              </motion.div>
            )}
          </AnimatePresence>,
          document.body,
        )}
    </div>
  )
}

