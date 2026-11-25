import clsx from 'clsx'
import { Calendar, ChevronLeft, ChevronRight } from 'lucide-react'
import { useEffect, useId, useRef, useState, type ChangeEvent, type CSSProperties, type KeyboardEvent } from 'react'
import type { InputProps } from '../Input'

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

export function DateInput({
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

  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, '')
  const popoverId = `date-pop-${uid}`
  const anchorName = `--date-anchor-${uid}`

  const [todayISO] = useState(getTodayISO)
  const [open, setOpen] = useState(false)
  const [view, setView] = useState(() => {
    const p = parseISO(valStr) ?? parseISO(todayISO) ?? { y: 2026, m: 0, d: 1 }
    return { y: p.y, m: p.m }
  })

  const triggerRef = useRef<HTMLButtonElement>(null)
  const popRef = useRef<HTMLDivElement>(null)
  const dayRefs = useRef<Array<HTMLButtonElement | null>>([])

  useEffect(() => {
    const el = popRef.current
    if (!el) return
    const onToggle = (e: Event) => {
      const te = e as ToggleEvent
      const isOpen = te.newState === 'open'
      setOpen(isOpen)
      if (isOpen) {
        const p = parseISO(valStr) ?? parseISO(todayISO)
        if (p) setView({ y: p.y, m: p.m })
      }
    }
    el.addEventListener('toggle', onToggle)
    return () => el.removeEventListener('toggle', onToggle)
  }, [valStr, todayISO])

  const closePopover = () => {
    try {
      popRef.current?.hidePopover()
    } catch {
      setOpen(false)
    }
    triggerRef.current?.focus()
  }

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

  const onDayKeyDown = (e: KeyboardEvent<HTMLButtonElement>, idx: number) => {
    const delta =
      e.key === 'ArrowRight' ? 1
        : e.key === 'ArrowLeft' ? -1
          : e.key === 'ArrowDown' ? 7
            : e.key === 'ArrowUp' ? -7
              : 0
    if (!delta) return
    e.preventDefault()
    const nextIdx = idx + delta
    if (nextIdx >= 0 && nextIdx < cells.length) {
      dayRefs.current[nextIdx]?.focus()
    } else if (delta < 0) {
      prevMonth()
    } else {
      nextMonth()
    }
  }

  const display = formatDisplayDate(valStr)
  const years = Array.from({ length: 16 }, (_, i) => view.y - 7 + i)
  const describedBy = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined

  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      {hint && <p id={`${id}-hint`} className="text-xs leading-snug text-muted">{hint}</p>}
      <div className="relative">
        {icon && <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted [&>svg]:size-[18px]" aria-hidden="true">{icon}</span>}
        <button
          ref={triggerRef}
          id={id}
          type="button"
          disabled={disabled}
          popoverTarget={popoverId}
          aria-label={ariaLabel ?? label ?? 'Seleccionar fecha'}
          aria-expanded={open}
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={describedBy}
          style={{ anchorName } as CSSProperties}
          className={clsx(
            'flex min-h-12 w-full items-center justify-between rounded-xl border bg-card pr-3.5 text-left text-base transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25 sm:text-sm',
            icon ? 'pl-11' : 'pl-4',
            open ? 'border-primary ring-2 ring-primary/25' : error ? 'border-danger' : 'border-line',
            display ? 'font-medium text-fg' : 'text-muted',
            className,
          )}
        >
          <span className="truncate">{display || placeholder || 'dd/mm/aaaa'}</span>
          <Calendar aria-hidden="true" className={clsx('size-4 shrink-0 transition', open ? 'text-primary' : 'text-fg-2')} />
        </button>

        {/* Renderizado en Top Layer (popover="auto") dentro del subárbol para funcionar también dentro de <dialog>. */}
        <div
          ref={popRef}
          id={popoverId}
          {...({ popover: 'auto' } as Record<string, string>)}
          role="dialog"
          aria-label="Calendario"
          style={{ positionAnchor: anchorName } as CSSProperties}
          className="anchored-popover w-72 select-none"
        >
          <div className="rounded-2xl border border-line-strong bg-elevated p-3.5 shadow-2xl shadow-black/45">
            {/* Encabezado mes / año */}
            <div className="mb-3 flex items-center justify-between gap-1">
              <button
                type="button"
                onClick={prevMonth}
                aria-label="Mes anterior"
                className="grid size-8 place-items-center rounded-lg text-fg-2 transition hover:bg-hover hover:text-fg active:scale-95"
              >
                <ChevronLeft className="size-4" aria-hidden="true" />
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
                <ChevronRight className="size-4" aria-hidden="true" />
              </button>
            </div>

            {/* Días de la semana */}
            <div className="mb-1.5 grid grid-cols-7 text-center" aria-hidden="true">
              {DIAS_SEMANA.map(d => (
                <span key={d} className="py-1 text-[11px] font-bold uppercase tracking-wider text-muted">
                  {d}
                </span>
              ))}
            </div>

            {/* Cuadrícula de días */}
            <div className="grid grid-cols-7 gap-1">
              {cells.map((c, idx) => {
                const selected = c.iso === valStr
                const isToday = c.iso === todayISO
                const disabledDay = isOut(c.iso)
                return (
                  <button
                    key={c.iso}
                    ref={el => { dayRefs.current[idx] = el }}
                    type="button"
                    disabled={disabledDay}
                    aria-pressed={selected}
                    aria-label={formatDisplayDate(c.iso)}
                    onKeyDown={e => onDayKeyDown(e, idx)}
                    onClick={() => {
                      emitChange(c.iso)
                      closePopover()
                    }}
                    className={clsx(
                      'grid h-8 w-full place-items-center rounded-xl text-xs font-semibold tabular-nums transition active:scale-95',
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
                  closePopover()
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
                  closePopover()
                }}
                className="rounded-lg px-2.5 py-1 text-xs font-bold text-primary transition hover:bg-primary-soft disabled:opacity-40"
              >
                Hoy
              </button>
            </div>
          </div>
        </div>
      </div>
      {error && <p id={`${id}-error`} className="text-xs text-danger">{error}</p>}
    </div>
  )
}
