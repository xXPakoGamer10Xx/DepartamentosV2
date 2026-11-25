import type { CSSProperties } from 'react'

const fillVar: Record<'success' | 'primary' | 'warning' | 'danger', string> = {
  success: 'var(--success)',
  primary: 'var(--primary)',
  warning: 'var(--warning)',
  danger: 'var(--danger)',
}

export function ProgressBar({ value, tone = 'success', label = 'Progreso' }: { value: number; tone?: 'success' | 'primary' | 'warning' | 'danger'; label?: string }) {
  const clamped = Math.max(0, Math.min(100, Math.round(value)))
  return (
    <progress
      value={clamped}
      max={100}
      aria-label={label}
      className="native-progress"
      style={{ '--progress-fill': fillVar[tone] } as CSSProperties}
    />
  )
}
