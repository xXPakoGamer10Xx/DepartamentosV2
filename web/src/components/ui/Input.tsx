import clsx from 'clsx'
import { Eye, EyeOff } from 'lucide-react'
import { useId, useState, type InputHTMLAttributes, type ReactNode } from 'react'
import { DateInput } from './campos/DateInput'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  icon?: ReactNode
  error?: string
  /** Texto de ayuda bajo la etiqueta: explica para qué sirve el campo. */
  hint?: string
}

export function Input({ label, icon, error, hint, className, type, ...rest }: InputProps) {
  const id = useId()
  const [show, setShow] = useState(false)
  const isPassword = type === 'password'

  if (type === 'date') {
    return (
      <DateInput
        id={id}
        label={label}
        icon={icon}
        error={error}
        hint={hint}
        className={className}
        {...rest}
      />
    )
  }

  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      {hint && <p id={`${id}-hint`} className="text-xs leading-snug text-muted">{hint}</p>}
      <div className="relative">
        {icon && <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted [&>svg]:size-[18px]">{icon}</span>}
        <input
          {...rest}
          id={id}
          aria-describedby={hint ? `${id}-hint` : undefined}
          type={isPassword && show ? 'text' : type}
          className={clsx(
            'h-12 w-full rounded-xl border bg-card text-sm text-fg placeholder:text-muted transition focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25',
            icon ? 'pl-11' : 'pl-4', isPassword ? 'pr-11' : 'pr-4',
            error ? 'border-danger' : 'border-line', className,
          )}
        />
        {isPassword && (
          <button type="button" onClick={() => setShow(s => !s)} aria-label={show ? 'Ocultar contraseña' : 'Mostrar contraseña'}
            className="absolute right-3 top-1/2 -translate-y-1/2 rounded-md p-1 text-muted hover:text-fg">
            {show ? <EyeOff className="size-[18px]" /> : <Eye className="size-[18px]" />}
          </button>
        )}
      </div>
      {error && <p className="text-xs text-danger">{error}</p>}
    </div>
  )
}
