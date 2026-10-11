import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, CalendarDays, CreditCard, Download, Eye, FilePlus2, FileText, History, Package, Pencil, Phone, ShieldCheck, UserMinus, Wallet } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { Spinner } from '../../components/ui/Spinner'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { abrirPdf, guardarBlob } from '../../lib/file'
import { useAuth } from '../../lib/auth'
import { fecha, fechaHora, iniciales, money, num } from '../../lib/format'
import { GenerarContratoModal } from './contratos/GenerarContratoModal'
import { estadoChip, vencimiento } from './Inquilinos'

const METODO = { efectivo: 'Efectivo', transferencia: 'Transferencia bancaria', ambos: 'Efectivo o transferencia' } as const
const ACCION = { crear: 'Alta', editar: 'Edición', eliminar: 'Baja' } as Record<string, string>

export default function InquilinoDetalle() {
  const { id = '' } = useParams()
  const { can } = useAuth()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()
  const [baja, setBaja] = useState(false)
  const [generar, setGenerar] = useState(false)
  const puedeEditar = can('inquilinos.editar')

  const { data: inq, isLoading, isError } = useQuery({ queryKey: ['inquilino', id], queryFn: async () => (await api.getInquilino(id)).data })
  const contratos = useQuery({ queryKey: ['contratos-historial', id], queryFn: async () => (await api.getHistorialContratos(id)).data, enabled: can('contratos') && !!inq })
  const bajar = useMutation({
    mutationFn: async ({ cid, formato, ver }: { cid: string; formato: 'pdf' | 'docx'; ver?: boolean }) => {
      const blob = await api.descargarContrato(cid, formato)
      if (ver) abrirPdf(blob, `Contrato – ${inq?.nombre_completo ?? ''}`)
      else guardarBlob(blob, `contrato_${inq?.depto_numero}_${(inq?.nombre_completo ?? '').replace(/\s+/g, '_')}.${formato}`)
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo descargar', 'error'),
  })
  const saldo = useQuery({ queryKey: ['saldo', id], queryFn: async () => (await api.getSaldoInquilino(id)).data, enabled: can('pagos') && !!inq })

  const darDeBaja = useMutation({
    mutationFn: () => api.deleteInquilino(id),
    onSuccess: () => {
      toast('Inquilino dado de baja', 'success')
      void qc.invalidateQueries({ queryKey: ['inquilinos'] })
      void qc.invalidateQueries({ queryKey: ['departamentos'] })
      navigate('/inquilinos', { replace: true })
    },
  })

  if (isLoading) return <div className="grid place-items-center py-24"><Spinner className="size-8" /></div>
  if (isError || !inq) return <Card><EmptyState icon={FileText} title="Inquilino no encontrado"><Link to="/inquilinos" className="font-semibold text-primary">Volver a la lista</Link></EmptyState></Card>

  const chip = estadoChip(inq)
  const venc = vencimiento(inq.fecha_termino)
  const activo = inq.estado === 'activo'
  const deposito = num(inq.deposito)

  return (
    <div className="space-y-6">
      <PageHeader title={inq.nombre_completo} back="/inquilinos"
        subtitle={<Link to={`/departamentos/${inq.depto_numero}`} className="font-semibold text-primary hover:underline">Departamento {inq.depto_numero}</Link>}
        actions={<>
          <Badge tone={chip.tone}>{chip.label}</Badge>
          {can('contratos') && activo && <Button variant={contratos.data?.length ? 'secondary' : 'primary'} icon={<FilePlus2 className="size-4" />} className="h-10 px-4" onClick={() => setGenerar(true)}>{contratos.data?.length ? 'Regenerar contrato' : 'Generar contrato'}</Button>}
          {puedeEditar && activo && <Link to={`/inquilinos/${inq.id}/editar`}><Button variant="secondary" icon={<Pencil className="size-4" />} className="h-10 px-4">Editar</Button></Link>}
          {puedeEditar && activo && <Button variant="danger" icon={<UserMinus className="size-4" />} className="h-10 px-4" onClick={() => { darDeBaja.reset(); setBaja(true) }}>Dar de baja</Button>}
          {puedeEditar && !activo && <Link to={`/inquilinos/nuevo?desde=${inq.id}`}><Button className="h-10 px-4">Nuevo contrato</Button></Link>}
        </>} />

      <Card className="flex items-center gap-4">
        <span className="grid size-16 shrink-0 place-items-center rounded-2xl bg-primary text-xl font-extrabold text-white shadow-lg shadow-primary/30">{iniciales(inq.nombre_completo)}</span>
        <div className="grid flex-1 gap-x-8 gap-y-2 sm:grid-cols-3">
          <Dato label="Renta mensual" value={money(inq.renta)} strong />
          <Dato label="Fecha de pago" value={inq.fecha_pago || '—'} />
          <Dato label="Vigencia" value={venc ? venc.label : '—'} tone={venc?.tone === 'warning' ? 'text-warning' : venc?.expired ? 'text-danger' : undefined} />
        </div>
      </Card>

      {saldo.data && (
        <Card className={saldo.data.deuda_total > 0 ? 'border-danger/30 bg-danger-soft' : 'border-success/30 bg-success-soft'}>
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              <CreditCard className={`size-6 ${saldo.data.deuda_total > 0 ? 'text-danger' : 'text-success'}`} />
              <div><p className="text-sm font-bold">{saldo.data.deuda_total > 0 ? 'Saldo pendiente' : 'Al corriente'}</p><p className="text-xs text-fg-2">Desde el inicio del contrato</p></div>
            </div>
            <p className={`text-2xl font-extrabold ${saldo.data.deuda_total > 0 ? 'text-danger' : 'text-success'}`}>{money(saldo.data.deuda_total)}</p>
          </div>
        </Card>
      )}

      <div className="grid gap-5 lg:grid-cols-2">
        <Seccion titulo="Contrato" icon={<CalendarDays className="size-4" />}>
          <Fila label="Inicio" value={fecha(inq.fecha_inicio)} />
          <Fila label="Término" value={fecha(inq.fecha_termino)} />
          <Fila label="Renta en letra" value={inq.renta_letra || '—'} />
          <Fila label="Estado" value={inq.estado} />
          {can('contratos') && (
            <div className="space-y-2 border-t border-line pt-3">
              {contratos.data?.length ? contratos.data.slice(0, 3).map(k => (
                <div key={k.id} className="flex items-center justify-between gap-3 text-sm">
                  <span className="min-w-0"><b>Versión {k.version}</b><span className="block truncate text-xs text-muted">{k.plantilla_nombre} · {fechaHora(k.fecha_generacion)}</span></span>
                  <span className="flex shrink-0 gap-1">
                    <button onClick={() => bajar.mutate({ cid: k.id, formato: 'pdf', ver: true })} className="grid size-8 place-items-center rounded-lg text-fg-2 hover:bg-hover" aria-label="Ver PDF" title="Ver PDF"><Eye className="size-4" /></button>
                    <button onClick={() => bajar.mutate({ cid: k.id, formato: 'docx' })} className="grid size-8 place-items-center rounded-lg text-fg-2 hover:bg-hover" aria-label="Descargar Word" title="Descargar Word"><Download className="size-4" /></button>
                  </span>
                </div>
              )) : <p className="text-sm text-muted">Aún no se ha generado el contrato.</p>}
            </div>
          )}
        </Seccion>

        <Seccion titulo="Pagos y depósito" icon={<Wallet className="size-4" />}>
          <Fila label="Método de pago" value={inq.metodo_pago ? METODO[inq.metodo_pago] : '—'} />
          <Fila label="Depósito" value={deposito > 0 ? money(deposito) : 'Sin depósito'} />
          {deposito > 0 && <Fila label="Modalidad" value={inq.deposito_tipo === 'quincenas' ? 'Dos quincenas' : inq.deposito_tipo === 'personalizado' ? 'Pagos mensuales' : 'Al inicio del contrato'} />}
          {!!inq.deposito_fechas?.length && <Fila label="Fechas" value={inq.deposito_fechas.map(f => fecha(f)).join(' · ')} />}
        </Seccion>

        <Seccion titulo="Contacto" icon={<Phone className="size-4" />}>
          <Fila label="Teléfono" value={inq.tel_arrendatario || '—'} />
          <Fila label="Cuenta de acceso" value={inq.usuario ? `${inq.usuario.nombre} (${inq.usuario.email})` : 'Sin vincular'} />
        </Seccion>

        <Seccion titulo="Fiador" icon={<ShieldCheck className="size-4" />}>
          <Fila label="Nombre" value={inq.fiador_nombre || 'Sin fiador'} />
          {inq.fiador_nombre && <Fila label="Teléfono" value={inq.fiador_telefono || '—'} />}
        </Seccion>

        {inq.observaciones && (
          <Seccion titulo="Observaciones" icon={<FileText className="size-4" />} className="lg:col-span-2"><p className="whitespace-pre-wrap text-sm">{inq.observaciones}</p></Seccion>
        )}

        <Seccion titulo="Inventario" icon={<Package className="size-4" />}>
          {inq.inventario?.length ? <ul className="flex flex-wrap gap-2">{inq.inventario.map((x, k) => <li key={k} className="rounded-lg bg-hover px-3 py-1 text-sm">{x}</li>)}</ul> : <p className="text-sm text-muted">Sin inventario registrado</p>}
        </Seccion>

        <Seccion titulo="Departamento" icon={<Building2 className="size-4" />}>
          <Link to={`/departamentos/${inq.depto_numero}`} className="font-semibold text-primary hover:underline">Ver Departamento {inq.depto_numero} →</Link>
        </Seccion>

        <Seccion titulo="Historial de cambios" icon={<History className="size-4" />} className="lg:col-span-2">
          {inq.historial?.length ? (
            <ul className="divide-y divide-line">
              {inq.historial.map(h => (
                <li key={h.id} className="flex items-center justify-between gap-3 py-2.5 text-sm">
                  <span><b>{ACCION[h.accion] ?? h.accion}</b>{h.usuario && <span className="text-fg-2"> · {h.usuario}</span>}</span>
                  <span className="text-xs text-muted">{fechaHora(h.fecha)}</span>
                </li>
              ))}
            </ul>
          ) : <p className="text-sm text-muted">Sin cambios registrados</p>}
        </Seccion>
      </div>

      <GenerarContratoModal inquilinoId={generar ? id : null} onClose={() => setGenerar(false)} />

      <ConfirmDialog open={baja} title="Dar de baja" confirmLabel="Dar de baja" loading={darDeBaja.isPending}
        error={darDeBaja.error instanceof Error ? darDeBaja.error.message : null}
        onClose={() => setBaja(false)} onConfirm={() => darDeBaja.mutate()}>
        <p>¿Dar de baja a <b className="text-fg">{inq.nombre_completo}</b>? El Depto {inq.depto_numero} quedará disponible y sus datos se conservarán en el historial.</p>
      </ConfirmDialog>
    </div>
  )
}

function Seccion({ titulo, icon, children, className = '' }: { titulo: string; icon: ReactNode; children: ReactNode; className?: string }) {
  return (
    <Card className={className}>
      <h2 className="mb-3 flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2">{icon}{titulo}</h2>
      <div className="space-y-2.5">{children}</div>
    </Card>
  )
}
const Fila = ({ label, value }: { label: string; value: string }) => (
  <div className="flex justify-between gap-4 text-sm"><span className="text-fg-2">{label}</span><span className="text-right font-semibold">{value}</span></div>
)
const Dato = ({ label, value, strong, tone }: { label: string; value: string; strong?: boolean; tone?: string }) => (
  <div><p className="text-xs font-semibold text-fg-2">{label}</p><p className={`${strong ? 'text-xl font-extrabold' : 'font-bold'} ${tone ?? ''}`}>{value}</p></div>
)
