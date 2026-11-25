import { X } from 'lucide-react'
import { motion } from 'motion/react'
import { useEffect, useId, useRef, type ReactNode } from 'react'

const fueraDeDialogo = (e: PointerEvent, dialog: HTMLDialogElement) => {
  const r = dialog.getBoundingClientRect()
  return e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom
}

/** Diálogo modal nativo (<dialog> en Top Layer): hoja inferior en móvil, tarjeta centrada en escritorio. Cierra con Esc, gesto atrás o tocando fuera. */
export function Modal({ open, title, onClose, children }: { open: boolean; title: string; onClose: () => void; children: ReactNode }) {
  const titleId = useId()
  const dialogRef = useRef<HTMLDialogElement>(null)
  const onCloseRef = useRef(onClose)

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return

    if (open) {
      if (!dialog.open) dialog.showModal()
    } else if (dialog.open) {
      dialog.close()
    }
  }, [open])

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog || !open) return

    const onNativeClose = () => {
      dialog.returnValue = ''
      onCloseRef.current()
    }
    dialog.addEventListener('close', onNativeClose)

    // Respaldo para navegadores sin closedby="any" (p. ej. Safari): exige presionar y soltar fuera del cuadro.
    if (!('closedBy' in HTMLDialogElement.prototype)) {
      let pressedOutside = false
      const onPointerDown = (e: PointerEvent) => { pressedOutside = fueraDeDialogo(e, dialog) }
      const onPointerUp = (e: PointerEvent) => {
        if (pressedOutside && fueraDeDialogo(e, dialog)) dialog.close()
        pressedOutside = false
      }
      dialog.addEventListener('pointerdown', onPointerDown)
      dialog.addEventListener('pointerup', onPointerUp)
      return () => {
        dialog.removeEventListener('close', onNativeClose)
        dialog.removeEventListener('pointerdown', onPointerDown)
        dialog.removeEventListener('pointerup', onPointerUp)
      }
    }

    return () => {
      dialog.removeEventListener('close', onNativeClose)
    }
  }, [open])

  if (!open) return null

  return (
    <dialog
      ref={dialogRef}
      aria-labelledby={titleId}
      {...({ closedby: 'any' } as Record<string, string>)}
      className="native-dialog mt-auto mb-0 w-full flex-col sm:m-auto sm:max-w-lg"
    >
      <motion.div
        initial={{ opacity: 0, y: 24, scale: 0.97 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ type: 'spring', stiffness: 380, damping: 32, bounce: 0 }}
        className="flex max-h-[88dvh] w-full flex-col overflow-hidden rounded-t-3xl border border-line bg-card shadow-2xl sm:rounded-3xl"
      >
        <div className="shrink-0 px-6 pt-6 pb-4 sm:px-7 sm:pt-7">
          <div className="mx-auto mb-3 h-1 w-10 rounded-full bg-line-strong sm:hidden" aria-hidden="true" />
          <div className="flex items-center justify-between gap-3">
            <h1 id={titleId} className="text-lg font-extrabold tracking-tight">{title}</h1>
            <button
              type="button"
              onClick={() => dialogRef.current?.close()}
              aria-label="Cerrar"
              className="cursor-pointer rounded-lg p-1.5 text-muted transition hover:bg-hover hover:text-fg active:scale-95"
            >
              <X className="size-5" aria-hidden="true" />
            </button>
          </div>
        </div>
        <div className="custom-scrollbar flex-1 overflow-y-auto overscroll-contain px-6 pt-1 pb-[max(1.5rem,env(safe-area-inset-bottom))] sm:px-7 sm:pb-7">
          {children}
        </div>
      </motion.div>
    </dialog>
  )
}
