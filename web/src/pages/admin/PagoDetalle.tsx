import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Banknote, CalendarClock, CheckCircle2, ChevronDown, FileImage, HandCoins, History, Plus, Printer, ShieldAlert, Trash2, Undo2, Wallet, XCircle } from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EmptyState } from '../../components/ui/EmptyState'
import { Modal } from '../../components/ui/Modal'
import { PageHeader } from '../../components/ui/PageHeader'
import { ProgressBar } from '../../components/ui/ProgressBar'
import { Spinner } from '../../components/ui/Spinner'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { detectBankInfo, getBankBrand } from '../../lib/bank'
import { puedeCancelar } from '../../lib/cobranza'
import { fecha, fechaHora, money, num, periodoLabel } from '../../lib/format'
import { buildReciboHtml, imprimirHtml } from '../../lib/recibo'
import { useSSE } from '../../lib/sse'
import type { Abono } from '../../lib/types'
import { nombreVisible } from '../../lib/nombre'
import { Abonos } from './pago/Abonos'
import { AbonoModal } from './pago/AbonoModal'
import { RechazarModal } from './pago/RechazarModal'
import { CuotaModal } from './pago/CuotaModal'
import { Promesa } from './pago/Promesa'
import { KEYS, errMsg } from './pago/comun'

export default function PagoDetalle() {
  const { id = '' } = useParams()
  const { user, can } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const puedeMarcar = can('pagos.marcar')
  const esAdmin = user?.rol === 'admin'

  const [abonoModal, setAbonoModal] = useState<{ tipo: 'renta' | 'deposito'; abono?: Abono; periodo?: string; saldo?: number } | null>(null)
  const [rechazar, setRechazar] = useState(false)
  const [cuotaModal, setCuotaModal] = useState(false)
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
      saldoRentaPendiente: pago?.confirmado ? 0 : pago ? saldoPend : rentaBase,
      depositoPendiente: dep?.deposito_saldo ?? 0,
    })
    if (!imprimirHtml(html)) toast('El navegador bloqueó la ventana. Permite las ventanas emergentes para imprimir.', 'error')
  }

  return (
    <div className="space-y-6">
      <PageHeader title="Cobrar renta" back="/pagos"
        subtitle={<><Link to={`/inquilinos/${inq.id}`} className="font-semibold text-primary hover:underline">{nombreVisible(inq.nombre_completo)}</Link> · Depto {inq.depto_numero}</>}
        actions={<Button variant="secondary" className="h-10 px-4" icon={<Printer className="size-4" />} onClick={imprimir}>Recibo</Button>} />

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
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
              <p className="text-2xl font-extrabold">{money(pago && !pago.confirmado && totalAbonado > 0 ? saldoPend : pago ? monto : num(inq.renta))}</p>
            </div>

            {parcial && (
              <div>
                <ProgressBar value={(totalAbonado / Math.max(monto, 1)) * 100} />
                <p className="mt-1.5 text-xs text-fg-2">Abonado {money(totalAbonado)} · Saldo pendiente <b className="text-fg">{money(saldoPend)}</b></p>
              </div>
            )}
            {pago?.rechazado && !pago.confirmado && <p className="rounded-xl bg-danger-soft p-3 text-sm text-danger"><b>Rechazado:</b> {pago.comentario_admin || 'sin comentario'}</p>}
            {cuotasPend.length > 0 && !pago?.confirmado && <p className="text-xs font-semibold text-warning">Aparte de la renta: {cuotasPend.length} cargo{cuotasPend.length > 1 ? 's' : ''} extra pendiente{cuotasPend.length > 1 ? 's' : ''} ({money(estadoQ.data?.total_extra)}). Se cobran en "Cargos extra".</p>}

            {puedeMarcar && (
              <div className="flex flex-wrap gap-2">
                {!pago?.confirmado && <Button loading={marcar.isPending} icon={<Banknote className="size-4" />} onClick={() => marcar.mutate(false)}>Marcar renta pagada</Button>}
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
                      <span className="flex items-center gap-3">
                        <span className="text-right"><b className="text-danger">{money(d.saldo)}</b>{num(d.abonado) > 0 && <span className="block text-xs text-muted">abonado {money(d.abonado)}</span>}</span>
                        {puedeMarcar && <Button variant="secondary" className="h-8 px-3" icon={<HandCoins className="size-4" />} onClick={() => setAbonoModal({ tipo: 'renta', periodo: d.periodo, saldo: num(d.saldo) })}>Abonar</Button>}
                      </span>
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

      <ConfirmDialog open={cancelar} title="Cancelar pago" confirmLabel="Cancelar pago" loading={cancelarPago.isPending} error={cancelarPago.error instanceof Error ? cancelarPago.error.message : null}
        onClose={() => setCancelar(false)} onConfirm={() => cancelarPago.mutate()}><p>El periodo volverá a quedar como pendiente y se eliminarán sus abonos.</p></ConfirmDialog>
      <Modal open={verComprobante} title="Comprobante de pago" onClose={() => setVerComprobante(false)}>
        {pago?.comprobante_url && <img src={pago.comprobante_url} alt="Comprobante" className="max-h-[75vh] w-full rounded-xl object-contain" />}
      </Modal>
    </div>
  )
}
