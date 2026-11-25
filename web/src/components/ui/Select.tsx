import clsx from 'clsx'
import { ChevronDown } from 'lucide-react'
import { useId, type ReactNode, type SelectHTMLAttributes } from 'react'

export function Select({ label, icon, error, hint, className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement> & { label?: string; icon?: ReactNode; error?: string; hint?: string; children: ReactNode }) {
  const id = useId()
  const describedBy = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined

  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      {hint && <p id={`${id}-hint`} className="text-xs leading-snug text-muted">{hint}</p>}
      <div className="relative">
        {icon && <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted [&>svg]:size-[18px]" aria-hidden="true">{icon}</span>}
        <select
          {...rest}
          id={id}
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={describedBy}
          className={clsx(
            'min-h-12 w-full cursor-pointer appearance-none rounded-xl border bg-card py-2.5 pr-11 text-base font-medium text-fg transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25 sm:text-sm',
            icon ? 'pl-11' : 'pl-4', error ? 'border-danger' : 'border-line', className,
          )}
        >
          {children}
        </select>
        <ChevronDown className="pointer-events-none absolute right-3.5 top-1/2 size-4 -translate-y-1/2 text-fg-2" aria-hidden="true" />
      </div>
      {error && <p id={`${id}-error`} className="text-xs text-danger">{error}</p>}
    </div>
  )
}
