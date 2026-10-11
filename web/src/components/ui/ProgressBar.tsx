import { motion } from 'motion/react'

export function ProgressBar({ value, tone = 'success' }: { value: number; tone?: 'success' | 'primary' | 'warning' | 'danger' }) {
  const c = { success: 'bg-success', primary: 'bg-primary', warning: 'bg-warning', danger: 'bg-danger' }[tone]
  const clamped = Math.max(0, Math.min(100, value))
  return (
    <div className="h-2 overflow-hidden rounded-full bg-hover" role="progressbar" aria-valuenow={Math.round(value)} aria-valuemin={0} aria-valuemax={100}>
      <motion.div
        initial={{ width: 0 }}
        animate={{ width: `${clamped}%` }}
        transition={{ type: 'spring', stiffness: 120, damping: 22 }}
        className={`relative h-full overflow-hidden rounded-full ${c}`}
      >
        {clamped > 0 && clamped < 100 && (
          <span className="animate-shimmer absolute inset-y-0 w-1/2 bg-gradient-to-r from-transparent via-white/30 to-transparent" />
        )}
      </motion.div>
    </div>
  )
}

