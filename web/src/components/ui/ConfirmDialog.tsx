import { TriangleAlert } from 'lucide-react'
import { motion } from 'motion/react'
import type { ReactNode } from 'react'
import { Button } from './Button'
import { Modal } from './Modal'
import { ShakeError } from './MotionPrimitives'

/** Confirmación de acciones destructivas. Muestra el error del servidor si la acción falla. */
export function ConfirmDialog({ open, title, children, confirmLabel, loading, error, onConfirm, onClose, tone = 'danger' }: {
  open: boolean; title: string; children: ReactNode; confirmLabel: string; loading?: boolean; error?: string | null
  onConfirm: () => void; onClose: () => void; tone?: 'danger' | 'primary'
}) {
  return (
    <Modal open={open} title={title} onClose={onClose}>
      <div className="space-y-5">
        <div className="flex gap-3">
          <motion.span
            initial={{ scale: 0.7, rotate: -10 }}
            animate={{ scale: 1, rotate: 0 }}
            transition={{ type: 'spring', stiffness: 360, damping: 18 }}
            className={`grid size-11 shrink-0 place-items-center rounded-2xl ${tone === 'danger' ? 'bg-danger-soft text-danger' : 'bg-primary-soft text-primary'}`}
          >
            <TriangleAlert className="size-5" />
          </motion.span>
          <div className="text-sm text-fg-2">{children}</div>
        </div>
        <ShakeError error={error}>
          <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>
        </ShakeError>
        <div className="grid grid-cols-2 gap-2.5 pt-1">
          <Button variant="secondary" onClick={onClose} disabled={loading}>Cancelar</Button>
          <Button variant={tone === 'danger' ? 'danger' : 'primary'} onClick={onConfirm} loading={loading}>{confirmLabel}</Button>
        </div>
      </div>
    </Modal>
  )
}

