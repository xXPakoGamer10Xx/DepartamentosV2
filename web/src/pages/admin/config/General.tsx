import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell, KeyRound, Landmark, UserRound } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../../../components/ui/Button'
import { Card } from '../../../components/ui/Card'
import { Input } from '../../../components/ui/Input'
import { Modal } from '../../../components/ui/Modal'
import { StaggerItem, StaggerList } from '../../../components/ui/MotionPrimitives'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { useAuth } from '../../../lib/auth'

const RECORD_OPCIONES = [7, 5, 3, 2, 1, 0]
const parseRecord = (v?: string): number[] => {
  if (v == null) return [1, 0]
  if (v.trim() === '') return []
  return [...new Set(v.split(',').map(s => parseInt(s.trim(), 10)).filter(n => Number.isInteger(n) && n >= 0 && n <= 30))].sort((a, b) => b - a)
}
const errMsg = (e: unknown, f: string) => (e instanceof Error ? e.message : f)

export function General() {
  const { user, refresh } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const cfg = useQuery({ queryKey: ['config'], queryFn: async () => (await api.getConfig()).data })
  const c = cfg.data ?? {}

  const [nombre, setNombre] = useState(user?.nombre_completo ?? '')
  const [arr, setArr] = useState({ nombre: '', direccion: '', rfc: '', telefono: '', url: '', gracia: '' })
  const [record, setRecord] = useState<number[]>([1, 0])
  const [pwd, setPwd] = useState(false)

  // Carga inicial de los campos cuando llega la configuración.
  useEffect(() => {
    if (!cfg.data) return
    setArr({ nombre: c.arrendador_nombre ?? '', direccion: c.arrendador_direccion ?? '', rfc: c.arrendador_rfc ?? '', telefono: c.arrendador_telefono ?? '', url: c.app_url ?? '', gracia: c.dias_gracia_retraso ?? '0' })
    setRecord(parseRecord(c.recordatorios_renta_dias))
  }, [cfg.data]) // eslint-disable-line react-hooks/exhaustive-deps

  const guardar = useMutation({
    mutationFn: (d: Record<string, string>) => api.updateConfig(d),
    onSuccess: () => { toast('Configuración guardada', 'success'); void qc.invalidateQueries({ queryKey: ['config'] }) },
    onError: (e) => toast(errMsg(e, 'No se pudo guardar'), 'error'),
  })
  const perfil = useMutation({
    mutationFn: () => api.updatePerfil({ nombre_completo: nombre.trim() }),
    onSuccess: async () => { await refresh(); toast('Perfil actualizado', 'success') },
    onError: (e) => toast(errMsg(e, 'No se pudo actualizar'), 'error'),
  })

  const usaApp = c.usa_qr_inquilinos !== 'false'

  return (
    <StaggerList className="grid grid-cols-1 items-start gap-5 lg:grid-cols-2">
      <div className="space-y-5">
      <Sec titulo="Tu perfil" desc="Los datos con los que inicias sesión." icon={<UserRound className="size-4" />}
        acciones={<><Button variant="secondary" icon={<KeyRound className="size-4" />} onClick={() => setPwd(true)}>Cambiar contraseña</Button>
          <Button loading={perfil.isPending} disabled={!nombre.trim() || nombre.trim() === user?.nombre_completo} onClick={() => perfil.mutate()}>Guardar perfil</Button></>}>
        <div className="grid grid-cols-1 gap-4 2xl:grid-cols-2">
          <Input label="Nombre completo" hint="Así apareces en el sistema." value={nombre} onChange={e => setNombre(e.target.value)} />
          <Input label="Correo" hint="Tu usuario para iniciar sesión (no se puede cambiar)." value={user?.email ?? ''} disabled />
        </div>
      </Sec>

      <Sec titulo="Datos del arrendador" desc="Aparecen en recibos y en tus contratos." icon={<UserRound className="size-4" />}
        acciones={<Button loading={guardar.isPending} onClick={() => guardar.mutate({ arrendador_nombre: arr.nombre.trim(), arrendador_direccion: arr.direccion.trim(), arrendador_rfc: arr.rfc.trim().toUpperCase(), arrendador_telefono: arr.telefono.trim() })}>Guardar datos</Button>}>
        <div className="grid grid-cols-1 gap-4 2xl:grid-cols-2">
          <Input label="Nombre del arrendador" hint="Persona o empresa que recibe las rentas." maxLength={100} value={arr.nombre} onChange={e => setArr({ ...arr, nombre: e.target.value })} />
          <Input label="Dirección de la propiedad" hint="Domicilio principal del inmueble." maxLength={200} value={arr.direccion} onChange={e => setArr({ ...arr, direccion: e.target.value })} />
          <Input label="RFC" hint="Opcional. Algunos contratos lo piden." maxLength={13} value={arr.rfc} onChange={e => setArr({ ...arr, rfc: e.target.value.toUpperCase() })} />
          <Input label="Teléfono del arrendador" hint="Opcional. Para el contrato." inputMode="tel" maxLength={20} value={arr.telefono} onChange={e => setArr({ ...arr, telefono: e.target.value })} />
        </div>
      </Sec>

      </div>

      <div className="space-y-5">
      <Sec titulo="Cobranza" desc="Quién usa la plataforma y cómo se marcan los atrasos." icon={<Landmark className="size-4" />}
        acciones={<><Link to="/cuentas"><Button variant="secondary">Cuentas bancarias</Button></Link>
          <Button loading={guardar.isPending} onClick={() => guardar.mutate({ app_url: arr.url.trim(), dias_gracia_retraso: arr.gracia || '0' })}>Guardar</Button></>}>
        <fieldset className="space-y-2">
          <legend className="text-xs font-bold uppercase tracking-wide text-fg-2">Modo de uso</legend>
          <p className="text-xs text-muted">Elige si tus inquilinos entrarán a la plataforma. Se aplica al instante.</p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2" role="radiogroup" aria-label="Modo de uso">
            {([
              { v: true, t: 'Con inquilinos', d: 'Tus inquilinos entran a la app: ven su renta, pagan con QR, envían comprobantes y abren tickets.' },
              { v: false, t: 'Uso interno', d: 'Solo tú y tu equipo cobran y llevan el control. Se ocultan QR, Comprobantes y Tickets.' },
            ] as const).map(o => (
              <button key={o.t} type="button" role="radio" aria-checked={usaApp === o.v} disabled={guardar.isPending}
                onClick={() => usaApp !== o.v && guardar.mutate({ usa_qr_inquilinos: o.v ? 'true' : 'false' })}
                className={`rounded-xl border-2 p-4 text-left transition ${usaApp === o.v ? 'border-primary bg-primary-soft' : 'border-line hover:bg-hover'}`}>
                <span className="flex items-center gap-2 text-sm font-bold">
                  <span className={`grid size-4 place-items-center rounded-full border-2 ${usaApp === o.v ? 'border-primary' : 'border-line'}`}>{usaApp === o.v && <span className="size-2 rounded-full bg-primary" />}</span>
                  {o.t}
                </span>
                <span className="mt-1 block text-xs text-fg-2">{o.d}</span>
              </button>
            ))}
          </div>
        </fieldset>
        <div className="grid grid-cols-1 gap-4 2xl:grid-cols-2">
          {usaApp && <Input label="URL pública de la app" hint="Dirección que verán tus inquilinos (se usa en los QR y avisos)." placeholder="https://tuapp.com" value={arr.url} onChange={e => setArr({ ...arr, url: e.target.value })} />}
          <Input label="Días de gracia" hint="Días después del vencimiento antes de marcar atraso." type="number" min={0} max={90} value={arr.gracia} onChange={e => setArr({ ...arr, gracia: e.target.value.slice(0, 2) })} />
        </div>
      </Sec>

      <Sec titulo="Recordatorios de renta" desc="Avisos al inquilino y al equipo, los días antes del pago." icon={<Bell className="size-4" />}
        acciones={<Button loading={guardar.isPending} onClick={() => guardar.mutate({ recordatorios_renta_dias: record.join(',') })}>Guardar recordatorios</Button>}>
        <div className="flex flex-wrap gap-2">
          {RECORD_OPCIONES.map(d => {
            const on = record.includes(d)
            return <button key={d} type="button" onClick={() => setRecord(r => (on ? r.filter(x => x !== d) : [...r, d].sort((a, b) => b - a)))}
              className={`rounded-full border px-4 py-1.5 text-sm font-semibold transition ${on ? 'border-primary bg-primary text-white' : 'border-line bg-card text-fg-2 hover:bg-hover'}`}>{d === 0 ? 'El día' : `${d} día${d > 1 ? 's' : ''} antes`}</button>
          })}
        </div>
      </Sec>

      </div>

      <PasswordModal open={pwd} onClose={() => setPwd(false)} />
    </StaggerList>
  )
}

function Sec({ titulo, desc, icon, children, acciones }: { titulo: string; desc?: string; icon: ReactNode; children?: ReactNode; acciones?: ReactNode }) {
  return (
    <StaggerItem>
      <Card className="space-y-5 transition duration-200 hover:border-line-strong hover:shadow-md">
        <div>
          <h2 className="flex items-center gap-2 text-sm font-bold">{icon}{titulo}</h2>
          {desc && <p className="mt-1 text-sm text-fg-2">{desc}</p>}
        </div>
        {children}
        {acciones && <div className="flex flex-wrap justify-end gap-2 border-t border-line pt-4">{acciones}</div>}
      </Card>
    </StaggerItem>
  )
}

function PasswordModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const toast = useToast()
  const [a, setA] = useState(''); const [n, setN] = useState(''); const [n2, setN2] = useState(''); const [error, setError] = useState('')
  const cambiar = useMutation({
    mutationFn: () => api.changePassword(a, n),
    onSuccess: () => { toast('Contraseña actualizada', 'success'); setA(''); setN(''); setN2(''); onClose() },
    onError: (e) => setError(errMsg(e, 'No se pudo cambiar la contraseña')),
  })
  const submit = () => {
    if (!a) return setError('Ingresa tu contraseña actual')
    if (n.length < 8) return setError('La nueva contraseña debe tener al menos 8 caracteres')
    if (n !== n2) return setError('Las contraseñas no coinciden')
    setError(''); cambiar.mutate()
  }
  return (
    <Modal open={open} title="Cambiar contraseña" onClose={onClose}>
      <div className="space-y-5">
        <Input label="Contraseña actual" type="password" autoComplete="current-password" value={a} onChange={e => { setError(''); setA(e.target.value) }} />
        <Input label="Nueva contraseña" type="password" autoComplete="new-password" placeholder="Mínimo 8 caracteres" value={n} onChange={e => { setError(''); setN(e.target.value) }} />
        <Input label="Confirmar nueva contraseña" type="password" autoComplete="new-password" value={n2} onChange={e => { setError(''); setN2(e.target.value) }} />
        {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
        <div className="pt-2">
          <Button full loading={cambiar.isPending} onClick={submit}>Cambiar contraseña</Button>
        </div>
      </div>
    </Modal>
  )
}
