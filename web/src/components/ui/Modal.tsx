import { X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, type ReactNode } from 'react'

/** Diálogo modal: hoja inferior en móvil, tarjeta centrada en escritorio. Cierra con Esc o tocando fuera. */
export function Modal({ open, title, onClose, children }: { open: boolean; title: string; onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  return (
    <AnimatePresence>
      {open && (
        <div className="fixed inset-0 z-[90] flex items-end justify-center sm:items-center" role="dialog" aria-modal="true" aria-label={title}>
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.18 }}
            className="absolute inset-0 bg-black/60 backdrop-blur-[2px]"
            onClick={onClose}
          />
          <motion.div
            initial={{ opacity: 0, y: 36, scale: 0.96 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 28, scale: 0.96 }}
            transition={{ type: 'spring', stiffness: 380, damping: 32, bounce: 0 }}
            className="relative flex max-h-[88vh] w-full flex-col overflow-hidden rounded-t-3xl border border-line bg-card shadow-2xl sm:max-w-lg sm:rounded-3xl"
          >
            <div className="shrink-0 px-6 pt-6 pb-4 sm:px-7 sm:pt-7">
              <div className="mx-auto mb-3 h-1 w-10 rounded-full bg-line-strong sm:hidden" />
              <div className="flex items-center justify-between">
                <h2 className="text-lg font-extrabold tracking-tight">{title}</h2>
                <button onClick={onClose} aria-label="Cerrar" className="cursor-pointer rounded-lg p-1.5 text-muted transition hover:bg-hover hover:text-fg active:scale-95"><X className="size-5" /></button>
              </div>
            </div>
            <div className="custom-scrollbar flex-1 overflow-y-auto overscroll-contain px-6 pt-1 pb-[max(1.5rem,env(safe-area-inset-bottom))] sm:px-7 sm:pb-7">
              {children}
            </div>
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  )
}

