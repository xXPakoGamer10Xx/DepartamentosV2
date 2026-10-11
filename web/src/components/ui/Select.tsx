import clsx from 'clsx'
import { ChevronDown } from 'lucide-react'
import { useId, type ReactNode, type SelectHTMLAttributes } from 'react'

export function Select({ label, icon, error, hint, className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement> & { label?: string; icon?: ReactNode; error?: string; hint?: string; children: ReactNode }) {
  const id = useId()
  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      {hint && <p id={`${id}-hint`} className="text-xs leading-snug text-muted">{hint}</p>}
      <div className="relative">
        {icon && <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted [&>svg]:size-[18px]">{icon}</span>}
        <select {...rest} id={id} aria-describedby={hint ? `${id}-hint` : undefined}
          className={clsx('h-12 w-full cursor-pointer appearance-none rounded-xl border bg-card pr-11 text-sm font-medium text-fg transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25',
            icon ? 'pl-11' : 'pl-4', error ? 'border-danger' : 'border-line', className)}>
          {children}
        </select>
        <ChevronDown className="pointer-events-none absolute right-3.5 top-1/2 size-4 -translate-y-1/2 text-fg-2" />
      </div>
      {error && <p className="text-xs text-danger">{error}</p>}
    </div>
  )
}
