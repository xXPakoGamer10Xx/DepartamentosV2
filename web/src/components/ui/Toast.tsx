import clsx from 'clsx'
import { CheckCircle2, Info, X, XCircle } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'

type Tone = 'success' | 'error' | 'info'
interface ToastItem { id: number; msg: string; tone: Tone }
const Ctx = createContext<(msg: string, tone?: Tone) => void>(() => {})
export const useToast = () => useContext(Ctx)

const icons = { success: CheckCircle2, error: XCircle, info: Info }
const colors: Record<Tone, string> = { success: 'text-success', error: 'text-danger', info: 'text-primary' }
const bars: Record<Tone, string> = { success: 'bg-success', error: 'bg-danger', info: 'bg-primary' }

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([])
  const remove = useCallback((id: number) => setItems(s => s.filter(t => t.id !== id)), [])
  const push = useCallback((msg: string, tone: Tone = 'info') => {
    const id = Date.now() + Math.random()
    setItems(s => [...s, { id, msg, tone }])
    setTimeout(() => remove(id), 4500)
  }, [remove])
  const value = useMemo(() => push, [push])

  return (
    <Ctx.Provider value={value}>
      {children}
      <div className="pointer-events-none fixed inset-x-0 top-0 z-[100] flex flex-col items-center gap-2 p-4 safe-top" aria-live="polite">
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
                className="pointer-events-auto relative flex w-full max-w-sm items-start gap-3 overflow-hidden rounded-2xl border border-line bg-elevated px-4 py-3 shadow-xl shadow-black/20"
              >
                <Icon className={clsx('mt-0.5 size-5 shrink-0', colors[t.tone])} />
                <p className="flex-1 text-sm font-medium">{t.msg}</p>
                <button
                  type="button"
                  onClick={() => remove(t.id)}
                  aria-label="Cerrar aviso"
                  className="rounded-md p-0.5 text-muted transition hover:text-fg"
                >
                  <X className="size-4" />
                </button>
                <motion.span
                  initial={{ scaleX: 1 }}
                  animate={{ scaleX: 0 }}
                  transition={{ duration: 4.5, ease: 'linear' }}
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

