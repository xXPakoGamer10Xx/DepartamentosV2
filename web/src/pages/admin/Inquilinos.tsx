import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Eye, Pencil, Plus, RotateCcw, Search, UserMinus, Users, X } from 'lucide-react'
import { motion } from 'motion/react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EmptyState } from '../../components/ui/EmptyState'
import { StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { PageHeader } from '../../components/ui/PageHeader'
import { Segmented } from '../../components/ui/Segmented'
import { Spinner } from '../../components/ui/Spinner'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { iniciales, money, num } from '../../lib/format'
import type { Inquilino } from '../../lib/types'
import { useEstadosPago } from '../../lib/estadoPago'
import type { RowState } from '../../lib/cobranza'
import { nombreVisible } from '../../lib/nombre'

type Tone = 'success' | 'warning' | 'danger' | 'primary' | 'neutral'

export function vencimiento(fecha?: string | null) {
  if (!fecha) return null
  const dias = Math.ceil((new Date(fecha).getTime() - Date.now()) / 86_400_000)
  if (dias < 0) return { expired: true, label: 'Contrato vencido', tone: 'danger' as Tone, short: null as string | null }
  if (dias <= 30) return { expired: false, label: `Vence en ${dias} días`, tone: 'warning' as Tone, short: `Vence en ${dias}d` }
  const meses = Math.round(dias / 30)
  return { expired: false, label: `Vence en ${meses} mes${meses !== 1 ? 'es' : ''}`, tone: 'success' as Tone, short: null }
}

const PAGO_CHIP: Record<RowState, { label: string; tone: Tone }> = {
  pagado: { label: 'Pagado', tone: 'success' }, revision: { label: 'En revisión', tone: 'warning' },
  atrasado: { label: 'Atrasado', tone: 'danger' }, pendiente: { label: 'Por pagar', tone: 'neutral' },
}

/** Etiqueta de estado: el estado de PAGO del mes si se conoce; si no, solo "Activo" (no se afirma que esté al corriente). */
export function estadoChip(i: Pick<Inquilino, 'estado' | 'fecha_termino'>, pago?: RowState): { label: string; tone: Tone } {
  if (i.estado !== 'activo') return { label: 'Archivado', tone: 'danger' }
  if (pago) return PAGO_CHIP[pago]
  if (vencimiento(i.fecha_termino)?.expired) return { label: 'Contrato vencido', tone: 'danger' }
  return { label: 'Activo', tone: 'neutral' }
}

export default function Inquilinos() {
  const { can } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const [estado, setEstado] = useState<'activo' | 'inactivo'>('activo')
  const [search, setSearch] = useState('')
  const [baja, setBaja] = useState<Inquilino | null>(null)
  const puedeEditar = can('inquilinos.editar')
  const pagos = useEstadosPago(can('pagos'))

  const { data = [], isLoading } = useQuery({ queryKey: ['inquilinos', estado], queryFn: async () => (await api.getInquilinos({ estado })).data })

  const filtered = useMemo(() => {
    const s = search.trim().toLowerCase()
    if (!s) return data
    return data.filter(i => i.nombre_completo?.toLowerCase().includes(s) || String(i.depto_numero).includes(s) || (i.tel_arrendatario ?? '').includes(s))
  }, [data, search])

  const metrics = useMemo(() => {
    const n = data.length
    const avg = n ? data.reduce((a, i) => a + num(i.renta), 0) / n : 0
    const porVencer = data.filter(i => { const v = vencimiento(i.fecha_termino); return v && !v.expired && v.tone === 'warning' }).length
    return { n, avg, porVencer }
  }, [data])

  const darDeBaja = useMutation({
    mutationFn: (id: string) => api.deleteInquilino(id),
    onSuccess: () => {
      toast('Inquilino dado de baja', 'success')
      setBaja(null)
      void qc.invalidateQueries({ queryKey: ['inquilinos'] })
      void qc.invalidateQueries({ queryKey: ['departamentos'] })
    },
  })

  return (
    <StaggerList className="space-y-6">
      <PageHeader title="Inquilinos"
        subtitle={`${metrics.n} ${estado === 'activo' ? (metrics.n === 1 ? 'activo' : 'activos') : (metrics.n === 1 ? 'archivado' : 'archivados')}${estado === 'activo' && metrics.porVencer ? ` · ${metrics.porVencer} por vencer` : ''}`}
        actions={estado === 'activo' && puedeEditar && <Link to="/inquilinos/nuevo"><Button icon={<Plus className="size-4" />}>Nuevo inquilino</Button></Link>} />

      <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
        <StaggerItem>
          <Card className="h-full p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md"><p className="text-[11px] font-bold uppercase tracking-wide text-muted">{estado === 'activo' ? 'Inquilinos activos' : 'Archivados'}</p><p className="mt-1 text-2xl font-extrabold"><AnimatedNumber value={metrics.n} /></p></Card>
        </StaggerItem>
        <StaggerItem>
          <Card className="h-full p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md"><p className="text-[11px] font-bold uppercase tracking-wide text-muted">Promedio de renta</p><p className="mt-1 text-2xl font-extrabold"><AnimatedNumber value={metrics.avg} format={money} /></p></Card>
        </StaggerItem>
        <StaggerItem className="hidden md:block">
          <Card className="h-full p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md"><p className="text-[11px] font-bold uppercase tracking-wide text-muted">Contratos por vencer</p><p className={`mt-1 text-2xl font-extrabold ${metrics.porVencer ? 'text-warning' : ''}`}><AnimatedNumber value={metrics.porVencer} /></p></Card>
        </StaggerItem>
      </div>

      <StaggerItem className="flex flex-wrap items-center gap-3">
        <Segmented value={estado} onChange={setEstado} options={[{ value: 'activo', label: 'Activos' }, { value: 'inactivo', label: 'Archivados' }]} />
        <div className="relative min-w-60 flex-1">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Buscar por nombre, teléfono o depto…" aria-label="Buscar inquilinos"
            className="h-11 w-full rounded-xl border border-line bg-card pl-10 pr-9 text-sm placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25" />
          {search && <button onClick={() => setSearch('')} aria-label="Limpiar búsqueda" className="absolute right-3 top-1/2 -translate-y-1/2 text-muted hover:text-fg"><X className="size-4" /></button>}
        </div>
      </StaggerItem>

      {isLoading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}
      {!isLoading && filtered.length === 0 && (
        <StaggerItem>
          <Card><EmptyState icon={Users} title={search ? 'Sin resultados' : estado === 'inactivo' ? 'Sin archivados' : 'No hay inquilinos'}>
            {search ? `No se encontró “${search}”` : estado === 'inactivo' ? 'Aquí aparecerán los inquilinos dados de baja' : 'Agrega el primer inquilino con el botón “Nuevo inquilino”'}
          </EmptyState></Card>
        </StaggerItem>
      )}

      {/* Tabla (escritorio) */}
      {filtered.length > 0 && (
        <Card className="hidden overflow-hidden p-0 md:block">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-line bg-hover/50 text-[11px] font-bold uppercase tracking-wide text-muted">
              <tr><th className="px-5 py-3">Inquilino</th><th className="px-3 py-3">Unidad</th><th className="px-3 py-3">Renta</th><th className="px-3 py-3">Estado</th><th className="px-5 py-3 text-right">Acciones</th></tr>
            </thead>
            <tbody key={`${estado}-${search}`} className="divide-y divide-line">
              {filtered.map((i, idx) => {
                const chip = estadoChip(i, pagos?.get(i.id)); const v = vencimiento(i.fecha_termino); const activo = i.estado === 'activo'
                return (
                  <motion.tr
                    key={i.id}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: Math.min(idx * 0.03, 0.25), duration: 0.25 }}
                    className="transition hover:bg-hover/40"
                  >
                    <td className="px-5 py-3">
                      <Link to={`/inquilinos/${i.id}`} className="flex items-center gap-3">
                        <span className="relative grid size-10 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-bold text-primary">
                          {iniciales(i.nombre_completo)}
                          <span className={`absolute -bottom-0.5 -right-0.5 size-3 rounded-full border-2 border-card ${activo ? 'bg-success' : 'bg-muted'}`} />
                        </span>
                        <span className="min-w-0"><span className="block truncate font-semibold">{nombreVisible(i.nombre_completo)}</span>{i.tel_arrendatario && <span className="text-xs text-muted">{i.tel_arrendatario}</span>}</span>
                      </Link>
                    </td>
                    <td className="px-3 py-3 font-semibold">Depto {i.depto_numero}</td>
                    <td className="px-3 py-3"><span className="font-bold">{money(i.renta)}</span>{i.fecha_pago && <span className="block text-xs text-muted">Día {i.fecha_pago.match(/\d+/)?.[0] ?? i.fecha_pago}</span>}</td>
                    <td className="px-3 py-3"><span className="flex flex-wrap gap-1.5"><Badge tone={chip.tone}>{chip.label}</Badge>{activo && v?.expired && <Badge tone="danger">Contrato vencido</Badge>}</span>{v && !v.expired && <span className={`mt-1 block text-xs ${v.tone === 'warning' ? 'text-warning' : 'text-muted'}`}>{v.label}</span>}</td>
                    <td className="px-5 py-3"><div className="flex justify-end gap-1"><Acciones i={i} activo={activo} puedeEditar={puedeEditar} onBaja={() => { darDeBaja.reset(); setBaja(i) }} /></div></td>
                  </motion.tr>
                )
              })}
            </tbody>
          </table>
        </Card>
      )}

      {/* Tarjetas (móvil) */}
      <StaggerList key={`${estado}-${search}`} className="space-y-3 md:hidden">
        {filtered.map(i => {
          const chip = estadoChip(i, pagos?.get(i.id)); const v = vencimiento(i.fecha_termino); const activo = i.estado === 'activo'
          return (
            <StaggerItem key={i.id}>
              <Card className="p-0">
                <Link to={`/inquilinos/${i.id}`} className="flex items-center gap-3 p-4">
                  <span className="grid size-11 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-bold text-primary">{iniciales(i.nombre_completo)}</span>
                  <span className="min-w-0 flex-1 space-y-1.5">
                    <span className="block truncate font-bold">{nombreVisible(i.nombre_completo)}</span>
                    <span className="flex flex-wrap items-center gap-2"><Badge tone="primary">Depto {i.depto_numero}</Badge><span className="text-sm font-bold">{money(i.renta)}</span></span>
                    <span className="flex flex-wrap gap-2"><Badge tone={chip.tone}>{chip.label}</Badge>{v?.short && <Badge tone={v.tone}>{v.short}</Badge>}{activo && v?.expired && <Badge tone="danger">Contrato vencido</Badge>}</span>
                  </span>
                </Link>
                {puedeEditar && (
                  <div className="flex divide-x divide-line border-t border-line text-sm font-semibold">
                    {activo ? (
                      <>
                        <Link to={`/inquilinos/${i.id}/editar`} className="flex flex-1 items-center justify-center gap-1.5 py-3 text-fg-2"><Pencil className="size-4" /> Editar</Link>
                        <button onClick={() => { darDeBaja.reset(); setBaja(i) }} className="flex flex-1 items-center justify-center gap-1.5 py-3 text-danger"><UserMinus className="size-4" /> Dar de baja</button>
                      </>
                    ) : (
                      <Link to={`/inquilinos/nuevo?desde=${i.id}`} className="flex flex-1 items-center justify-center gap-1.5 py-3 text-primary"><RotateCcw className="size-4" /> Nuevo contrato</Link>
                    )}
                  </div>
                )}
              </Card>
            </StaggerItem>
          )
        })}
      </StaggerList>

      <ConfirmDialog open={!!baja} title="Dar de baja" confirmLabel="Dar de baja" loading={darDeBaja.isPending}
        error={darDeBaja.error instanceof Error ? darDeBaja.error.message : null}
        onClose={() => setBaja(null)} onConfirm={() => baja && darDeBaja.mutate(baja.id)}>
        <p>¿Dar de baja a <b className="text-fg">{nombreVisible(baja?.nombre_completo)}</b>? El Depto {baja?.depto_numero} quedará disponible.</p>
        <p className="mt-2">Sus datos se guardarán en el historial y podrás generar un nuevo contrato en el futuro.</p>
      </ConfirmDialog>
    </StaggerList>
  )
}

function Acciones({ i, activo, puedeEditar, onBaja }: { i: Inquilino; activo: boolean; puedeEditar: boolean; onBaja: () => void }) {
  const btn = 'grid size-9 place-items-center rounded-lg text-fg-2 transition hover:bg-hover'
  return (
    <>
      <Link to={`/inquilinos/${i.id}`} className={btn} aria-label="Ver detalle" title="Ver detalle"><Eye className="size-[17px]" /></Link>
      {puedeEditar && (activo ? (
        <>
          <Link to={`/inquilinos/${i.id}/editar`} className={btn} aria-label="Editar" title="Editar"><Pencil className="size-4" /></Link>
          <button onClick={onBaja} className={`${btn} text-danger`} aria-label="Dar de baja" title="Dar de baja"><UserMinus className="size-4" /></button>
        </>
      ) : (
        <Link to={`/inquilinos/nuevo?desde=${i.id}`} className={`${btn} text-primary`} aria-label="Nuevo contrato" title="Nuevo contrato"><RotateCcw className="size-4" /></Link>
      ))}
    </>
  )
}
