import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Link2, Plus, Power, Search, Shield, Trash2, UserCog } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Card } from '../../../components/ui/Card'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { Input } from '../../../components/ui/Input'
import { Modal } from '../../../components/ui/Modal'
import { StaggerItem, StaggerList } from '../../../components/ui/MotionPrimitives'
import { Select } from '../../../components/ui/Select'
import { Spinner } from '../../../components/ui/Spinner'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { useAuth } from '../../../lib/auth'
import { fecha, iniciales } from '../../../lib/format'
import { PRESETS } from '../../../lib/permisos'
import type { Permiso, User } from '../../../lib/types'
import { PermisosEditor } from './PermisosEditor'

const ROL_TONE = { admin: 'primary', cobrador: 'warning', inquilino: 'neutral' } as const
const errMsg = (e: unknown, f: string) => (e instanceof Error ? e.message : f)

export function Usuarios() {
  const { user: yo } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const [q, setQ] = useState('')
  const [nuevo, setNuevo] = useState(false)
  const [rolDe, setRolDe] = useState<User | null>(null)
  const [permisosDe, setPermisosDe] = useState<User | null>(null)
  const [vincular, setVincular] = useState<User | null>(null)
  const [borrar, setBorrar] = useState<User | null>(null)

  const { data = [], isLoading } = useQuery({ queryKey: ['usuarios'], queryFn: async () => (await api.getUsuarios()).data })
  const refrescar = () => void qc.invalidateQueries({ queryKey: ['usuarios'] })
  const filtrados = useMemo(() => { const s = q.trim().toLowerCase(); return s ? data.filter(u => u.nombre_completo.toLowerCase().includes(s) || u.email.toLowerCase().includes(s)) : data }, [data, q])

  const toggle = useMutation({ mutationFn: (id: string) => api.toggleUsuario(id), onSuccess: refrescar, onError: (e) => toast(errMsg(e, 'No se pudo cambiar el estado'), 'error') })
  const eliminar = useMutation({ mutationFn: (id: string) => api.deleteUsuario(id), onSuccess: () => { setBorrar(null); toast('Usuario eliminado', 'success'); refrescar() } })

  return (
    <StaggerList className="space-y-5">
      <StaggerItem className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-60 flex-1">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted" />
          <input value={q} onChange={e => setQ(e.target.value)} placeholder="Buscar usuario…" aria-label="Buscar usuario"
            className="h-11 w-full rounded-xl border border-line bg-card pl-10 pr-4 text-sm placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25" />
        </div>
        <Button icon={<Plus className="size-4" />} onClick={() => setNuevo(true)}>Nuevo usuario</Button>
      </StaggerItem>

      {isLoading && <div className="grid place-items-center py-16"><Spinner className="size-8" /></div>}
      {!isLoading && filtrados.length === 0 && <StaggerItem><Card><EmptyState icon={UserCog} title="Sin usuarios" /></Card></StaggerItem>}

      {filtrados.length > 0 && (
        <div key={q} className="space-y-3">
          {filtrados.map(u => {
            const soyYo = u.id === yo?.id
            return (
              <StaggerItem key={u.id}>
                <Card className={`flex flex-wrap items-center gap-x-4 gap-y-3 p-4 ${u.activo === false ? 'opacity-60' : ''}`}>
                  <span className="grid size-11 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-bold text-primary">{iniciales(u.nombre_completo)}</span>
                  <div className="min-w-48 flex-1">
                    <p className="truncate font-bold">{u.nombre_completo}{soyYo && <span className="ml-2 text-xs font-normal text-muted">(tú)</span>}</p>
                    <p className="truncate text-xs text-fg-2">{u.email}</p>
                    <p className="mt-1 flex flex-wrap items-center gap-1.5">
                      <Badge tone={ROL_TONE[u.rol]}>{u.rol === 'cobrador' && u.rol_label ? u.rol_label : u.rol}</Badge>
                      {u.activo === false && <Badge tone="danger">Desactivado</Badge>}
                      {u.ultimo_acceso && <span className="text-[11px] text-muted">Último acceso {fecha(u.ultimo_acceso)}</span>}
                    </p>
                  </div>
                  {!soyYo && (
                    <div className="flex flex-wrap gap-1">
                      {u.rol === 'cobrador' && <IconBtn label="Permisos" onClick={() => setPermisosDe(u)}><Shield className="size-4" /></IconBtn>}
                      {u.rol === 'inquilino' && <IconBtn label="Vincular a un inquilino" onClick={() => setVincular(u)}><Link2 className="size-4" /></IconBtn>}
                      <IconBtn label="Cambiar rol" onClick={() => setRolDe(u)}><UserCog className="size-4" /></IconBtn>
                      <IconBtn label={u.activo === false ? 'Activar' : 'Desactivar'} onClick={() => toggle.mutate(u.id)}><Power className="size-4" /></IconBtn>
                      <IconBtn label="Eliminar" danger onClick={() => { eliminar.reset(); setBorrar(u) }}><Trash2 className="size-4" /></IconBtn>
                    </div>
                  )}
                </Card>
              </StaggerItem>
            )
          })}
        </div>
      )}

      <NuevoUsuario open={nuevo} onClose={() => setNuevo(false)} onDone={() => { setNuevo(false); refrescar() }} />
      <CambiarRol user={rolDe} onClose={() => setRolDe(null)} onDone={(u, nuevoRol) => { setRolDe(null); refrescar(); if (nuevoRol === 'cobrador') setPermisosDe(u) }} />
      <EditarPermisos user={permisosDe} onClose={() => setPermisosDe(null)} onDone={() => { setPermisosDe(null); refrescar() }} />
      <Vincular user={vincular} onClose={() => setVincular(null)} />
      <ConfirmDialog open={!!borrar} title="Eliminar usuario" confirmLabel="Eliminar" loading={eliminar.isPending} error={eliminar.error instanceof Error ? eliminar.error.message : null}
        onClose={() => setBorrar(null)} onConfirm={() => borrar && eliminar.mutate(borrar.id)}>
        <p>¿Seguro que deseas eliminar a <b className="text-fg">{borrar?.nombre_completo}</b>? Esta acción no se puede deshacer.</p>
      </ConfirmDialog>
    </StaggerList>
  )
}

function IconBtn({ label, onClick, children, danger }: { label: string; onClick: () => void; children: React.ReactNode; danger?: boolean }) {
  return <button onClick={onClick} aria-label={label} title={label} className={`grid size-9 place-items-center rounded-lg transition hover:bg-hover ${danger ? 'text-danger' : 'text-fg-2'}`}>{children}</button>
}

function NuevoUsuario({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [f, setF] = useState({ nombre: '', email: '', password: '', rol: 'cobrador' as User['rol'] })
  const [permisos, setPermisos] = useState<Permiso[]>([...PRESETS[0].permisos]); const [preset, setPreset] = useState('cobrador')
  const [error, setError] = useState('')
  const crear = useMutation({
    mutationFn: () => api.createUsuario({ nombre_completo: f.nombre.trim(), email: f.email.trim(), password: f.password, rol: f.rol, permisos: f.rol === 'cobrador' ? permisos : undefined, rol_label: f.rol === 'cobrador' ? (PRESETS.find(p => p.key === preset)?.label ?? 'Personalizado') : undefined }),
    onSuccess: () => { toast('Usuario creado', 'success'); setF({ nombre: '', email: '', password: '', rol: 'cobrador' }); onDone() },
    onError: (e) => setError(errMsg(e, 'No se pudo crear el usuario')),
  })
  const submit = () => {
    if (!f.nombre.trim() || !f.email.includes('@')) return setError('Ingresa nombre y un correo válido')
    if (f.password.length < 8) return setError('La contraseña debe tener al menos 8 caracteres')
    setError(''); crear.mutate()
  }
  return (
    <Modal open={open} title="Nuevo usuario" onClose={onClose}>
      <div className="space-y-5">
        <Input label="Nombre completo *" value={f.nombre} onChange={e => { setError(''); setF({ ...f, nombre: e.target.value }) }} />
        <Input label="Correo *" type="email" value={f.email} onChange={e => { setError(''); setF({ ...f, email: e.target.value }) }} />
        <Input label="Contraseña *" type="password" autoComplete="new-password" placeholder="Mínimo 8 caracteres" value={f.password} onChange={e => { setError(''); setF({ ...f, password: e.target.value }) }} />
        <Select label="Rol" value={f.rol} onChange={e => setF({ ...f, rol: e.target.value as User['rol'] })}><option value="cobrador">Colaborador (permisos a medida)</option><option value="inquilino">Inquilino</option><option value="admin">Administrador</option></Select>
        {f.rol === 'cobrador' && <PermisosEditor value={permisos} preset={preset} onChange={(p, pr) => { setPermisos(p); setPreset(pr) }} />}
        {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
        <div className="pt-2">
          <Button full loading={crear.isPending} onClick={submit}>Crear usuario</Button>
        </div>
      </div>
    </Modal>
  )
}

function CambiarRol({ user, onClose, onDone }: { user: User | null; onClose: () => void; onDone: (u: User, rol: string) => void }) {
  const toast = useToast()
  const [rol, setRol] = useState<User['rol']>('cobrador')
  const [seen, setSeen] = useState<string | null>(null)
  if (user && seen !== user.id) { setSeen(user.id); setRol(user.rol) }
  const cambiar = useMutation({
    mutationFn: () => api.cambiarRol(user!.id, rol),
    onSuccess: () => { toast('Rol actualizado', 'success'); onDone(user!, rol) },
    onError: (e) => toast(errMsg(e, 'No se pudo cambiar el rol'), 'error'),
  })
  return (
    <Modal open={!!user} title={`Cambiar rol de ${user?.nombre_completo ?? ''}`} onClose={onClose}>
      <div className="space-y-5">
        <Select label="Rol" value={rol} onChange={e => setRol(e.target.value as User['rol'])}><option value="admin">Administrador</option><option value="cobrador">Colaborador</option><option value="inquilino">Inquilino</option></Select>
        <div className="pt-2">
          <Button full loading={cambiar.isPending} disabled={rol === user?.rol} onClick={() => cambiar.mutate()}>Guardar rol</Button>
        </div>
      </div>
    </Modal>
  )
}

function EditarPermisos({ user, onClose, onDone }: { user: User | null; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [permisos, setPermisos] = useState<Permiso[]>([]); const [preset, setPreset] = useState('custom')
  const [seen, setSeen] = useState<string | null>(null)
  if (user && seen !== user.id) {
    setSeen(user.id)
    const p = user.permisos ?? []
    setPermisos(p)
    setPreset(PRESETS.find(x => x.label === user.rol_label && x.permisos.length === p.length)?.key ?? 'custom')
  }
  const guardar = useMutation({
    mutationFn: () => api.actualizarPermisos(user!.id, permisos, PRESETS.find(p => p.key === preset)?.label ?? 'Personalizado'),
    onSuccess: () => { toast('Permisos actualizados', 'success'); onDone() },
    onError: (e) => toast(errMsg(e, 'No se pudo guardar'), 'error'),
  })
  return (
    <Modal open={!!user} title={`Permisos de ${user?.nombre_completo ?? ''}`} onClose={onClose}>
      <div className="space-y-5">
        <PermisosEditor value={permisos} preset={preset} onChange={(p, pr) => { setPermisos(p); setPreset(pr) }} />
        <div className="pt-2">
          <Button full loading={guardar.isPending} icon={<KeyRound className="size-4" />} onClick={() => guardar.mutate()}>Guardar permisos</Button>
        </div>
      </div>
    </Modal>
  )
}

function Vincular({ user, onClose }: { user: User | null; onClose: () => void }) {
  const toast = useToast()
  const qc = useQueryClient()
  const lista = useQuery({ queryKey: ['inquilinos', 'activo'], queryFn: async () => (await api.getInquilinos({ estado: 'activo' })).data, enabled: !!user })
  const vincular = useMutation({
    mutationFn: (id: string) => api.vincularUsuario(id, user!.id),
    onSuccess: () => { toast('Cuenta vinculada al inquilino', 'success'); void qc.invalidateQueries({ queryKey: ['inquilinos'] }); onClose() },
    onError: (e) => toast(errMsg(e, 'No se pudo vincular'), 'error'),
  })
  return (
    <Modal open={!!user} title={`Vincular a ${user?.nombre_completo ?? ''}`} onClose={onClose}>
      <p className="mb-3 text-sm text-fg-2">Elige el inquilino cuya información verá esta cuenta.</p>
      {lista.isLoading ? <Spinner /> : (
        <ul className="divide-y divide-line">
          {lista.data?.map(i => (
            <li key={i.id}>
              <button onClick={() => vincular.mutate(i.id)} disabled={vincular.isPending} className="flex w-full items-center justify-between gap-3 py-3 text-left text-sm hover:text-primary">
                <span><b>{i.nombre_completo}</b><span className="block text-xs text-muted">Depto {i.depto_numero}{i.usuario ? ` · vinculado a ${i.usuario.email}` : ''}</span></span><Link2 className="size-4" />
              </button>
            </li>
          ))}
          {lista.data?.length === 0 && <li className="py-6 text-center text-sm text-muted">No hay inquilinos activos</li>}
        </ul>
      )}
    </Modal>
  )
}
