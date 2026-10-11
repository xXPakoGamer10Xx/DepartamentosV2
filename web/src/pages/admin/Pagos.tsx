import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowUpDown, Banknote, CheckCircle2, ChevronDown, ClipboardCheck, Landmark, Percent, QrCode, Search, Undo2, Wallet, X } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EmptyState } from '../../components/ui/EmptyState'
import { StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { PageHeader } from '../../components/ui/PageHeader'
import { ProgressBar } from '../../components/ui/ProgressBar'
import { Spinner } from '../../components/ui/Spinner'
import { Stat } from '../../components/ui/Stat'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { diaPago, EPS, puedeCancelar, rowState, toSaldo, type EstadoActual, type RowState, type Saldo } from '../../lib/cobranza'
import { iniciales, money, num, periodoLabel } from '../../lib/format'
import { useSSE } from '../../lib/sse'
import type { Inquilino } from '../../lib/types'

type Tab = 'todos' | 'pagados' | 'revision' | 'pendientes' | 'atrasados' | 'cuotas_extra' | 'deposito'
type Sort = 'depto' | 'dia_asc' | 'dia_desc' | 'monto_desc' | 'monto_asc' | 'nombre'
const SORTS: { key: Sort; label: string }[] = [
  { key: 'depto', label: 'Número de departamento' }, { key: 'dia_asc', label: 'Día de pago — del 1 al 31' },
  { key: 'dia_desc', label: 'Día de pago — del 31 al 1' }, { key: 'monto_desc', label: 'Renta — de mayor a menor' },
  { key: 'monto_asc', label: 'Renta — de menor a mayor' }, { key: 'nombre', label: 'Nombre (A–Z)' },
]
const CHIP: Record<RowState, { label: string; tone: 'success' | 'warning' | 'danger' | 'neutral' }> = {
  pagado: { label: 'Pagado', tone: 'success' }, revision: { label: 'En revisión', tone: 'warning' },
  atrasado: { label: 'Atrasado', tone: 'danger' }, pendiente: { label: 'Por pagar', tone: 'neutral' },
}
const tabColor: Partial<Record<Tab, string>> = { revision: 'text-warning', atrasados: 'text-danger', cuotas_extra: 'text-warning', deposito: 'text-primary' }

export default function Pagos() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const puedeMarcar = can('pagos.marcar')
  const [tab, setTab] = useState<Tab>('todos')
  const [sort, setSort] = useState<Sort>('depto')
  const [search, setSearch] = useState('')
  const [cancelar, setCancelar] = useState<{ pagoId: string; nombre: string } | null>(null)
  const [undo, setUndo] = useState<{ pagoId: string; nombre: string } | null>(null)
  const undoTimer = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(() => () => { if (undoTimer.current) clearTimeout(undoTimer.current) }, [])

  const refetch = 15_000
  const inquilinos = useQuery({ queryKey: ['inquilinos', 'activo'], queryFn: async () => (await api.getInquilinos({ estado: 'activo' })).data, refetchInterval: refetch })
  const estadosQ = useQuery({ queryKey: ['estados-actuales'], queryFn: async () => (await api.getEstadosActuales()).data, refetchInterval: refetch })
  const saldosQ = useQuery({ queryKey: ['saldos'], queryFn: async () => (await api.getSaldos()).data, refetchInterval: refetch })
  const config = useQuery({ queryKey: ['config'], queryFn: async () => (await api.getConfig()).data })
  const usaQr = config.data?.usa_qr_inquilinos !== 'false'

  const invalidar = () => { for (const k of ['estados-actuales', 'saldos', 'resumen-deuda', 'inquilinos']) void qc.invalidateQueries({ queryKey: [k] }) }
  useSSE('payment_confirmed', invalidar)
  useSSE('comprobante_subido', invalidar)

  const estados = useMemo(() => new Map<string, EstadoActual>((estadosQ.data ?? []).map(e => [e.inquilino_id, e])), [estadosQ.data])
  const saldos = useMemo(() => new Map<string, Saldo>((saldosQ.data ?? []).map(s => [s.inquilino_id, toSaldo(s)])), [saldosQ.data])
  const lista = inquilinos.data ?? []

  const rows = useMemo(() => lista.map(i => ({ i, state: rowState(estados.get(i.id), saldos.get(i.id)) })), [lista, estados, saldos])
  const counts = useMemo(() => ({
    todos: rows.length,
    pagados: rows.filter(r => r.state === 'pagado').length,
    revision: rows.filter(r => r.state === 'revision').length,
    pendientes: rows.filter(r => r.state === 'pendiente' || r.state === 'atrasado').length,
    atrasados: rows.filter(r => r.state === 'atrasado').length,
    cuotas_extra: rows.filter(r => (saldos.get(r.i.id)?.cuotasExtra ?? 0) > EPS).length,
    deposito: rows.filter(r => (saldos.get(r.i.id)?.deposito ?? 0) > EPS).length,
  }), [rows, saldos])

  const kpi = useMemo(() => {
    const esperado = lista.reduce((a, i) => a + num(i.renta), 0)
    const recaudado = rows.filter(r => r.state === 'pagado').reduce((a, r) => a + num(r.i.renta), 0)
    const vencido = lista.reduce((a, i) => a + (saldos.get(i.id)?.vencida ?? 0), 0)
    const sum = (f: (s: Saldo) => number) => lista.reduce((a, i) => a + f(saldos.get(i.id) ?? { total: 0, vencida: 0, periodoVencido: null, cuotasExtra: 0, deposito: 0 }), 0)
    let valor = 0, label = 'Por recaudar total', sub = ''
    if (tab === 'pagados' || tab === 'pendientes') { valor = rows.filter(r => r.state === 'pendiente' || r.state === 'atrasado').reduce((a, r) => a + num(r.i.renta), 0); label = 'Rentas por cobrar' }
    else if (tab === 'atrasados') { valor = vencido; label = 'Dinero atrasado'; sub = `${counts.atrasados} inquilino(s) con atraso` }
    else if (tab === 'cuotas_extra') { valor = sum(s => s.cuotasExtra); label = 'Cuotas extra'; sub = `${counts.cuotas_extra} inquilino(s) pendiente(s)` }
    else if (tab === 'deposito') { valor = sum(s => s.deposito); label = 'Falta depósito'; sub = `${counts.deposito} inquilino(s) sin depósito` }
    else {
      valor = sum(s => s.total + s.deposito)
      sub = vencido > EPS ? `${money(vencido)} vencido${counts.pendientes > counts.atrasados ? ` · ${counts.pendientes - counts.atrasados} por vencer` : ''}`
        : counts.pendientes > 0 ? `${counts.pendientes} por pagar · al corriente` : 'Todo cobrado'
    }
    const spei = lista.filter(i => i.metodo_pago === 'transferencia' || i.metodo_pago === 'ambos').length
    return { esperado, recaudado, pct: esperado > 0 ? Math.round((recaudado / esperado) * 100) : 0, valor, label, sub, speiPct: lista.length ? Math.round((spei / lista.length) * 100) : 0 }
  }, [lista, rows, saldos, tab, counts])

  const visibles = useMemo(() => {
    const q = search.trim().toLowerCase()
    const f = rows.filter(({ i, state }) => {
      if (tab === 'pagados' && state !== 'pagado') return false
      if (tab === 'revision' && state !== 'revision') return false
      if (tab === 'pendientes' && state !== 'pendiente' && state !== 'atrasado') return false
      if (tab === 'atrasados' && state !== 'atrasado') return false
      if (tab === 'cuotas_extra' && (saldos.get(i.id)?.cuotasExtra ?? 0) <= EPS) return false
      if (tab === 'deposito' && (saldos.get(i.id)?.deposito ?? 0) <= EPS) return false
      return !q || i.nombre_completo?.toLowerCase().includes(q) || String(i.depto_numero).includes(q)
    })
    const cmp: Record<Sort, (a: Inquilino, b: Inquilino) => number> = {
      depto: (a, b) => a.depto_numero - b.depto_numero,
      dia_asc: (a, b) => (diaPago(a.fecha_pago) ?? 99) - (diaPago(b.fecha_pago) ?? 99),
      dia_desc: (a, b) => (diaPago(b.fecha_pago) ?? -1) - (diaPago(a.fecha_pago) ?? -1),
      monto_desc: (a, b) => num(b.renta) - num(a.renta), monto_asc: (a, b) => num(a.renta) - num(b.renta),
      nombre: (a, b) => (a.nombre_completo || '').localeCompare(b.nombre_completo || ''),
    }
    return [...f].sort((a, b) => cmp[sort](a.i, b.i))
  }, [rows, tab, search, sort, saldos])

  const marcar = useMutation({
    mutationFn: (i: Inquilino) => api.marcarPagado(i.id),
    onSuccess: (res, i) => {
      invalidar()
      const pagoId = res.data?.id
      if (pagoId) {
        if (undoTimer.current) clearTimeout(undoTimer.current)
        setUndo({ pagoId, nombre: i.nombre_completo })
        undoTimer.current = setTimeout(() => setUndo(null), 5000)
      }
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo marcar el pago', 'error'),
  })
  const validar = useMutation({
    mutationFn: (pagoId: string) => api.confirmarPagoAdmin(pagoId),
    onSuccess: () => { toast('Pago validado', 'success'); invalidar() },
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo validar el pago', 'error'),
  })
  const cancelarPago = useMutation({
    mutationFn: (pagoId: string) => api.cancelarPago(pagoId),
    onSuccess: () => { setCancelar(null); setUndo(null); toast('Pago cancelado', 'success'); invalidar() },
  })

  const mes = periodoLabel(new Date().toISOString().slice(0, 7))
  const loading = inquilinos.isLoading || estadosQ.isLoading

  const TABS: { key: Tab; label: string }[] = [
    { key: 'todos', label: 'Todos' }, { key: 'pagados', label: 'Pagados' },
    ...(usaQr ? [{ key: 'revision' as Tab, label: 'Revisión' }] : []),
    { key: 'pendientes', label: 'Por pagar' }, { key: 'atrasados', label: 'Atrasados' },
    { key: 'cuotas_extra', label: 'Cuotas extra' }, { key: 'deposito', label: 'Falta depósito' },
  ]

  return (
    <StaggerList className="space-y-6">
      <PageHeader title="Pagos y cobranza" subtitle={<span className="inline-block first-letter:uppercase">{mes}</span>} />

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StaggerItem className="h-full">
          <Card className="flex h-full flex-col justify-between p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md">
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wide text-muted">Recaudado del mes</p>
              <p className="mt-1 text-2xl font-extrabold tracking-tight text-success"><AnimatedNumber value={kpi.recaudado} format={money} /></p>
            </div>
            <div className="mt-2">
              <ProgressBar value={kpi.pct} />
              <p className="mt-1 text-xs text-fg-2">{kpi.pct}% de {money(kpi.esperado)}</p>
            </div>
          </Card>
        </StaggerItem>
        <StaggerItem className="h-full">
          <Stat label={kpi.label} numericValue={kpi.valor} format={money} sub={kpi.sub} icon={<Wallet />} tone={kpi.valor > EPS ? 'text-danger' : ''} />
        </StaggerItem>
        {usaQr && (
          <StaggerItem className="h-full">
            <Stat label="Por validar" numericValue={counts.revision} sub={counts.revision ? 'pendiente de aprobación' : 'todo revisado'} icon={<ClipboardCheck />} tone={counts.revision ? 'text-warning' : ''} />
          </StaggerItem>
        )}
        {usaQr && (
          <StaggerItem className="h-full">
            <Stat label="Pagan por SPEI" numericValue={kpi.speiPct} format={(n) => `${Math.round(n)}%`} sub="de los inquilinos activos" icon={<Percent />} />
          </StaggerItem>
        )}
      </div>

      <StaggerItem className="flex flex-wrap items-center gap-3">
        <div className="relative min-w-60 flex-1">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Buscar por inquilino o unidad…" aria-label="Buscar pagos"
            className="h-11 w-full rounded-xl border border-line bg-card pl-10 pr-9 text-sm placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25" />
          {search && <button onClick={() => setSearch('')} aria-label="Limpiar" className="absolute right-3 top-1/2 -translate-y-1/2 text-muted hover:text-fg"><X className="size-4" /></button>}
        </div>
        <label className="relative inline-flex items-center">
          <span className="sr-only">Ordenar por</span>
          <ArrowUpDown className="pointer-events-none absolute left-3.5 size-4 text-muted" />
          <select value={sort} onChange={e => setSort(e.target.value as Sort)} className="h-11 cursor-pointer appearance-none rounded-xl border border-line bg-card pl-10 pr-10 text-sm font-semibold transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25">
            {SORTS.map(s => <option key={s.key} value={s.key}>{s.label}</option>)}
          </select>
          <ChevronDown className="pointer-events-none absolute right-3.5 size-4 text-fg-2" />
        </label>
      </StaggerItem>

      <StaggerItem className="flex flex-wrap gap-1.5 sm:gap-2">
        {TABS.map(t => {
          const active = tab === t.key
          return (
            <button key={t.key} onClick={() => setTab(t.key)}
              className={`relative shrink-0 rounded-full border px-3 py-1.5 text-xs font-semibold transition-colors sm:px-4 sm:text-sm ${active ? `border-transparent ${tabColor[t.key] ?? 'text-fg'}` : 'border-transparent text-fg-2 hover:bg-hover'}`}>
              {active && (
                <motion.span
                  layoutId="pagos-tab-pill"
                  transition={{ type: 'spring', stiffness: 400, damping: 36, bounce: 0 }}
                  className="absolute inset-0 rounded-full border border-line-strong bg-hover"
                />
              )}
              <span className="relative z-10">{t.label} ({counts[t.key]})</span>
            </button>
          )
        })}
      </StaggerItem>

      {loading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}
      {!loading && visibles.length === 0 && (
        <StaggerItem>
          <Card><EmptyState icon={Banknote} title="Sin resultados">{search ? `No se encontró “${search}”` : 'No hay inquilinos en esta categoría.'}</EmptyState></Card>
        </StaggerItem>
      )}

      {visibles.length > 0 && (
        <StaggerList key={`${tab}-${sort}`} className="space-y-3">
        {visibles.map(({ i, state }) => {
          const e = estados.get(i.id); const s = saldos.get(i.id); const chip = CHIP[state]
          const transfer = i.metodo_pago === 'transferencia' || i.metodo_pago === 'ambos'
          const busy = (marcar.isPending && marcar.variables?.id === i.id) || (validar.isPending && validar.variables === e?.pago_id)
          return (
            <StaggerItem key={i.id} layout>
              <Card className="flex flex-wrap items-center gap-x-5 gap-y-3 p-4 transition duration-200 hover:border-line-strong hover:shadow-md">
                <Link to={`/pagos/${i.id}`} className="flex min-w-56 flex-1 items-center gap-3">
                  <span className="grid size-11 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-bold text-primary">{iniciales(i.nombre_completo)}</span>
                  <span className="min-w-0">
                    <span className="block truncate font-bold">{i.nombre_completo}</span>
                    <span className="text-xs text-fg-2">Depto {i.depto_numero} · día {diaPago(i.fecha_pago) ?? '—'} · {transfer ? (usaQr ? 'SPEI' : 'Transferencia') : usaQr ? <><QrCode className="mr-0.5 inline size-3" />Efectivo</> : 'Efectivo'}</span>
                  </span>
                </Link>

                <div className="min-w-28 text-right">
                  <p className="font-extrabold">{money(i.renta)}</p>
                  {state === 'atrasado' && s && s.vencida > EPS && <p className="text-xs font-semibold text-danger">Debe {money(s.vencida)}{s.periodoVencido ? ` · desde ${periodoLabel(s.periodoVencido)}` : ''}</p>}
                  {tab === 'cuotas_extra' && s && <p className="text-xs font-semibold text-warning">Extras {money(s.cuotasExtra)}</p>}
                  {tab === 'deposito' && s && <p className="text-xs font-semibold text-primary">Falta {money(s.deposito)}</p>}
                  {state === 'pendiente' && num(e?.total_abonado) > 0 && <p className="text-xs text-fg-2">Abonado {money(e?.total_abonado)}</p>}
                </div>

                <div className="w-28 text-center"><Badge tone={chip.tone}>{chip.label}</Badge></div>

                <div className="flex min-w-44 justify-end gap-2">
                  {puedeMarcar && state === 'revision' && e?.pago_id && <Button className="h-9 px-3" loading={busy} icon={<CheckCircle2 className="size-4" />} onClick={() => validar.mutate(e.pago_id!)}>Validar</Button>}
                  {puedeMarcar && (state === 'pendiente' || state === 'atrasado') && <Button variant="secondary" className="h-9 px-3" loading={busy} icon={<Banknote className="size-4" />} onClick={() => marcar.mutate(i)}>Marcar pagado</Button>}
                  {puedeMarcar && state === 'pagado' && puedeCancelar(e) && <Button variant="ghost" className="h-9 px-3 text-danger" icon={<Undo2 className="size-4" />} onClick={() => { cancelarPago.reset(); setCancelar({ pagoId: e!.pago_id!, nombre: i.nombre_completo }) }}>Cancelar</Button>}
                  <Link to={`/pagos/${i.id}`}><Button variant="ghost" className="h-9 px-3" icon={<Landmark className="size-4" />}>Detalle</Button></Link>
                </div>
              </Card>
            </StaggerItem>
          )
        })}
        </StaggerList>
      )}

      <AnimatePresence>
        {undo && (
          <motion.div
            initial={{ opacity: 0, y: 24, scale: 0.94 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 20, scale: 0.94 }}
            transition={{ type: 'spring', stiffness: 380, damping: 28 }}
            className="safe-bottom fixed inset-x-0 bottom-20 z-[80] flex justify-center px-4 lg:bottom-6"
          >
            <div className="flex items-center gap-4 rounded-2xl border border-line bg-elevated px-5 py-3 shadow-2xl">
              <span className="text-sm">Pago de <b>{undo.nombre}</b> registrado</span>
              <button onClick={() => cancelarPago.mutate(undo.pagoId)} className="inline-flex items-center gap-1.5 text-sm font-bold text-primary transition hover:underline active:scale-95"><Undo2 className="size-4" /> Deshacer</button>
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      <ConfirmDialog open={!!cancelar} title="Cancelar pago" confirmLabel="Cancelar pago" loading={cancelarPago.isPending}
        error={cancelarPago.error instanceof Error ? cancelarPago.error.message : null} onClose={() => setCancelar(null)} onConfirm={() => cancelar && cancelarPago.mutate(cancelar.pagoId)}>
        <p>El periodo de <b className="text-fg">{cancelar?.nombre}</b> volverá a quedar como pendiente de pago.</p>
      </ConfirmDialog>
    </StaggerList>
  )
}

