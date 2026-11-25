import clsx from 'clsx'
import { CheckCircle2, Info, X, XCircle } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'

type Tone = 'success' | 'error' | 'info'
interface ToastItem { id: number; msg: string; tone: Tone }
const Ctx = createContext<(msg: string, tone?: Tone) => void>(() => {})
export const useToast = () => useContext(Ctx)

const icons = { success: CheckCircle2, error: XCircle, info: Info }
const colors: Record<Tone, string> = { success: 'text-success', error: 'text-danger', info: 'text-primary' }
const bars: Record<Tone, string> = { success: 'bg-success', error: 'bg-danger', info: 'bg-primary' }
const TOAST_MS = 4500

type DocWithAriaNotify = Document & { ariaNotify?: (msg: string, options?: { priority?: 'normal' | 'high' }) => void }

const isPopoverOpen = (el: HTMLElement) =>
  typeof CSS !== 'undefined' && CSS.supports('selector(:popover-open)')
    ? el.matches(':popover-open')
    : el.classList.contains(':popover-open')

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([])
  const [liveMsg, setLiveMsg] = useState('')
  const nextId = useRef(1)
  const timers = useRef<Map<number, ReturnType<typeof setTimeout>>>(new Map())
  const paused = useRef<Set<number>>(new Set())
  const popoverRef = useRef<HTMLDivElement>(null)
  const closeRefs = useRef<Map<number, HTMLButtonElement | null>>(new Map())

  const clearTimer = useCallback((id: number) => {
    const t = timers.current.get(id)
    if (t) {
      clearTimeout(t)
      timers.current.delete(id)
    }
  }, [])

  const remove = useCallback((id: number, fromUser = false) => {
    clearTimer(id)
    paused.current.delete(id)
    setItems(prev => {
      const idx = prev.findIndex(t => t.id === id)
      const next = prev.filter(t => t.id !== id)
      if (fromUser && next.length > 0) {
        const focusTarget = next[Math.min(idx, next.length - 1)]
        queueMicrotask(() => closeRefs.current.get(focusTarget.id)?.focus())
      }
      return next
    })
  }, [clearTimer])

  const scheduleDismiss = useCallback((id: number, ms = TOAST_MS) => {
    clearTimer(id)
    if (paused.current.has(id)) return
    timers.current.set(id, setTimeout(() => remove(id), ms))
  }, [clearTimer, remove])

  const pauseToast = useCallback((id: number) => {
    paused.current.add(id)
    clearTimer(id)
  }, [clearTimer])

  const resumeToast = useCallback((id: number) => {
    paused.current.delete(id)
    scheduleDismiss(id, 2000)
  }, [scheduleDismiss])

  const announce = useCallback((msg: string, tone: Tone) => {
    const doc = document as DocWithAriaNotify
    if (typeof doc.ariaNotify === 'function') {
      doc.ariaNotify(msg, { priority: tone === 'error' ? 'high' : 'normal' })
    } else {
      setLiveMsg('')
      requestAnimationFrame(() => setLiveMsg(msg))
    }
  }, [])

  const push = useCallback((msg: string, tone: Tone = 'info') => {
    const id = nextId.current++
    setItems(s => [...s, { id, msg, tone }])
    announce(msg, tone)
    scheduleDismiss(id)
  }, [announce, scheduleDismiss])

  // Promueve la región de toasts al Top Layer (popover="manual") para que siempre quede encima de <dialog> nativos.
  useEffect(() => {
    const el = popoverRef.current
    if (!el || typeof el.showPopover !== 'function') return
    try {
      if (items.length > 0) {
        if (isPopoverOpen(el)) el.hidePopover()
        el.showPopover()
      } else if (isPopoverOpen(el)) {
        el.hidePopover()
      }
    } catch {
      // Ignora si el polyfill aún está inicializando.
    }
  }, [items.length])

  useEffect(() => {
    const map = timers.current
    return () => {
      map.forEach(t => clearTimeout(t))
      map.clear()
    }
  }, [])

  const value = useMemo(() => push, [push])

  return (
    <Ctx.Provider value={value}>
      {children}
      {/* Región live de respaldo cuando document.ariaNotify() no está disponible: solo anuncia el mensaje, no los controles. */}
      <p className="visually-hidden">Avisos del sistema</p>
      <div aria-live="polite" aria-atomic="true" className="visually-hidden">{liveMsg}</div>

      <div
        ref={popoverRef}
        {...({ popover: 'manual' } as Record<string, string>)}
        role="region"
        aria-label="Notificaciones"
        className="toast-region safe-top flex flex-col items-center gap-2 p-4"
      >
        <AnimatePresence mode="popLayout">
          {items.map(t => {
            const Icon = icons[t.tone]
            return (
              <motion.div
                key={t.id}
                layout
                initial={{ opacity: 0, y: -24, scale: 0.92 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -16, scale: 0.94 }}
                transition={{ type: 'spring', stiffness: 400, damping: 28 }}
                onMouseEnter={() => pauseToast(t.id)}
                onMouseLeave={() => resumeToast(t.id)}
                onFocusCapture={() => pauseToast(t.id)}
                onBlurCapture={e => {
                  if (!e.currentTarget.contains(e.relatedTarget as Node | null)) resumeToast(t.id)
                }}
                className="pointer-events-auto relative flex w-full max-w-sm items-start gap-3 overflow-hidden rounded-2xl border border-line bg-elevated px-4 py-3 shadow-xl shadow-black/20"
              >
                <Icon className={clsx('mt-0.5 size-5 shrink-0', colors[t.tone])} aria-hidden="true" />
                <p className="flex-1 text-sm font-medium">{t.msg}</p>
                <button
                  ref={el => { closeRefs.current.set(t.id, el) }}
                  type="button"
                  onClick={() => remove(t.id, true)}
                  aria-label="Cerrar aviso"
                  className="rounded-md p-0.5 text-muted transition hover:text-fg"
                >
                  <X className="size-4" aria-hidden="true" />
                </button>
                <motion.span
                  aria-hidden="true"
                  initial={{ scaleX: 1 }}
                  animate={{ scaleX: 0 }}
                  transition={{ duration: TOAST_MS / 1000, ease: 'linear' }}
                  style={{ transformOrigin: 'left' }}
                  className={clsx('absolute inset-x-0 bottom-0 h-0.5 opacity-70', bars[t.tone])}
                />
              </motion.div>
            )
          })}
        </AnimatePresence>
      </div>
    </Ctx.Provider>
  )
}
