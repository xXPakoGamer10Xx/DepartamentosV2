import clsx from 'clsx'
import { Lock, Mail } from 'lucide-react'
import { motion } from 'motion/react'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { ShakeError } from '../components/ui/MotionPrimitives'
import { useAuth } from '../lib/auth'
import type { Rol } from '../lib/types'
import { AuthLayout } from './AuthLayout'

type Tab = 'admin' | 'inquilino'
const LABEL: Record<Rol, string> = { admin: 'Administrador', cobrador: 'Cobrador', inquilino: 'Inquilino' }

export default function Login() {
  const { login, logout } = useAuth()
  const navigate = useNavigate()
  const [tab, setTab] = useState<Tab>('admin')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [remember, setRemember] = useState(true)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!email.trim() || !password) return setError('Ingresa tu correo y contraseña')
    setError(''); setLoading(true)
    try {
      const user = await login(email.trim(), password, remember)
      const allowed: Rol[] = tab === 'admin' ? ['admin', 'cobrador'] : ['inquilino']
      if (!allowed.includes(user.rol)) {
        logout()
        setError(`Esta cuenta es de tipo "${LABEL[user.rol]}". Elegiste la pestaña "${tab === 'admin' ? 'Administrador' : 'Inquilino'}" — cámbiala para continuar.`)
        return
      }
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Credenciales incorrectas. Verifica e intenta de nuevo.')
    } finally { setLoading(false) }
  }

  return (
    <AuthLayout title="Bienvenido" subtitle="Inicia sesión para continuar">
      <form onSubmit={submit} className="space-y-4" noValidate>
        <div role="tablist" className="grid grid-cols-2 rounded-xl border border-line bg-hover p-1">
          {(['admin', 'inquilino'] as Tab[]).map(t => {
            const active = tab === t
            return (
              <button key={t} type="button" role="tab" aria-selected={active} onClick={() => { setTab(t); setError('') }}
                className={clsx('relative rounded-lg py-2 text-sm font-semibold transition-colors', active ? 'text-white' : 'text-fg-2 hover:text-fg')}>
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

        <Input label="Correo electrónico" hint="El correo con el que se creó tu cuenta." type="email" autoComplete="email" inputMode="email" placeholder="correo@ejemplo.com"
          icon={<Mail />} value={email} onChange={e => { setError(''); setEmail(e.target.value) }} />
        <Input label="Contraseña" hint="Distingue mayúsculas y minúsculas." type="password" autoComplete="current-password" placeholder="••••••••"
          icon={<Lock />} value={password} onChange={e => { setError(''); setPassword(e.target.value) }} />

        <label className="flex cursor-pointer items-center gap-2 text-sm text-fg-2">
          <input type="checkbox" checked={remember} onChange={e => setRemember(e.target.checked)} className="size-4 accent-[var(--primary)]" />
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
