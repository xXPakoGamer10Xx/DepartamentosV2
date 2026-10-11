import clsx from 'clsx'
import { useId, type TextareaHTMLAttributes } from 'react'

export function Textarea({ label, error, className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement> & { label?: string; error?: string }) {
  const id = useId()
  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      <textarea {...rest} id={id}
        className={clsx('min-h-28 w-full resize-none rounded-xl border bg-card p-4 text-sm text-fg placeholder:text-muted transition focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25', error ? 'border-danger' : 'border-line', className)} />
      {error && <p className="text-xs text-danger">{error}</p>}
    </div>
  )
}
