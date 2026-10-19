import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Copy, Plus, Share2, Ticket, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Card } from '../../../components/ui/Card'
import { EmptyState } from '../../../components/ui/EmptyState'
import { Modal } from '../../../components/ui/Modal'
import { StaggerItem, StaggerList } from '../../../components/ui/MotionPrimitives'
import { Segmented } from '../../../components/ui/Segmented'
import { Select } from '../../../components/ui/Select'
import { Spinner } from '../../../components/ui/Spinner'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { fecha } from '../../../lib/format'
import { PRESETS } from '../../../lib/permisos'
import type { CodigoInvitacion, Permiso } from '../../../lib/types'
import { PermisosEditor } from './PermisosEditor'

const enlaceDe = (codigo: string) => `${window.location.origin}/register?codigo=${encodeURIComponent(codigo)}`

const estadoDe = (c: CodigoInvitacion) => c.usado ? { label: 'Usado', tone: 'neutral' as const } : c.expira_en && new Date(c.expira_en) < new Date() ? { label: 'Expirado', tone: 'danger' as const } : { label: 'Activo', tone: 'success' as const }

export function Invitaciones() {
  const qc = useQueryClient()
  const toast = useToast()
  const [gen, setGen] = useState(false)
  const [creado, setCreado] = useState<CodigoInvitacion | null>(null)
  const { data = [], isLoading } = useQuery({ queryKey: ['codigos'], queryFn: async () => (await api.getCodigos()).data })
  const revocar = useMutation({ mutationFn: (id: string) => api.revocarCodigo(id), onSuccess: () => { toast('Enlace revocado', 'success'); void qc.invalidateQueries({ queryKey: ['codigos'] }) }, onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo revocar', 'error') })

  return (
    <StaggerList className="space-y-5">
      <StaggerItem className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-fg-2">Genera un enlace de invitación: quien lo abra crea su cuenta con el rol y los permisos que elijas.</p>
        <Button icon={<Plus className="size-4" />} onClick={() => { setCreado(null); setGen(true) }}>Generar enlace</Button>
      </StaggerItem>
      {isLoading && <div className="grid place-items-center py-16"><Spinner className="size-8" /></div>}
      {!isLoading && data.length === 0 && <StaggerItem><Card><EmptyState icon={Ticket} title="Sin enlaces de invitación">Genera uno para invitar a un inquilino o colaborador.</EmptyState></Card></StaggerItem>}
      {data.length > 0 && (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
          {data.map(c => { const e = estadoDe(c); return (
            <StaggerItem key={c.id}>
              <Card className="space-y-2 p-4">
                <div className="flex items-center justify-between gap-2">
                  <p className="font-mono text-xl font-extrabold tracking-widest">{c.codigo}</p><Badge tone={e.tone}>{e.label}</Badge>
                </div>
                <p className="text-xs text-fg-2">{c.rol === 'cobrador' ? `Colaborador${c.rol_label ? ` · ${c.rol_label}` : ''}` : 'Inquilino'} · creado {fecha(c.created_at)}{c.expira_en ? ` · vence ${fecha(c.expira_en)}` : ' · sin vencimiento'}</p>
                {c.usado && c.usado_por_nombre && <p className="text-xs text-muted">Usado por {c.usado_por_nombre} {c.usado_en ? `el ${fecha(c.usado_en)}` : ''}</p>}
                <div className="flex justify-end gap-1">
                  {!c.usado && <CopyBtn texto={enlaceDe(c.codigo)} />}
                  <Button variant="ghost" className="h-8 px-3 text-danger" icon={<Trash2 className="size-4" />} onClick={() => confirm('¿Revocar este enlace? Dejará de funcionar.') && revocar.mutate(c.id)}>Revocar</Button>
                </div>
              </Card>
            </StaggerItem>
          ) })}
        </div>
      )}
      <Generar open={gen} creado={creado} onClose={() => setGen(false)} onCreated={c => { setCreado(c); void qc.invalidateQueries({ queryKey: ['codigos'] }) }} />
    </StaggerList>
  )
}

function CopyBtn({ texto, grande }: { texto: string; grande?: boolean }) {
  const [ok, setOk] = useState(false)
  const copiar = async () => { try { await navigator.clipboard.writeText(texto); setOk(true); setTimeout(() => setOk(false), 2000) } catch { /* sin portapapeles */ } }
  return <Button variant={grande ? 'primary' : 'ghost'} className={grande ? '' : 'h-8 px-3'} icon={ok ? <Check className="size-4" /> : <Copy className="size-4" />} onClick={copiar}>{ok ? 'Copiado' : 'Copiar enlace'}</Button>
}

function Generar({ open, creado, onClose, onCreated }: { open: boolean; creado: CodigoInvitacion | null; onClose: () => void; onCreated: (c: CodigoInvitacion) => void }) {
  const toast = useToast()
  const [rol, setRol] = useState<'inquilino' | 'cobrador'>('inquilino')
  const [dias, setDias] = useState<string>('7')
  const [permisos, setPermisos] = useState<Permiso[]>([...PRESETS[0].permisos]); const [preset, setPreset] = useState('cobrador')
  const crear = useMutation({
    mutationFn: () => api.crearCodigo(rol, dias === '0' ? null : Number(dias), rol === 'cobrador' ? permisos : undefined, rol === 'cobrador' ? preset : undefined, rol === 'cobrador' ? (PRESETS.find(p => p.key === preset)?.label ?? 'Personalizado') : undefined),
    onSuccess: (r) => onCreated(r.data),
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo generar el código', 'error'),
  })
  const compartir = async (url: string) => { try { await navigator.share({ title: 'Invitación a NethRent', text: 'Crea tu cuenta en NethRent con este enlace:', url }) } catch { /* cancelado */ } }

  return (
    <Modal open={open} title={creado ? 'Enlace generado' : 'Generar enlace de invitación'} onClose={onClose}>
      {creado ? (
        <div className="space-y-4 text-center">
          <p className="break-all rounded-2xl bg-hover px-4 py-5 font-mono text-sm font-semibold">{enlaceDe(creado.codigo)}</p>
          <p className="text-sm text-fg-2">{creado.expira_en ? `Vence el ${fecha(creado.expira_en)}.` : 'No vence.'} El enlace se puede usar una sola vez.</p>
          <div className="grid grid-cols-2 gap-2"><CopyBtn texto={enlaceDe(creado.codigo)} grande />{typeof navigator.share === 'function' && <Button variant="secondary" icon={<Share2 className="size-4" />} onClick={() => compartir(enlaceDe(creado.codigo))}>Compartir</Button>}</div>
        </div>
      ) : (
        <div className="space-y-5">
          <div className="space-y-1.5"><span className="block text-xs font-bold uppercase tracking-wide text-fg-2">¿Para quién?</span>
            <Segmented value={rol} onChange={setRol} options={[{ value: 'inquilino', label: 'Inquilino' }, { value: 'cobrador', label: 'Colaborador' }]} /></div>
          <Select label="Vigencia" value={dias} onChange={e => setDias(e.target.value)}><option value="1">1 día</option><option value="7">7 días</option><option value="30">30 días</option><option value="0">Sin vencimiento</option></Select>
          {rol === 'cobrador' && <PermisosEditor value={permisos} preset={preset} onChange={(p, pr) => { setPermisos(p); setPreset(pr) }} />}
          <div className="pt-2">
            <Button full loading={crear.isPending} onClick={() => crear.mutate()}>Generar enlace</Button>
          </div>
        </div>
      )}
    </Modal>
  )
}
