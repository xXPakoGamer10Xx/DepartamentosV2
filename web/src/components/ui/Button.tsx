import clsx from 'clsx'
import { Loader2 } from 'lucide-react'
import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
const styles: Record<Variant, string> = {
  primary: 'bg-primary text-white hover:bg-primary-dark shadow-sm shadow-primary/20',
  secondary: 'bg-card text-fg border border-line hover:bg-hover',
  ghost: 'text-fg-2 hover:bg-hover',
  danger: 'bg-danger text-white hover:opacity-90',
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  loading?: boolean
  icon?: ReactNode
  full?: boolean
}

export function Button({ variant = 'primary', loading, icon, full, className, children, disabled, ...rest }: ButtonProps) {
  return (
    <button
      {...rest}
      disabled={disabled || loading}
      className={clsx(
        'inline-flex h-11 cursor-pointer items-center justify-center gap-2 whitespace-nowrap rounded-xl px-5 text-sm font-semibold transition active:scale-[0.98] disabled:cursor-not-allowed disabled:opacity-60',
        styles[variant], full && 'w-full', className,
      )}
    >
      {loading ? <Loader2 className="size-4 animate-spin" /> : icon}
      {children}
    </button>
  )
}
