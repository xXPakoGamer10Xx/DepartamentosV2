import clsx from 'clsx'
import { Lock, Mail } from 'lucide-react'
import { motion } from 'motion/react'
import { useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { ShakeError } from '../components/ui/MotionPrimitives'
import { useAuth } from '../lib/auth'
import { destinoTrasLogin } from '../lib/destino'
import type { Rol } from '../lib/types'
import { AuthLayout } from './AuthLayout'

type Tab = 'admin' | 'inquilino'
const TABS: Tab[] = ['admin', 'inquilino']
const LABEL: Record<Rol, string> = { admin: 'Administrador', cobrador: 'Cobrador', inquilino: 'Inquilino' }

export default function Login() {
  const { login, logout } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [tab, setTab] = useState<Tab>('admin')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(true)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const emailRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)
  const tabRefs = useRef<Record<Tab, HTMLButtonElement | null>>({ admin: null, inquilino: null })

  const onTabKeyDown = (e: KeyboardEvent<HTMLButtonElement>, idx: number) => {
    const nextIdx =
      e.key === 'ArrowRight' || e.key === 'ArrowDown' ? (idx + 1) % TABS.length
        : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? (idx - 1 + TABS.length) % TABS.length
          : e.key === 'Home' ? 0
            : e.key === 'End' ? TABS.length - 1
              : -1
    if (nextIdx === -1) return
    e.preventDefault()
    const next = TABS[nextIdx]
    setTab(next)
    setError('')
    tabRefs.current[next]?.focus()
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!email.trim()) {
      setError('Ingresa tu correo electrónico')
      emailRef.current?.focus()
      return
    }
    if (!password) {
      setError('Ingresa tu contraseña')
      passwordRef.current?.focus()
      return
    }
    setError(''); setLoading(true)
    try {
      const user = await login(email.trim(), password, remember)
      const allowed: Rol[] = tab === 'admin' ? ['admin', 'cobrador'] : ['inquilino']
      if (!allowed.includes(user.rol)) {
        logout()
        setError(`Esta cuenta es de tipo "${LABEL[user.rol]}". Elegiste la pestaña "${tab === 'admin' ? 'Administrador' : 'Inquilino'}" — cámbiala para continuar.`)
        return
      }
      navigate(destinoTrasLogin(location.state), { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Credenciales incorrectas. Verifica e intenta de nuevo.')
    } finally { setLoading(false) }
  }

  return (
    <AuthLayout title="Bienvenido" subtitle="Inicia sesión para continuar">
      <form onSubmit={submit} className="space-y-4" noValidate>
        <div role="tablist" aria-label="Tipo de cuenta" className="grid grid-cols-2 rounded-xl border border-line bg-hover p-1">
          {TABS.map((t, idx) => {
            const active = tab === t
            return (
              <button
                key={t}
                ref={el => { tabRefs.current[t] = el }}
                type="button"
                role="tab"
                tabIndex={active ? 0 : -1}
                aria-selected={active}
                onKeyDown={e => onTabKeyDown(e, idx)}
                onClick={() => { setTab(t); setError('') }}
                className={clsx('relative rounded-lg py-2 text-sm font-semibold transition-colors', active ? 'text-white' : 'text-fg-2 hover:text-fg')}
              >
                {active && (
                  <motion.span
                    layoutId="login-role-tab"
                    transition={{ type: 'spring', stiffness: 460, damping: 32 }}
                    className="absolute inset-0 rounded-lg bg-primary shadow"
                  />
                )}
                <span className="relative z-10">{t === 'admin' ? 'Administrador' : 'Inquilino'}</span>
              </button>
            )
          })}
        </div>

        <Input
          ref={emailRef}
          name="email"
          label="Correo electrónico"
          hint="El correo con el que se creó tu cuenta."
          type="email"
          autoComplete="username"
          inputMode="email"
          spellCheck={false}
          enterKeyHint="next"
          required
          placeholder="correo@ejemplo.com"
          icon={<Mail />}
          value={email}
          onChange={e => { setError(''); setEmail(e.target.value) }}
        />
        <Input
          ref={passwordRef}
          name="password"
          label="Contraseña"
          hint="Distingue mayúsculas y minúsculas."
          type="password"
          autoComplete="current-password"
          enterKeyHint="done"
          required
          placeholder="••••••••"
          icon={<Lock />}
          value={password}
          onChange={e => { setError(''); setPassword(e.target.value) }}
        />

        <label className="flex cursor-pointer items-center gap-2 text-sm text-fg-2">
          <input type="checkbox" name="remember" checked={remember} onChange={e => setRemember(e.target.checked)} className="size-4 accent-[var(--primary)]" />
          Recordar sesión
        </label>

        {error && (
          <ShakeError error={error}>
            <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>
          </ShakeError>
        )}

        <Button type="submit" full loading={loading}>Iniciar sesión</Button>
        <p className="pt-2 text-center text-sm text-fg-2">
          ¿Administras propiedades? <Link to="/register" className="font-semibold text-primary hover:underline">Crea tu cuenta</Link>
          <span className="mt-1 block text-xs text-muted">¿Eres inquilino o colaborador? Usa el enlace de invitación que te envió tu administrador.</span>
        </p>
      </form>
    </AuthLayout>
  )
}
