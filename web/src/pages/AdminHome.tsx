import { useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowRight, Building2, CalendarClock, CheckCircle2, FileText, MessageSquare, TrendingUp, Wallet } from 'lucide-react'
import { motion } from 'motion/react'
import { useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { AnimatedNumber } from '../components/ui/AnimatedNumber'
import { Badge } from '../components/ui/Badge'
import { Card } from '../components/ui/Card'
import { StaggerItem, StaggerList } from '../components/ui/MotionPrimitives'
import { ProgressBar } from '../components/ui/ProgressBar'
import { Spinner } from '../components/ui/Spinner'
import { api } from '../lib/api'
import { useAuth } from '../lib/auth'
import { diaPago, EPS, rowState, toSaldo, type EstadoActual, type RowState, type Saldo } from '../lib/cobranza'
import { fecha, iniciales, money, num } from '../lib/format'
import { useSSE } from '../lib/sse'
import { useUsaApp } from '../lib/usaApp'

type Tab = 'atrasados' | 'porpagar' | 'pagados' | 'extras' | 'deposito'
interface Fila { id: string; nombre: string; depto: number; renta: number; dia: number | null; state: RowState; saldo: Saldo }

const diasHasta = (iso: string) => Math.ceil((new Date(iso).getTime() - Date.now()) / 86_400_000)
const SIN_SALDO: Saldo = { total: 0, vencida: 0, periodoVencido: null, cuotasExtra: 0, deposito: 0 }

export default function AdminHome() {
  const { user, can } = useAuth()
  const qc = useQueryClient()
  const usaApp = useUsaApp()
  const verInq = can('inquilinos')
  const verCobranza = can('pagos') && verInq

  const refrescar = () => { for (const k of ['inquilinos', 'estados-actuales', 'saldos', 'resumen-deuda', 'tickets-dashboard']) void qc.invalidateQueries({ queryKey: [k] }) }
  useSSE('payment_confirmed', refrescar)
  useSSE('comprobante_subido', refrescar)
  useSSE('ticket_new', refrescar)

  const stats = useQuery({ queryKey: ['deptos-stats'], queryFn: async () => (await api.getDepartamentosStats()).data, enabled: can('departamentos') })
  const inquilinos = useQuery({ queryKey: ['inquilinos', 'activo'], queryFn: async () => (await api.getInquilinos({ estado: 'activo' })).data, enabled: verInq, refetchInterval: 30_000 })
  const resumen = useQuery({ queryKey: ['contratos-resumen'], queryFn: async () => (await api.getResumenContratos()).data, enabled: !verInq && can('contratos') })
  const estados = useQuery({ queryKey: ['estados-actuales'], queryFn: async () => (await api.getEstadosActuales()).data, enabled: verCobranza, refetchInterval: 30_000 })
  const saldosQ = useQuery({ queryKey: ['saldos'], queryFn: async () => (await api.getSaldos()).data, enabled: verCobranza, refetchInterval: 30_000 })
  const tickets = useQuery({ queryKey: ['tickets-dashboard'], queryFn: async () => (await api.getTickets()).data, enabled: can('tickets') && usaApp })

  // ── Cobranza del mes ──
  const filas = useMemo<Fila[]>(() => {
    if (!inquilinos.data || !estados.data || !saldosQ.data) return []
    const est = new Map<string, EstadoActual>(estados.data.map(e => [e.inquilino_id, e]))
    const sal = new Map<string, Saldo>(saldosQ.data.map(s => [s.inquilino_id, toSaldo(s)]))
    return inquilinos.data.map(i => {
      const saldo = sal.get(i.id) ?? SIN_SALDO
      return { id: i.id, nombre: i.nombre_completo, depto: i.depto_numero, renta: num(i.renta), dia: diaPago(i.fecha_pago), state: rowState(est.get(i.id), saldo), saldo }
    })
  }, [inquilinos.data, estados.data, saldosQ.data])

  const listas = useMemo(() => ({
    atrasados: filas.filter(f => f.state === 'atrasado').sort((a, b) => b.saldo.vencida - a.saldo.vencida),
    porpagar: filas.filter(f => f.state === 'pendiente' || f.state === 'revision').sort((a, b) => (a.dia ?? 99) - (b.dia ?? 99)),
    pagados: filas.filter(f => f.state === 'pagado').sort((a, b) => a.depto - b.depto),
    extras: filas.filter(f => f.saldo.cuotasExtra > EPS).sort((a, b) => b.saldo.cuotasExtra - a.saldo.cuotasExtra),
    deposito: filas.filter(f => f.saldo.deposito > EPS).sort((a, b) => b.saldo.deposito - a.saldo.deposito),
  }), [filas])

  const esperado = filas.reduce((a, f) => a + f.renta, 0)
  const cobrado = listas.pagados.reduce((a, f) => a + f.renta, 0)
  const vencido = listas.atrasados.reduce((a, f) => a + f.saldo.vencida, 0)
  const pct = esperado > 0 ? Math.round((cobrado / esperado) * 100) : 0
  const [tabSel, setTabSel] = useState<Tab | null>(null)
  const tab: Tab = tabSel ?? (listas.atrasados.length > 0 ? 'atrasados' : 'porpagar')

  // ── Contratos ──
  const contratos = useMemo(() => {
    const base = inquilinos.data
      ? inquilinos.data.map(i => ({ id: i.id, nombre: i.nombre_completo, depto: i.depto_numero, termino: i.fecha_termino }))
      : (resumen.data ?? []).map(i => ({ id: i.id, nombre: i.nombre_completo, depto: i.depto_numero, termino: i.fecha_termino }))
    return base.filter(c => c.termino).map(c => ({ ...c, dias: diasHasta(c.termino) })).filter(c => c.dias <= 60).sort((a, b) => a.dias - b.dias)
  }, [inquilinos.data, resumen.data])
  const porVencer = contratos.filter(c => c.dias >= 0 && c.dias <= 30).length
  const yaVencidos = contratos.filter(c => c.dias < 0).length

  // ── Próximos pagos (7 días) ──
  const proximos = useMemo(() => {
    const hoy = new Date(); const d = hoy.getDate(); const dim = new Date(hoy.getFullYear(), hoy.getMonth() + 1, 0).getDate()
    return listas.porpagar.filter(f => f.state === 'pendiente' && f.dia)
      .map(f => ({ ...f, en: f.dia! >= d ? f.dia! - d : dim - d + f.dia! })).filter(f => f.en <= 7).sort((a, b) => a.en - b.en)
  }, [listas.porpagar])

  const abiertos = (tickets.data ?? []).filter(t => t.estado !== 'resuelto')
  const s = stats.data
  const total = num(s?.total); const ocupados = num(s?.ocupados)
  const mes = new Date().toLocaleDateString('es-MX', { month: 'long', year: 'numeric' })
  const cargandoCobranza = verCobranza && (inquilinos.isLoading || estados.isLoading || saldosQ.isLoading)

  const TABS: { v: Tab; label: string; n: number; tone: string }[] = [
    { v: 'atrasados', label: 'Atrasados', n: listas.atrasados.length, tone: 'text-danger' },
    { v: 'porpagar', label: 'Por pagar', n: listas.porpagar.length, tone: '' },
    { v: 'pagados', label: 'Pagados', n: listas.pagados.length, tone: 'text-success' },
    { v: 'extras', label: 'Cuotas extra', n: listas.extras.length, tone: 'text-warning' },
    { v: 'deposito', label: 'Falta depósito', n: listas.deposito.length, tone: 'text-primary' },
  ]
  const montoDe = (f: Fila): number => ({ atrasados: f.saldo.vencida, porpagar: f.renta, pagados: f.renta, extras: f.saldo.cuotasExtra, deposito: f.saldo.deposito })[tab]
  const colorMonto = { atrasados: 'text-danger', porpagar: 'text-fg', pagados: 'text-success', extras: 'text-warning', deposito: 'text-primary' }[tab]

  return (
    <StaggerList className="space-y-6">
      <div>
        <h1 className="text-2xl font-extrabold tracking-tight">Hola, {user?.nombre_completo.split(' ')[0]} 👋</h1>
        <p className="text-sm text-fg-2">Resumen de {mes}</p>
      </div>

      {/* ── Indicadores ── */}
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {verCobranza && (
          <StaggerItem>
            <Kpi icon={<TrendingUp />} tone="#10B981" label="Cobrado este mes" value={<AnimatedNumber value={cobrado} format={money} />}
              hint={`de ${money(esperado)} esperados`} extra={<div className="mt-2"><ProgressBar value={pct} tone={pct >= 80 ? 'success' : 'primary'} /></div>} />
          </StaggerItem>
        )}
        {verCobranza && (
          <StaggerItem>
            <Kpi icon={<AlertTriangle />} tone="#EF4444" label="Atrasado" value={<AnimatedNumber value={vencido} format={money} />}
              hint={listas.atrasados.length ? `${listas.atrasados.length} inquilino${listas.atrasados.length !== 1 ? 's' : ''}` : 'Sin atrasos'} valueClass={vencido > EPS ? 'text-danger' : ''} />
          </StaggerItem>
        )}
        {(verInq || can('contratos') || can('departamentos')) && (
          <StaggerItem>
            <Kpi icon={<CalendarClock />} tone="#F59E0B" label="Contratos por vencer"
              value={<AnimatedNumber value={verInq || can('contratos') ? porVencer : num(s?.contratos_por_vencer)} />}
              hint={(verInq || can('contratos') ? yaVencidos : num(s?.contratos_vencidos)) > 0 ? `próximos 30 días · ${verInq || can('contratos') ? yaVencidos : num(s?.contratos_vencidos)} vencido(s)` : 'próximos 30 días'} />
          </StaggerItem>
        )}
        {can('departamentos') && (
          <StaggerItem>
            <Kpi icon={<Building2 />} tone="#3B82F6" label="Ocupación" value={s ? `${ocupados}/${total}` : null}
              hint={s ? `${total ? Math.round((ocupados / total) * 100) : 0}% ocupado · ${money(s.ingresos_mensuales)}/mes` : undefined} />
          </StaggerItem>
        )}
      </div>

      <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-5">
        {/* ── Cobranza del mes ── */}
        {verCobranza && (
          <StaggerItem className="min-w-0 lg:col-span-3">
            <Card className="p-0">
              <div className="flex items-center justify-between gap-3 px-5 pt-5">
                <div>
                  <h2 className="font-bold">Cobranza de {mes.split(' ')[0]}</h2>
                  <p className="text-xs text-muted">{filas.length} inquilinos activos · {pct}% cobrado</p>
                </div>
                <Link to="/pagos" className="flex items-center gap-1 text-xs font-semibold text-primary hover:underline">Ver todo <ArrowRight className="size-3.5" /></Link>
              </div>
              <div className="custom-scrollbar mt-4 flex gap-2 overflow-x-auto px-5 pb-1">
                {TABS.map(t => (
                  <button key={t.v} onClick={() => setTabSel(t.v)}
                    className={`shrink-0 cursor-pointer rounded-full border px-3.5 py-1.5 text-xs font-bold transition ${tab === t.v ? 'border-primary/50 bg-primary-soft text-fg' : 'border-line text-fg-2 hover:bg-hover'}`}>
                    {t.label} <span className={t.n > 0 ? t.tone : 'text-muted'}>{t.n}</span>
                  </button>
                ))}
              </div>
              {cargandoCobranza && <div className="grid place-items-center py-12"><Spinner /></div>}
              {!cargandoCobranza && listas[tab].length === 0 && (
                <p className="px-5 py-10 text-center text-sm text-muted">{tab === 'atrasados' ? 'Sin atrasos 🎉' : tab === 'porpagar' ? 'Todo cobrado este mes 🎉' : 'Nada por aquí'}</p>
              )}
              <ul key={tab} className="mt-2 divide-y divide-line px-2 pb-3">
                {listas[tab].slice(0, 8).map((f, idx) => (
                  <motion.li key={f.id} initial={{ opacity: 0, x: -8 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: idx * 0.03 }}>
                    <Link to={`/pagos/${f.id}`} className="flex items-center gap-3 rounded-xl px-3 py-2.5 transition hover:bg-hover/50">
                      <span className="grid size-9 shrink-0 place-items-center rounded-full bg-primary-soft text-xs font-bold text-primary">{iniciales(f.nombre)}</span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-semibold">{f.nombre}</span>
                        <span className="text-xs text-muted">Depto {f.depto}{f.dia ? ` · paga el ${f.dia}` : ''}{f.state === 'revision' && ' · '}{f.state === 'revision' && <span className="font-semibold text-warning">En revisión</span>}</span>
                      </span>
                      <span className={`text-sm font-extrabold ${colorMonto}`}>{tab === 'pagados' && <CheckCircle2 className="mr-1 inline size-4" />}{money(montoDe(f))}</span>
                    </Link>
                  </motion.li>
                ))}
              </ul>
              {listas[tab].length > 8 && <Link to="/pagos" className="block border-t border-line px-5 py-3 text-center text-xs font-semibold text-primary hover:bg-hover/40">Ver los {listas[tab].length} en Pagos</Link>}
            </Card>
          </StaggerItem>
        )}

        <div className={`min-w-0 space-y-5 ${verCobranza ? 'lg:col-span-2' : 'lg:col-span-5'}`}>
          {/* ── Contratos por vencer ── */}
          {(verInq || can('contratos')) && (
            <StaggerItem>
              <Card className="p-0">
                <div className="flex items-center justify-between px-5 pt-5">
                  <div>
                    <h2 className="flex items-center gap-2 font-bold"><FileText className="size-4 text-warning" />Contratos por vencer</h2>
                    <p className="text-xs text-muted">Vencidos y los de los próximos 60 días</p>
                  </div>
                  {can('contratos') && <Link to="/contratos" className="flex items-center gap-1 text-xs font-semibold text-primary hover:underline">Contratos <ArrowRight className="size-3.5" /></Link>}
                </div>
                {(inquilinos.isLoading || resumen.isLoading) && <div className="grid place-items-center py-8"><Spinner /></div>}
                {!inquilinos.isLoading && !resumen.isLoading && contratos.length === 0 && <p className="px-5 py-8 text-center text-sm text-muted">Ningún contrato vence pronto ✅</p>}
                <ul className="mt-2 divide-y divide-line px-2 pb-3">
                  {contratos.slice(0, 6).map(c => (
                    <li key={c.id}>
                      <Link to={`/inquilinos/${c.id}`} className="flex items-center gap-3 rounded-xl px-3 py-2.5 transition hover:bg-hover/50">
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm font-semibold">{c.nombre}</span>
                          <span className="text-xs text-muted">Depto {c.depto} · {fecha(c.termino)}</span>
                        </span>
                        <Badge tone={c.dias < 0 ? 'danger' : c.dias <= 30 ? 'warning' : 'neutral'}>
                          {c.dias < 0 ? `Venció hace ${-c.dias} d` : c.dias === 0 ? 'Vence hoy' : `Faltan ${c.dias} d`}
                        </Badge>
                      </Link>
                    </li>
                  ))}
                </ul>
              </Card>
            </StaggerItem>
          )}

          {/* ── Próximos pagos ── */}
          {verCobranza && proximos.length > 0 && (
            <StaggerItem>
              <Card className="p-0">
                <div className="px-5 pt-5">
                  <h2 className="flex items-center gap-2 font-bold"><Wallet className="size-4 text-primary" />Próximos pagos</h2>
                  <p className="text-xs text-muted">Rentas que vencen en los próximos 7 días</p>
                </div>
                <ul className="mt-2 divide-y divide-line px-2 pb-3">
                  {proximos.slice(0, 5).map(f => (
                    <li key={f.id}>
                      <Link to={`/pagos/${f.id}`} className="flex items-center gap-3 rounded-xl px-3 py-2.5 transition hover:bg-hover/50">
                        <span className="min-w-0 flex-1"><span className="block truncate text-sm font-semibold">{f.nombre}</span><span className="text-xs text-muted">Depto {f.depto}</span></span>
                        <span className="text-right"><span className="block text-sm font-bold">{money(f.renta)}</span><span className="text-[11px] font-semibold text-muted">{f.en === 0 ? 'Hoy' : f.en === 1 ? 'Mañana' : `En ${f.en} días`}</span></span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </Card>
            </StaggerItem>
          )}

          {/* ── Tickets abiertos ── */}
          {can('tickets') && usaApp && abiertos.length > 0 && (
            <StaggerItem>
              <Card className="p-0">
                <div className="flex items-center justify-between px-5 pt-5">
                  <h2 className="flex items-center gap-2 font-bold"><MessageSquare className="size-4 text-danger" />Tickets abiertos <Badge tone="danger">{abiertos.length}</Badge></h2>
                  <Link to="/tickets" className="flex items-center gap-1 text-xs font-semibold text-primary hover:underline">Ver <ArrowRight className="size-3.5" /></Link>
                </div>
                <ul className="mt-2 divide-y divide-line px-2 pb-3">
                  {abiertos.slice(0, 4).map(t => (
                    <li key={t.id} className="px-3 py-2.5">
                      <p className="truncate text-sm font-semibold">{t.titulo}</p>
                      <p className="text-xs text-muted">{t.inquilino_nombre ?? 'Inquilino'}{t.depto_numero ? ` · Depto ${t.depto_numero}` : ''}</p>
                    </li>
                  ))}
                </ul>
              </Card>
            </StaggerItem>
          )}
        </div>
      </div>
    </StaggerList>
  )
}

function Kpi({ icon, tone, label, value, hint, extra, valueClass = '' }: {
  icon: ReactNode; tone: string; label: string; value: ReactNode | null; hint?: string; extra?: ReactNode; valueClass?: string
}) {
  return (
    <Card className="h-full p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md">
      <span className="mb-3 grid size-9 place-items-center rounded-xl text-white shadow-sm [&>svg]:size-[18px]" style={{ background: tone }}>{icon}</span>
      <p className="text-xs font-semibold text-fg-2">{label}</p>
      {value === null ? <div className="mt-1 h-7 w-24 animate-pulse rounded-md bg-hover" /> : <p className={`mt-0.5 text-xl font-extrabold tracking-tight ${valueClass}`}>{value}</p>}
      {hint && <p className="text-[11px] text-muted">{hint}</p>}
      {extra}
    </Card>
  )
}
