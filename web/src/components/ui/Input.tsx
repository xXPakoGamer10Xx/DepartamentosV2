import clsx from 'clsx'
import { Eye, EyeOff } from 'lucide-react'
import { useId, useState, type InputHTMLAttributes, type ReactNode, type Ref } from 'react'
import { DateInput } from './campos/DateInput'

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  icon?: ReactNode
  error?: string
  /** Texto de ayuda bajo la etiqueta: explica para qué sirve el campo. */
  hint?: string
  ref?: Ref<HTMLInputElement>
}

export function Input({ label, icon, error, hint, className, type, ref, ...rest }: InputProps) {
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

  const describedBy = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined

  return (
    <div className="space-y-1.5">
      {label && <label htmlFor={id} className="block text-xs font-bold uppercase tracking-wide text-fg-2">{label}</label>}
      {hint && <p id={`${id}-hint`} className="text-xs leading-snug text-muted">{hint}</p>}
      <div className="relative">
        {icon && <span className="pointer-events-none absolute left-3.5 top-1/2 -translate-y-1/2 text-muted [&>svg]:size-[18px]" aria-hidden="true">{icon}</span>}
        <input
          {...rest}
          ref={ref}
          id={id}
          aria-invalid={error ? 'true' : 'false'}
          aria-describedby={describedBy}
          type={isPassword && show ? 'text' : type}
          className={clsx(
            'min-h-12 w-full rounded-xl border bg-card py-2.5 text-base text-fg placeholder:text-muted transition focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25 sm:text-sm',
            icon ? 'pl-11' : 'pl-4', isPassword ? 'pr-11' : 'pr-4',
            error ? 'border-danger' : 'border-line', className,
          )}
        />
        {isPassword && (
          <>
            <button
              type="button"
              onClick={() => setShow(s => !s)}
              aria-pressed={show}
              aria-label={show ? 'Ocultar contraseña' : 'Mostrar contraseña'}
              aria-describedby={`${id}-pwd-warn`}
              className="absolute right-3 top-1/2 -translate-y-1/2 rounded-md p-1 text-muted hover:text-fg"
            >
              {show ? <EyeOff className="size-[18px]" aria-hidden="true" /> : <Eye className="size-[18px]" aria-hidden="true" />}
            </button>
            <span id={`${id}-pwd-warn`} className="visually-hidden">Atención: esto mostrará tu contraseña en pantalla.</span>
          </>
        )}
      </div>
      {error && <p id={`${id}-error`} className="text-xs text-danger">{error}</p>}
    </div>
  )
}
