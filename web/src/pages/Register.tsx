import { Lock, Mail, User } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Button } from '../components/ui/Button'
import { Input } from '../components/ui/Input'
import { useAuth } from '../lib/auth'
import { AuthLayout } from './AuthLayout'

export default function Register() {
  const { register } = useAuth()
  const navigate = useNavigate()
  // /register: cualquiera crea su cuenta de administrador (su propio espacio).
  // /register?codigo=XXXX: enlace de invitación del administrador (inquilino o colaborador, define rol y permisos).
  const codigoUrl = (useSearchParams()[0].get('codigo') ?? '').trim()
  const invitado = codigoUrl !== ''
  const rol = invitado ? 'inquilino' : 'admin'
  const [f, setF] = useState({ nombre: '', email: '', password: '', confirm: '', code: '' })
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement>) => { setError(''); setF(s => ({ ...s, [k]: e.target.value })) }

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!f.nombre.trim()) return setError('Ingresa tu nombre completo')
    if (!f.email.includes('@')) return setError('Ingresa un correo electrónico válido')
    if (f.password.length < 8) return setError('La contraseña debe tener al menos 8 caracteres')
    if (f.password !== f.confirm) return setError('Las contraseñas no coinciden')
    setLoading(true)
    try {
      // El código puede resolver a un rol distinto (p. ej. colaborador): se usa el que devuelve el servidor.
      await register({ email: f.email.trim(), password: f.password, nombre_completo: f.nombre.trim(), rol, invite_code: invitado ? codigoUrl : undefined })
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo crear la cuenta. Intenta de nuevo.')
    } finally { setLoading(false) }
  }

  return (
    <AuthLayout title="Crear cuenta" subtitle={invitado ? 'Completa tus datos para activar tu acceso' : 'Administra tus propiedades, inquilinos y cobranza'}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Nombre completo" hint="Así aparecerás en el sistema y en tus recibos o contratos." placeholder="Ej. Juan Pérez García"
          icon={<User />} autoComplete="name" value={f.nombre} onChange={set('nombre')} />
        <Input label="Correo electrónico" hint="Con este correo iniciarás sesión. Debe ser uno que uses, aquí no se envía verificación." placeholder="correo@ejemplo.com"
          type="email" icon={<Mail />} autoComplete="email" inputMode="email" value={f.email} onChange={set('email')} />
        <Input label="Contraseña" hint="Mínimo 8 caracteres. Combina letras y números para que sea segura." placeholder="Mínimo 8 caracteres"
          type="password" icon={<Lock />} autoComplete="new-password" value={f.password} onChange={set('password')} />
        <Input label="Confirmar contraseña" hint="Escríbela de nuevo para evitar errores de dedo." placeholder="Repite tu contraseña"
          type="password" icon={<Lock />} autoComplete="new-password" value={f.confirm} onChange={set('confirm')} />
        {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
        <Button type="submit" full loading={loading}>Crear cuenta</Button>
        <p className="pt-2 text-center text-sm text-fg-2">
          ¿Ya tienes cuenta? <Link to="/login" className="font-semibold text-primary hover:underline">Iniciar sesión</Link>
        </p>
      </form>
    </AuthLayout>
  )
}
