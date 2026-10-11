import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AlertTriangle, Ban, Banknote, CalendarClock, CheckCircle2, ChevronDown, FileImage, HandCoins, History, Pencil, Plus, Printer,
  ShieldAlert, Trash2, Undo2, Wallet, XCircle,
} from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EmptyState } from '../../components/ui/EmptyState'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { PageHeader } from '../../components/ui/PageHeader'
import { ProgressBar } from '../../components/ui/ProgressBar'
import { Select } from '../../components/ui/Select'
import { Spinner } from '../../components/ui/Spinner'
import { Textarea } from '../../components/ui/Textarea'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { detectBankInfo, getBankBrand } from '../../lib/bank'
import { hoyISO, puedeCancelar } from '../../lib/cobranza'
import { fecha, fechaHora, money, num, periodoLabel } from '../../lib/format'
import { buildReciboHtml, imprimirHtml } from '../../lib/recibo'
import { useSSE } from '../../lib/sse'
import type { Abono } from '../../lib/types'

const KEYS = ['estado-pago', 'abonos', 'deposito-saldo', 'deposito-abonos', 'promesas', 'saldo', 'historial-pagos-admin', 'estados-actuales', 'saldos', 'resumen-deuda', 'cuotas']
const errMsg = (e: unknown, f: string) => (e instanceof Error ? e.message : f)

export default function PagoDetalle() {
  const { id = '' } = useParams()
  const { user, can } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const puedeMarcar = can('pagos.marcar')
  const esAdmin = user?.rol === 'admin'

  const [abonoModal, setAbonoModal] = useState<{ tipo: 'renta' | 'deposito'; abono?: Abono } | null>(null)
  const [rechazar, setRechazar] = useState(false)
  const [cuotaModal, setCuotaModal] = useState(false)
  const [choice, setChoice] = useState(false)
  const [cancelar, setCancelar] = useState(false)
  const [verComprobante, setVerComprobante] = useState(false)
  const [histAbierto, setHistAbierto] = useState(false)

  const inqQ = useQuery({ queryKey: ['inquilino', id], queryFn: async () => (await api.getInquilino(id)).data })
  const inq = inqQ.data
  const cfgQ = useQuery({ queryKey: ['config', inq?.depto_numero], queryFn: async () => (await api.getConfig(inq!.depto_numero)).data, enabled: !!inq })
  const estadoQ = useQuery({ queryKey: ['estado-pago', id], queryFn: () => api.getEstadoPago(id), refetchInterval: 15_000 })
  const pago = estadoQ.data?.data ?? null
  const abonosQ = useQuery({ queryKey: ['abonos', pago?.id], queryFn: async () => (await api.getAbonosPago(pago!.id)).data, enabled: !!pago?.id })
  const depSaldoQ = useQuery({ queryKey: ['deposito-saldo', id], queryFn: async () => (await api.getSaldoDeposito(id)).data })
  const depAbonosQ = useQuery({ queryKey: ['deposito-abonos', id], queryFn: async () => (await api.getAbonosDeposito(id)).data })
  const promesasQ = useQuery({ queryKey: ['promesas', id], queryFn: async () => (await api.getPromesas(id)).data })
  const saldoQ = useQuery({ queryKey: ['saldo', id], queryFn: async () => (await api.getSaldoInquilino(id)).data })
  const histQ = useQuery({ queryKey: ['historial-pagos-admin', id], queryFn: async () => (await api.getHistorialPagos(id)).data, enabled: histAbierto })
  const estadosActuales = useQuery({ queryKey: ['estados-actuales'], queryFn: async () => (await api.getEstadosActuales()).data })

  const refresh = () => { for (const k of KEYS) void qc.invalidateQueries({ queryKey: [k] }) }
  useSSE('comprobante_subido', refresh)
  useSSE('payment_confirmed', refresh)

  const run = <T,>(fn: () => Promise<T>, ok: string) => ({
    mutationFn: fn,
    onSuccess: () => { toast(ok, 'success'); refresh() },
    onError: (e: unknown) => toast(errMsg(e, 'No se pudo completar la acción'), 'error'),
  })

  const marcar = useMutation({ ...run(() => api.marcarPagado(id, undefined), 'Pago registrado'), mutationFn: (soloRenta?: boolean) => api.marcarPagado(id, soloRenta) })
  const confirmar = useMutation(run(() => api.confirmarPagoAdmin(pago!.id), 'Pago confirmado'))
  const cancelarPago = useMutation({ mutationFn: () => api.cancelarPago(pago!.id), onSuccess: () => { setCancelar(false); toast('Pago cancelado', 'success'); refresh() } })
  const eliminarAbono = useMutation({ mutationFn: (a: string) => api.eliminarAbono(a), onSuccess: () => { toast('Abono eliminado', 'success'); refresh() }, onError: (e) => toast(errMsg(e, 'No se pudo eliminar'), 'error') })
  const eliminarAbonoDep = useMutation({ mutationFn: (a: string) => api.eliminarAbonoDeposito(a), onSuccess: () => { toast('Abono eliminado', 'success'); refresh() }, onError: (e) => toast(errMsg(e, 'No se pudo eliminar'), 'error') })
  const pagarCuota = useMutation({ mutationFn: (c: string) => api.pagarCuota(c), onSuccess: () => { toast('Cargo pagado', 'success'); refresh() }, onError: (e) => toast(errMsg(e, 'No se pudo pagar'), 'error') })
  const borrarCuota = useMutation({ mutationFn: (c: string) => api.deleteCuota(c), onSuccess: () => { toast('Cargo eliminado', 'success'); refresh() }, onError: (e) => toast(errMsg(e, 'No se pudo eliminar'), 'error') })
  const borrarPromesa = useMutation({ mutationFn: (p: string) => api.eliminarPromesa(p), onSuccess: refresh, onError: (e) => toast(errMsg(e, 'No se pudo eliminar'), 'error') })

  if (inqQ.isLoading || estadoQ.isLoading) return <div className="grid place-items-center py-24"><Spinner className="size-8" /></div>
  if (inqQ.isError || !inq) return <Card><EmptyState icon={AlertTriangle} title="Inquilino no encontrado"><Link to="/pagos" className="font-semibold text-primary">Volver a pagos</Link></EmptyState></Card>

  const cfg = cfgQ.data ?? {}
  const periodo = estadoQ.data?.periodo
  const cuotas = estadoQ.data?.cuotas ?? []
  const cuotasPend = cuotas.filter(c => c.estado !== 'pagado')
  const totalAbonado = estadoQ.data?.total_abonado ?? 0
  const saldoPend = estadoQ.data?.saldo_pendiente ?? 0
  const monto = num(pago?.monto)
  const parcial = !pago?.confirmado && totalAbonado > 0
  const esTransf = inq.metodo_pago === 'transferencia' || inq.metodo_pago === 'ambos'
  const usaQr = cfg.usa_qr_inquilinos !== 'false'
  const enRevision = !!pago?.comprobante_url && !pago.rechazado && !pago.confirmado
  const est = estadosActuales.data?.find(e => e.inquilino_id === id)
  const dep = depSaldoQ.data

  const imprimir = () => {
    const rentaBase = num(inq.renta)
    const html = buildReciboHtml({
      arrendadorNombre: cfg.arrendador_nombre || 'Administración', nombreInquilino: inq.nombre_completo, deptoNumero: inq.depto_numero,
      periodoLabel: new Date().toLocaleDateString('es-MX', { month: 'long', year: 'numeric' }).toUpperCase(), renta: rentaBase,
      cargosExtra: cuotasPend.map(c => ({ concepto: c.concepto, monto: num(c.monto) })),
      abonosRenta: (abonosQ.data ?? []).map(a => ({ monto: num(a.monto), fecha: a.fecha })),
      saldoRentaPendiente: pago?.confirmado ? 0 : pago ? saldoPend : rentaBase + (estadoQ.data?.total_extra ?? 0),
      depositoPendiente: dep?.deposito_saldo ?? 0,
    })
    if (!imprimirHtml(html)) toast('El navegador bloqueó la ventana. Permite las ventanas emergentes para imprimir.', 'error')
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Cobrar renta" back="/pagos"
        subtitle={<><Link to={`/inquilinos/${inq.id}`} className="font-semibold text-primary hover:underline">{inq.nombre_completo}</Link> · Depto {inq.depto_numero}</>}
        actions={<Button variant="secondary" className="h-10 px-4" icon={<Printer className="size-4" />} onClick={imprimir}>Recibo</Button>} />

      <div className="grid gap-5 lg:grid-cols-2">
        <div className="space-y-5">
          {/* Estado del periodo */}
          <Card className={`space-y-4 ${pago?.confirmado ? 'border-success/30 bg-success-soft' : ''}`}>
            <div className="flex items-center gap-3">
              {pago?.confirmado ? <CheckCircle2 className="size-9 text-success" /> : enRevision ? <ShieldAlert className="size-9 text-warning" /> : <Wallet className="size-9 text-muted" />}
              <div className="min-w-0 flex-1">
                <p className="text-xs font-bold uppercase tracking-wide text-fg-2">{periodo ? periodoLabel(periodo) : '—'}</p>
                <p className="text-lg font-extrabold">{pago?.confirmado ? 'Pago confirmado' : enRevision ? 'Comprobante en revisión' : pago?.rechazado ? 'Comprobante rechazado' : pago ? 'Pago pendiente' : 'Sin registro de pago'}</p>
                {pago?.confirmado && pago.confirmado_en && <p className="text-xs text-fg-2">{fechaHora(pago.confirmado_en)}</p>}
              </div>
              <p className="text-2xl font-extrabold">{money(pago ? monto : num(inq.renta) + (estadoQ.data?.total_extra ?? 0))}</p>
            </div>

            {parcial && (
              <div>
                <ProgressBar value={(totalAbonado / Math.max(monto, 1)) * 100} />
                <p className="mt-1.5 text-xs text-fg-2">Abonado {money(totalAbonado)} · Saldo pendiente <b className="text-fg">{money(saldoPend)}</b></p>
              </div>
            )}
            {pago?.rechazado && !pago.confirmado && <p className="rounded-xl bg-danger-soft p-3 text-sm text-danger"><b>Rechazado:</b> {pago.comentario_admin || 'sin comentario'}</p>}
            {cuotasPend.length > 0 && !pago?.confirmado && <p className="text-xs text-fg-2">Incluye {cuotasPend.length} cargo{cuotasPend.length > 1 ? 's' : ''} extra ({money(estadoQ.data?.total_extra)}).</p>}

            {puedeMarcar && (
              <div className="flex flex-wrap gap-2">
                {!pago?.confirmado && <Button loading={marcar.isPending} icon={<Banknote className="size-4" />} onClick={() => (cuotasPend.length ? setChoice(true) : marcar.mutate(false))}>Marcar pagado</Button>}
                {!pago?.confirmado && <Button variant="secondary" icon={<HandCoins className="size-4" />} onClick={() => setAbonoModal({ tipo: 'renta' })}>Registrar abono</Button>}
                {pago?.confirmado && puedeCancelar(est) && <Button variant="ghost" className="text-danger" icon={<Undo2 className="size-4" />} onClick={() => { cancelarPago.reset(); setCancelar(true) }}>Cancelar pago</Button>}
              </div>
            )}
          </Card>

          {/* Comprobante / transferencia */}
          {esTransf && usaQr && (
            <Card className="space-y-3">
              <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">Transferencia y comprobante</h2>
              {(cfg.banco_clabe || cfg.banco_nombre) && (() => {
                const b = getBankBrand(cfg.banco_nombre); const info = detectBankInfo(cfg.banco_clabe || '')
                return (
                  <div className="rounded-2xl p-4 text-white" style={{ background: `linear-gradient(135deg, ${b.gradient[0]}, ${b.gradient[1]})` }}>
                    <p className="text-xs font-bold uppercase tracking-widest text-white/70">{cfg.banco_nombre}</p>
                    <p className="mt-1 font-mono text-lg font-bold tracking-wider">{info.formato}</p>
                    <p className="text-xs uppercase text-white/80">{cfg.banco_titular} · {info.tipo}</p>
                  </div>
                )
              })()}
              {pago?.comprobante_url ? (
                <>
                  <button onClick={() => setVerComprobante(true)} className="group relative block w-full overflow-hidden rounded-xl border border-line">
                    <img src={pago.comprobante_url} alt="Comprobante" className="max-h-64 w-full bg-hover object-contain" />
                    <span className="absolute bottom-2 right-2 inline-flex items-center gap-1 rounded-lg bg-black/60 px-2 py-1 text-xs font-semibold text-white"><FileImage className="size-3.5" /> Ampliar</span>
                  </button>
                  {pago.comprobante_subido_en && <p className="text-xs text-muted">Enviado {fechaHora(pago.comprobante_subido_en)}</p>}
                  {puedeMarcar && enRevision && (
                    <div className="grid grid-cols-2 gap-2">
                      <Button loading={confirmar.isPending} icon={<CheckCircle2 className="size-4" />} onClick={() => confirmar.mutate()}>Confirmar pago</Button>
                      <Button variant="danger" icon={<XCircle className="size-4" />} onClick={() => setRechazar(true)}>Rechazar</Button>
                    </div>
                  )}
                </>
              ) : <p className="text-sm text-muted">El inquilino aún no ha enviado comprobante.</p>}
            </Card>
          )}

          {/* Abonos */}
          <Card className="space-y-3">
            <div className="flex items-center justify-between">
              <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">Abonos del periodo</h2>
              {puedeMarcar && pago && !pago.confirmado && <Button variant="ghost" className="h-8 px-3" icon={<Plus className="size-4" />} onClick={() => setAbonoModal({ tipo: 'renta' })}>Abono</Button>}
            </div>
            <Abonos items={abonosQ.data ?? []} editable={puedeMarcar} onEdit={a => setAbonoModal({ tipo: 'renta', abono: a })} onDelete={a => confirm('¿Eliminar este abono?') && eliminarAbono.mutate(a.id)} />
          </Card>

          {/* Cargos extra */}
          <Card className="space-y-3">
            <div className="flex items-center justify-between">
              <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">Cargos extra</h2>
              {esAdmin && <Button variant="ghost" className="h-8 px-3" icon={<Plus className="size-4" />} onClick={() => setCuotaModal(true)}>Agregar</Button>}
            </div>
            {cuotas.length === 0 ? <p className="text-sm text-muted">Sin cargos extra este periodo</p> : (
              <ul className="divide-y divide-line">
                {cuotas.map(c => (
                  <li key={c.id} className="flex items-center gap-3 py-2.5 text-sm">
                    <span className="min-w-0 flex-1"><span className="block truncate font-semibold">{c.concepto}</span><span className="text-xs text-muted">{fecha(c.created_at)}</span></span>
                    <b>{money(c.monto)}</b>
                    {c.estado === 'pagado' ? <Badge tone="success">Pagado</Badge> : esAdmin && (
                      <span className="flex gap-1">
                        <Button variant="secondary" className="h-8 px-3" loading={pagarCuota.isPending && pagarCuota.variables === c.id} onClick={() => pagarCuota.mutate(c.id)}>Pagar</Button>
                        <button onClick={() => confirm('¿Eliminar este cargo?') && borrarCuota.mutate(c.id)} aria-label="Eliminar cargo" className="grid size-8 place-items-center rounded-lg text-muted hover:bg-danger-soft hover:text-danger"><Trash2 className="size-4" /></button>
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </Card>
        </div>

        <div className="space-y-5">
          <Promesa inquilinoId={id} pagoId={pago?.id ?? null} fechaActual={pago?.fecha_promesa ?? null} bloqueado={!!pago?.confirmado} puedeEditar={puedeMarcar} onChange={refresh} />

          {/* Promesas anteriores */}
          {!!promesasQ.data?.length && (
            <Card className="space-y-2">
              <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2"><CalendarClock className="size-4" /> Historial de promesas</h2>
              <ul className="divide-y divide-line">
                {promesasQ.data.map(p => (
                  <li key={p.id} className="flex items-center gap-3 py-2 text-sm">
                    <span className="flex-1"><b>{fecha(p.fecha_promesa)}</b><span className="block text-xs text-muted">{periodoLabel(p.periodo)} · registrada {fecha(p.created_at)}</span></span>
                    <Badge tone={p.estado_cumplimiento === 'cumplida' ? 'success' : p.estado_cumplimiento === 'incumplida' ? 'danger' : 'neutral'}>{p.estado_cumplimiento}</Badge>
                    {puedeMarcar && <button onClick={() => borrarPromesa.mutate(p.id)} aria-label="Quitar del historial" className="text-muted hover:text-danger"><Trash2 className="size-4" /></button>}
                  </li>
                ))}
              </ul>
            </Card>
          )}

          {/* Depósito */}
          <Card className="space-y-3">
            <div className="flex items-center justify-between">
              <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">Depósito</h2>
              {puedeMarcar && dep && dep.deposito_saldo > 0 && <Button variant="ghost" className="h-8 px-3" icon={<Plus className="size-4" />} onClick={() => setAbonoModal({ tipo: 'deposito' })}>Abono</Button>}
            </div>
            {dep && dep.deposito_total > 0 ? (
              <>
                <ProgressBar value={(dep.deposito_pagado / dep.deposito_total) * 100} tone="primary" />
                <div className="grid grid-cols-3 text-center text-sm">
                  <div><p className="text-xs text-fg-2">Total</p><b>{money(dep.deposito_total)}</b></div>
                  <div><p className="text-xs text-fg-2">Pagado</p><b className="text-success">{money(dep.deposito_pagado)}</b></div>
                  <div><p className="text-xs text-fg-2">Pendiente</p><b className={dep.deposito_saldo > 0 ? 'text-danger' : ''}>{money(dep.deposito_saldo)}</b></div>
                </div>
                <Abonos items={depAbonosQ.data ?? []} editable={esAdmin} onEdit={a => setAbonoModal({ tipo: 'deposito', abono: a })} onDelete={a => confirm('¿Eliminar este abono?') && eliminarAbonoDep.mutate(a.id)} />
              </>
            ) : <p className="text-sm text-muted">Este inquilino no tiene depósito.</p>}
          </Card>

          {/* Adeudo por mes */}
          {saldoQ.data && (
            <Card className="space-y-3">
              <div className="flex items-center justify-between">
                <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">Adeudo por mes</h2>
                <b className={saldoQ.data.deuda_total > 0 ? 'text-danger' : 'text-success'}>{money(saldoQ.data.deuda_total)}</b>
              </div>
              {saldoQ.data.detalle.filter(d => num(d.saldo) > 0.5).length === 0 ? <p className="text-sm text-muted">Sin adeudos 🎉</p> : (
                <ul className="divide-y divide-line text-sm">
                  {saldoQ.data.detalle.filter(d => num(d.saldo) > 0.5).map(d => (
                    <li key={d.periodo} className="flex items-center justify-between py-2">
                      <span className="inline-block first-letter:uppercase">{periodoLabel(d.periodo)}</span>
                      <span className="text-right"><b className="text-danger">{money(d.saldo)}</b>{num(d.abonado) > 0 && <span className="block text-xs text-muted">abonado {money(d.abonado)}</span>}</span>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          )}

          {/* Historial */}
          <Card className="space-y-3">
            <button onClick={() => setHistAbierto(o => !o)} className="flex w-full items-center justify-between text-xs font-bold uppercase tracking-wide text-fg-2">
              <span className="flex items-center gap-2"><History className="size-4" /> Historial de pagos</span>
              <ChevronDown className={`size-4 transition ${histAbierto ? 'rotate-180' : ''}`} />
            </button>
            {histAbierto && (histQ.isLoading ? <Spinner /> : (
              <ul className="divide-y divide-line text-sm">
                {histQ.data?.map(h => (
                  <li key={h.id} className="flex items-center justify-between gap-3 py-2.5">
                    <span><span className="font-semibold inline-block first-letter:uppercase">{periodoLabel(h.periodo)}</span><span className="block text-xs text-muted">{h.confirmado ? `Pagado ${fechaHora(h.confirmado_en)}` : 'Pendiente'}</span></span>
                    <span className="flex items-center gap-2"><b>{money(h.monto)}</b>
                      <Badge tone={!h.confirmado ? 'neutral' : h.a_tiempo === false ? 'warning' : 'success'}>{!h.confirmado ? 'Pendiente' : h.a_tiempo === false ? 'Con retraso' : 'A tiempo'}</Badge></span>
                  </li>
                ))}
                {histQ.data?.length === 0 && <li className="py-3 text-muted">Sin pagos registrados</li>}
              </ul>
            ))}
          </Card>
        </div>
      </div>

      {/* Modales */}
      <AbonoModal state={abonoModal} inquilinoId={id} metodoDefault={inq.metodo_pago === 'transferencia' ? 'transferencia' : 'efectivo'} onClose={() => setAbonoModal(null)} onSaved={() => { setAbonoModal(null); refresh() }} />
      <RechazarModal open={rechazar} pagoId={pago?.id} onClose={() => setRechazar(false)} onDone={() => { setRechazar(false); refresh() }} />
      <CuotaModal open={cuotaModal} inquilinoId={id} onClose={() => setCuotaModal(false)} onSaved={() => { setCuotaModal(false); refresh() }} />

      <Modal open={choice} title="Marcar como pagado" onClose={() => setChoice(false)}>
        <div className="space-y-3">
          <p className="text-sm text-fg-2">Hay {cuotasPend.length} cargo(s) extra pendiente(s) por {money(estadoQ.data?.total_extra)}. ¿Qué cobraste?</p>
          <Button full icon={<Banknote className="size-4" />} onClick={() => { setChoice(false); marcar.mutate(false) }}>Todo: renta + cargos extra</Button>
          <Button full variant="secondary" onClick={() => { setChoice(false); marcar.mutate(true) }}>Solo la renta (los cargos quedan pendientes)</Button>
        </div>
      </Modal>
      <ConfirmDialog open={cancelar} title="Cancelar pago" confirmLabel="Cancelar pago" loading={cancelarPago.isPending} error={cancelarPago.error instanceof Error ? cancelarPago.error.message : null}
        onClose={() => setCancelar(false)} onConfirm={() => cancelarPago.mutate()}><p>El periodo volverá a quedar como pendiente y se eliminarán sus abonos.</p></ConfirmDialog>
      <Modal open={verComprobante} title="Comprobante de pago" onClose={() => setVerComprobante(false)}>
        {pago?.comprobante_url && <img src={pago.comprobante_url} alt="Comprobante" className="max-h-[75vh] w-full rounded-xl object-contain" />}
      </Modal>
    </div>
  )
}

function Abonos({ items, editable, onEdit, onDelete }: { items: Abono[]; editable: boolean; onEdit: (a: Abono) => void; onDelete: (a: Abono) => void }) {
  if (!items.length) return <p className="text-sm text-muted">Sin abonos registrados</p>
  return (
    <ul className="divide-y divide-line">
      {items.map(a => (
        <li key={a.id} className="flex items-center gap-3 py-2.5 text-sm">
          <span className="min-w-0 flex-1">
            <span className="block font-semibold">{fecha(a.fecha)} · {a.metodo === 'transferencia' ? 'Transferencia' : 'Efectivo'}</span>
            <span className="block truncate text-xs text-muted">{[a.nota, a.registrado_por_nombre && `por ${a.registrado_por_nombre}`].filter(Boolean).join(' · ') || '—'}</span>
          </span>
          <b className="text-success">{money(a.monto)}</b>
          {editable && (
            <span className="flex gap-1">
              <button onClick={() => onEdit(a)} aria-label="Editar abono" className="grid size-8 place-items-center rounded-lg text-muted hover:bg-hover hover:text-fg"><Pencil className="size-4" /></button>
              <button onClick={() => onDelete(a)} aria-label="Eliminar abono" className="grid size-8 place-items-center rounded-lg text-muted hover:bg-danger-soft hover:text-danger"><Trash2 className="size-4" /></button>
            </span>
          )}
        </li>
      ))}
    </ul>
  )
}

function AbonoModal({ state, inquilinoId, metodoDefault, onClose, onSaved }: { state: { tipo: 'renta' | 'deposito'; abono?: Abono } | null; inquilinoId: string; metodoDefault: string; onClose: () => void; onSaved: () => void }) {
  return (
    <Modal open={!!state} title={`${state?.abono ? 'Editar' : 'Registrar'} abono ${state?.tipo === 'deposito' ? 'al depósito' : 'a la renta'}`} onClose={onClose}>
      {state && <AbonoForm key={state.abono?.id ?? state.tipo} state={state} inquilinoId={inquilinoId} metodoDefault={metodoDefault} onSaved={onSaved} />}
    </Modal>
  )
}

function AbonoForm({ state, inquilinoId, metodoDefault, onSaved }: { state: { tipo: 'renta' | 'deposito'; abono?: Abono }; inquilinoId: string; metodoDefault: string; onSaved: () => void }) {
  const toast = useToast()
  const a = state.abono
  const [monto, setMonto] = useState(a ? String(num(a.monto)) : '')
  const [fechaV, setFechaV] = useState(a ? String(a.fecha).slice(0, 10) : hoyISO())
  const [metodo, setMetodo] = useState(a?.metodo ?? metodoDefault)
  const [nota, setNota] = useState(a?.nota ?? '')
  const [error, setError] = useState('')

  const guardar = useMutation({
    mutationFn: () => {
      const m = parseFloat(monto)
      const body = { monto: m, fecha: fechaV || undefined, metodo, nota: nota.trim() || undefined }
      if (state.tipo === 'deposito') return a ? api.editarAbonoDeposito(a.id, body) : api.registrarAbonoDeposito({ inquilino_id: inquilinoId, ...body })
      return a ? api.editarAbono(a.id, body) : api.registrarAbono({ inquilino_id: inquilinoId, ...body })
    },
    onSuccess: () => { toast('Abono guardado', 'success'); onSaved() },
    onError: (e) => setError(errMsg(e, 'No se pudo guardar el abono')),
  })
  const submit = () => {
    if (!(parseFloat(monto) > 0)) return setError('Ingresa un monto válido mayor a cero')
    if (!/^\d{4}-\d{2}-\d{2}$/.test(fechaV)) return setError('Indica la fecha del abono')
    setError(''); guardar.mutate()
  }
  return (
    <div className="space-y-5">
      <Input label="Monto *" type="number" inputMode="decimal" min={0} step="0.01" value={monto} autoFocus onChange={e => { setError(''); setMonto(e.target.value) }} />
      <div className="grid grid-cols-2 gap-3">
        <Input label="Fecha" type="date" value={fechaV} max={hoyISO()} onChange={e => setFechaV(e.target.value)} />
        <Select label="Método" value={metodo} onChange={e => setMetodo(e.target.value)}><option value="efectivo">Efectivo</option><option value="transferencia">Transferencia</option></Select>
      </div>
      <Textarea label="Nota (opcional)" className="min-h-20" value={nota} onChange={e => setNota(e.target.value)} />
      {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
      <div className="pt-2">
        <Button full loading={guardar.isPending} onClick={submit}>Guardar abono</Button>
      </div>
    </div>
  )
}

function RechazarModal({ open, pagoId, onClose, onDone }: { open: boolean; pagoId?: string; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [c, setC] = useState('')
  const rechazar = useMutation({
    mutationFn: () => api.rechazarPago(pagoId!, c.trim()),
    onSuccess: () => { toast('Comprobante rechazado', 'success'); setC(''); onDone() },
    onError: (e) => toast(errMsg(e, 'No se pudo rechazar'), 'error'),
  })
  return (
    <Modal open={open} title="Rechazar comprobante" onClose={onClose}>
      <div className="space-y-5">
        <p className="text-sm text-fg-2">El inquilino recibirá una notificación. Puedes indicarle el motivo.</p>
        <Textarea label="Motivo (opcional)" placeholder="Ej. La imagen está borrosa" value={c} onChange={e => setC(e.target.value)} />
        <div className="pt-2">
          <Button full variant="danger" loading={rechazar.isPending} icon={<Ban className="size-4" />} onClick={() => rechazar.mutate()}>Rechazar comprobante</Button>
        </div>
      </div>
    </Modal>
  )
}

function CuotaModal({ open, inquilinoId, onClose, onSaved }: { open: boolean; inquilinoId: string; onClose: () => void; onSaved: () => void }) {
  const toast = useToast()
  const [concepto, setConcepto] = useState('')
  const [monto, setMonto] = useState('')
  const [error, setError] = useState('')
  const crear = useMutation({
    mutationFn: () => api.createCuota({ inquilino_id: inquilinoId, concepto: concepto.trim(), monto: parseFloat(monto) }),
    onSuccess: () => { toast('Cargo agregado', 'success'); setConcepto(''); setMonto(''); onSaved() },
    onError: (e) => setError(errMsg(e, 'No se pudo agregar el cargo')),
  })
  const submit = () => {
    if (!concepto.trim()) return setError('El concepto es requerido')
    if (!(parseFloat(monto) > 0)) return setError('Ingresa un monto válido mayor a cero')
    setError(''); crear.mutate()
  }
  return (
    <Modal open={open} title="Nuevo cargo extra" onClose={onClose}>
      <div className="space-y-5">
        <Input label="Concepto *" placeholder="Ej. Reparación de llave" value={concepto} onChange={e => { setError(''); setConcepto(e.target.value) }} />
        <Input label="Monto *" type="number" inputMode="decimal" min={0} step="0.01" value={monto} onChange={e => { setError(''); setMonto(e.target.value) }} />
        {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
        <div className="pt-2">
          <Button full loading={crear.isPending} onClick={submit}>Agregar cargo</Button>
        </div>
      </div>
    </Modal>
  )
}

function Promesa({ inquilinoId, pagoId, fechaActual, bloqueado, puedeEditar, onChange }: { inquilinoId: string; pagoId: string | null; fechaActual: string | null; bloqueado: boolean; puedeEditar: boolean; onChange: () => void }) {
  const toast = useToast()
  const guardada = fechaActual ? String(fechaActual).slice(0, 10) : ''
  const [valor, setValor] = useState(guardada)
  const [editando, setEditando] = useState(!guardada)
  // si el servidor cambia la fecha (otro admin, refresco), se refleja salvo que se esté editando
  const [prev, setPrev] = useState(guardada)
  if (prev !== guardada) { setPrev(guardada); if (!editando) setValor(guardada) }

  const guardar = useMutation({
    mutationFn: (f: string | null) => (pagoId ? api.setPromesa(pagoId, f) : api.setPromesaInquilino(inquilinoId, f)),
    onSuccess: (_r, f) => { toast(f ? 'Promesa guardada' : 'Promesa eliminada', 'success'); setEditando(!f); if (!f) setValor(''); onChange() },
    onError: (e) => toast(errMsg(e, 'No se pudo guardar la promesa'), 'error'),
  })

  const rapido = (dias: number) => { const d = new Date(); d.setDate(d.getDate() + dias); setValor(new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10)); setEditando(true) }
  const preview = (() => {
    if (!valor) return null
    const d = new Date(`${valor}T12:00:00`); const hoy = new Date(); hoy.setHours(0, 0, 0, 0); d.setHours(0, 0, 0, 0)
    const diff = Math.round((d.getTime() - hoy.getTime()) / 86_400_000)
    const raw = d.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
    const rel = diff === 0 ? 'hoy' : diff === 1 ? 'mañana' : diff === -1 ? 'ayer' : diff > 1 ? `en ${diff} días` : `hace ${Math.abs(diff)} días`
    return `${raw.charAt(0).toUpperCase()}${raw.slice(1)} · ${rel}`
  })()

  return (
    <Card className="space-y-3">
      <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2"><CalendarClock className="size-4" /> Promesa de pago</h2>
      {bloqueado ? <p className="text-sm text-muted">El periodo ya está pagado.</p> : !puedeEditar && !guardada ? <p className="text-sm text-muted">Sin promesa registrada.</p> : (
        <>
          {!editando && guardada ? (
            <div className="flex items-center justify-between gap-3 rounded-xl bg-warning-soft p-3">
              <p className="text-sm font-semibold">{preview}</p>
              {puedeEditar && <Button variant="ghost" className="h-8 px-3" onClick={() => setEditando(true)}>Cambiar</Button>}
            </div>
          ) : puedeEditar && (
            <>
              <div className="flex flex-wrap gap-2">
                {[['Hoy', 0], ['Mañana', 1], ['En 3 días', 3], ['En 1 semana', 7]].map(([l, d]) => (
                  <button key={l as string} onClick={() => rapido(d as number)} className="rounded-full border border-line bg-card px-3 py-1 text-xs font-semibold text-fg-2 hover:bg-hover">{l}</button>
                ))}
              </div>
              <Input type="date" aria-label="Fecha prometida" value={valor} onChange={e => setValor(e.target.value)} />
              {preview && <p className="text-xs text-fg-2">{preview}</p>}
              <div className="flex gap-2">
                <Button className="h-10 flex-1" loading={guardar.isPending} disabled={!valor} onClick={() => guardar.mutate(valor)}>Guardar promesa</Button>
                {guardada && <Button variant="secondary" className="h-10" loading={guardar.isPending} onClick={() => guardar.mutate(null)}>Quitar</Button>}
              </div>
            </>
          )}
        </>
      )}
    </Card>
  )
}

