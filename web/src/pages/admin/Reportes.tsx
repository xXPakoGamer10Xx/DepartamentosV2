import { useQuery } from '@tanstack/react-query'
import { Banknote, Building2, Calendar, ChevronDown, Receipt, TrendingUp, Wallet } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useState } from 'react'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Card } from '../../components/ui/Card'
import { StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { PageHeader } from '../../components/ui/PageHeader'
import { Spinner } from '../../components/ui/Spinner'
import { Stat } from '../../components/ui/Stat'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { num } from '../../lib/format'

const MESES = ['Ene', 'Feb', 'Mar', 'Abr', 'May', 'Jun', 'Jul', 'Ago', 'Sep', 'Oct', 'Nov', 'Dic']
const COLORS = { renta: '#10B981', extra: '#3B82F6', deposito: '#F59E0B' }
const money0 = (n: number) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN', maximumFractionDigits: 0 }).format(n || 0)
const k = (n: number) => (n >= 1000 ? `${(n / 1000).toFixed(1).replace('.0', '')}k` : String(Math.round(n)))

export default function Reportes() {
  const { can } = useAuth()
  const anioActual = new Date().getFullYear()
  const [year, setYear] = useState(anioActual)
  const [hover, setHover] = useState<number | null>(null)

  const anual = useQuery({ queryKey: ['reporte-anual', year], queryFn: async () => (await api.getReporteAnual(year)).data })
  const mensual = useQuery({ queryKey: ['reporte-mensual', year], queryFn: async () => (await api.getReporteMensual(year)).data })
  const stats = useQuery({ queryKey: ['deptos-stats'], queryFn: async () => (await api.getDepartamentosStats()).data, enabled: can('departamentos') })
  const deuda = useQuery({ queryKey: ['resumen-deuda'], queryFn: async () => (await api.getResumenDeuda()).data })

  const meses = mensual.data?.meses?.length ? mensual.data.meses : MESES.map((_, i) => ({ mes: i + 1, renta: 0, extra: 0, deposito: 0, total: 0 }))
  const porDepto = mensual.data?.por_departamento ?? []
  const anios = [...new Set([anioActual, ...(mensual.data?.anios_disponibles ?? [])])].sort((a, b) => b - a)
  const maxMes = Math.max(1, ...meses.map(m => m.total))
  const total = anual.data?.total_general ?? 0
  const pct = (v: number) => (total > 0 ? Math.round((v / total) * 100) : 0)
  const rentaPct = pct(anual.data?.renta_total ?? 0)
  const extraPct = pct(anual.data?.extra_total ?? 0)
  const depoPct = total > 0 && (anual.data?.deposito_total ?? 0) > 0 ? Math.max(0, 100 - rentaPct - extraPct) : 0
  const ocup = num(stats.data?.ocupados), tot = num(stats.data?.total)
  const ocupPct = tot > 0 ? Math.round((ocup / tot) * 100) : 0
  const cargando = anual.isLoading || mensual.isLoading
  const mejor = meses.reduce((a, m) => (m.total > a.total ? m : a), meses[0])

  return (
    <StaggerList className="space-y-6">
      <PageHeader title="Reportes" subtitle="Ingreso real, contabilizado por la fecha en que entró el dinero"
        actions={
          <div className="relative inline-flex items-center">
            <Calendar className="pointer-events-none absolute left-3.5 size-4 text-primary" />
            <select
              value={year}
              onChange={e => setYear(Number(e.target.value))}
              aria-label="Año"
              className="h-11 cursor-pointer appearance-none rounded-xl border border-line bg-card pl-10 pr-10 text-sm font-bold text-fg shadow-xs transition hover:border-line-strong hover:bg-hover/60 focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25"
            >
              {anios.map(a => <option key={a} value={a}>{a}</option>)}
            </select>
            <ChevronDown className="pointer-events-none absolute right-3.5 size-4 text-fg-2" />
          </div>} />

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StaggerItem className="h-full"><Stat label={`Ingreso ${year}`} numericValue={total} format={money0} icon={<TrendingUp />} tone="text-success" sub={mejor && mejor.total > 0 ? `Mejor mes: ${MESES[mejor.mes - 1]} (${money0(mejor.total)})` : 'Total acumulado del año'} /></StaggerItem>
        <StaggerItem className="h-full"><Stat label="Renta" numericValue={anual.data?.renta_total ?? 0} format={money0} icon={<Banknote />} sub={`${rentaPct}% del total`} /></StaggerItem>
        <StaggerItem className="h-full"><Stat label="Cuotas extra" numericValue={anual.data?.extra_total ?? 0} format={money0} icon={<Receipt />} sub={`${extraPct}% del total`} /></StaggerItem>
        <StaggerItem className="h-full"><Stat label="Depósitos" numericValue={anual.data?.deposito_total ?? 0} format={money0} icon={<Wallet />} sub={`${depoPct}% del total`} /></StaggerItem>
      </div>

      {cargando && <div className="grid place-items-center py-16"><Spinner className="size-8" /></div>}

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        {/* Gráfica de barras apiladas */}
        <StaggerItem className="min-w-0">
          <Card className="h-full">
            <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
              <h2 className="font-bold">Ingresos por mes</h2>
              <div className="flex gap-4 text-xs text-fg-2">{(['renta', 'extra', 'deposito'] as const).map(c => <span key={c} className="flex items-center gap-1.5 capitalize"><i className="size-2.5 rounded-sm" style={{ background: COLORS[c] }} />{c === 'extra' ? 'Cuotas extra' : c === 'deposito' ? 'Depósitos' : 'Renta'}</span>)}</div>
            </div>
            <div className="flex gap-3">
              <div className="flex h-60 flex-col justify-between pb-0 text-right text-[11px] text-muted" aria-hidden>
                {[1, 0.66, 0.33, 0].map(f => <span key={f}>{k(maxMes * f)}</span>)}
              </div>
              <div className="relative min-w-0 flex-1">
                <div className="absolute inset-x-0 top-0 flex h-60 flex-col justify-between" aria-hidden>{[0, 1, 2, 3].map(i => <div key={i} className="border-t border-dashed border-line" />)}</div>
                <div className="relative flex h-60 items-end gap-1.5 sm:gap-2.5" role="img" aria-label={`Ingresos mensuales de ${year}`}>
                  {meses.map((m, i) => (
                    <button key={`${year}-${m.mes}`} type="button" onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(h => (h === i ? null : h))} onClick={() => setHover(h => (h === i ? null : i))}
                      aria-label={`${MESES[i]}: ${money0(m.total)}`}
                      className="group relative flex h-full flex-1 flex-col justify-end">
                      {(['deposito', 'extra', 'renta'] as const).map((c, idx) => m[c] > 0 && (
                        <motion.div
                          key={c}
                          initial={{ height: 0 }}
                          animate={{
                            height: `${(m[c] / maxMes) * 100}%`,
                            opacity: hover !== null && hover !== i ? 0.45 : 1,
                          }}
                          transition={{
                            height: { type: 'spring', stiffness: 150, damping: 22, delay: i * 0.025 },
                            opacity: { duration: 0.15 },
                          }}
                          style={{ background: COLORS[c] }}
                          className={idx === 0 ? 'rounded-t' : ''}
                        />
                      ))}
                      {m.total === 0 && <div className="h-0.5 rounded bg-line" />}
                    </button>
                  ))}
                </div>
                <AnimatePresence>
                  {hover !== null && (() => {
                    const m = meses[hover]
                    return (
                      <motion.div
                        key="tooltip"
                        initial={{ opacity: 0, y: 6, scale: 0.95 }}
                        animate={{ opacity: 1, y: 0, scale: 1, left: `${((hover + 0.5) / meses.length) * 100}%` }}
                        exit={{ opacity: 0, y: 4, scale: 0.95 }}
                        transition={{ type: 'spring', stiffness: 380, damping: 28 }}
                        className="pointer-events-none absolute top-0 z-10 w-44 -translate-x-1/2 rounded-xl border border-line bg-elevated p-3 text-xs shadow-xl"
                      >
                        <p className="mb-1 font-bold">{MESES[hover]} {year}</p>
                        {m.renta > 0 && <p className="flex justify-between"><span style={{ color: COLORS.renta }}>Renta</span><b>{money0(m.renta)}</b></p>}
                        {m.extra > 0 && <p className="flex justify-between"><span style={{ color: COLORS.extra }}>Extras</span><b>{money0(m.extra)}</b></p>}
                        {m.deposito > 0 && <p className="flex justify-between"><span style={{ color: COLORS.deposito }}>Depósitos</span><b>{money0(m.deposito)}</b></p>}
                        <p className="mt-1 flex justify-between border-t border-line pt-1"><span>Total</span><b>{money0(m.total)}</b></p>
                      </motion.div>
                    )
                  })()}
                </AnimatePresence>
                <div className="mt-2 flex gap-1.5 sm:gap-2.5 text-center text-[11px] text-muted">{MESES.map(m => <span key={m} className="flex-1">{m}</span>)}</div>
              </div>
            </div>
          </Card>
        </StaggerItem>

        {/* Panel lateral */}
        <div className="min-w-0 space-y-5">
          {can('departamentos') && (
            <StaggerItem>
              <Card className="flex items-center gap-5">
                <svg viewBox="0 0 120 120" className="size-28 shrink-0 -rotate-90" role="img" aria-label={`Ocupación ${ocupPct}%`}>
                  <circle cx="60" cy="60" r="50" fill="none" stroke="var(--hover)" strokeWidth="12" />
                  <motion.circle
                    cx="60"
                    cy="60"
                    r="50"
                    fill="none"
                    stroke="var(--success)"
                    strokeWidth="12"
                    strokeLinecap="round"
                    initial={{ strokeDasharray: '0 314' }}
                    animate={{ strokeDasharray: `${(ocupPct / 100) * 314} 314` }}
                    transition={{ duration: 0.9, ease: [0.22, 1, 0.36, 1] }}
                  />
                </svg>
                <div>
                  <p className="text-3xl font-extrabold"><AnimatedNumber value={ocupPct} format={(n) => `${Math.round(n)}%`} /></p>
                  <p className="text-sm font-semibold">Ocupación</p>
                  <p className="flex items-center gap-1 text-xs text-fg-2"><Building2 className="size-3.5" /> {ocup} de {tot} unidades</p>
                </div>
              </Card>
            </StaggerItem>
          )}
          <StaggerItem>
            <Card>
              <p className="text-xs font-bold uppercase tracking-wide text-muted">Adeudo acumulado</p>
              <p className={`mt-1 text-2xl font-extrabold ${num(deuda.data?.total_general) > 0 ? 'text-danger' : 'text-success'}`}>
                <AnimatedNumber value={num(deuda.data?.total_general)} format={money0} />
              </p>
              <p className="text-xs text-fg-2">{num(deuda.data?.total_general) > 0 ? 'Saldo vencido por cobrar' : 'Sin adeudos vencidos'}</p>
            </Card>
          </StaggerItem>
          <StaggerItem>
            <Card className="space-y-3">
              <p className="text-xs font-bold uppercase tracking-wide text-muted">Composición del ingreso</p>
              {([['Renta base', rentaPct, COLORS.renta, anual.data?.renta_total], ['Cuotas extra', extraPct, COLORS.extra, anual.data?.extra_total], ['Depósitos', depoPct, COLORS.deposito, anual.data?.deposito_total]] as const).map(([l, p, c, v]) => (
                <div key={l}>
                  <div className="mb-1 flex justify-between text-sm"><span>{l}</span><span className="font-bold">{money0(v ?? 0)} <span className="font-normal text-muted">· {p}%</span></span></div>
                  <div className="h-2 overflow-hidden rounded-full bg-hover">
                    <motion.div
                      initial={{ width: 0 }}
                      animate={{ width: `${p}%` }}
                      transition={{ type: 'spring', stiffness: 120, damping: 22 }}
                      className="h-full rounded-full"
                      style={{ background: c }}
                    />
                  </div>
                </div>
              ))}
            </Card>
          </StaggerItem>
        </div>
      </div>

      {/* Matriz departamento × mes */}
      {porDepto.length > 0 && (
        <StaggerItem>
          <Card className="overflow-hidden p-0">
            <h2 className="p-5 pb-3 font-bold">Ingreso por departamento</h2>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[820px] text-right text-sm">
                <thead className="bg-hover/50 text-[11px] font-bold uppercase tracking-wide text-muted">
                  <tr><th className="sticky left-0 bg-card px-5 py-2.5 text-left">Depto</th>{MESES.map(m => <th key={m} className="px-2 py-2.5">{m}</th>)}<th className="px-5 py-2.5">Total</th></tr>
                </thead>
                <tbody className="divide-y divide-line">
                  {porDepto.map(d => (
                    <tr key={d.depto_numero} className="transition hover:bg-hover/40">
                      <td className="sticky left-0 bg-card px-5 py-2.5 text-left font-bold">{d.depto_numero}</td>
                      {d.meses.map((v, i) => <td key={i} className={`px-2 py-2.5 ${v > 0 ? '' : 'text-muted'}`}>{v > 0 ? k(v) : '·'}</td>)}
                      <td className="px-5 py-2.5 font-extrabold">{money0(d.total)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t-2 border-line-strong bg-hover/50 font-extrabold">
                  <tr><td className="sticky left-0 bg-card px-5 py-2.5 text-left">Total</td>{meses.map((m, i) => <td key={i} className="px-2 py-2.5">{m.total > 0 ? k(m.total) : '·'}</td>)}<td className="px-5 py-2.5">{money0(mensual.data?.total_anual ?? 0)}</td></tr>
                </tfoot>
              </table>
            </div>
          </Card>
        </StaggerItem>
      )}
      {!cargando && porDepto.length === 0 && total === 0 && (
        <StaggerItem>
          <Card className="py-12 text-center text-sm text-muted">Aún no hay ingresos registrados en {year}.</Card>
        </StaggerItem>
      )}
    </StaggerList>
  )
}

