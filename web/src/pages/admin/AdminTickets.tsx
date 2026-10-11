import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, CheckCircle2, ChevronLeft, ChevronRight, Clock, MessageSquare, Save, Trash2 } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useMemo, useState } from 'react'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { EmptyState } from '../../components/ui/EmptyState'
import { Modal } from '../../components/ui/Modal'
import { ShakeError, StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { PageHeader } from '../../components/ui/PageHeader'
import { Segmented } from '../../components/ui/Segmented'
import { Spinner } from '../../components/ui/Spinner'
import { Textarea } from '../../components/ui/Textarea'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { fechaHora } from '../../lib/format'
import { useSSE } from '../../lib/sse'
import type { Ticket } from '../../lib/types'

type Estado = Ticket['estado']
const E = {
  abierto: { label: 'Abierto', tone: 'danger', icon: AlertCircle, text: 'text-danger', bar: 'bg-danger' },
  en_revision: { label: 'En revisión', tone: 'warning', icon: Clock, text: 'text-warning', bar: 'bg-warning' },
  resuelto: { label: 'Resuelto', tone: 'success', icon: CheckCircle2, text: 'text-success', bar: 'bg-success' },
} as const

const mesActual = () => { const n = new Date(); return `${n.getFullYear()}-${String(n.getMonth() + 1).padStart(2, '0')}` }
const moverMes = (m: string, d: number) => { const [a, mm] = m.split('-').map(Number); const x = new Date(a, mm - 1 + d, 1); return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}` }
const labelMes = (m: string) => { const [a, mm] = m.split('-').map(Number); return new Date(a, mm - 1, 1).toLocaleDateString('es-MX', { month: 'long', year: 'numeric' }) }

export default function AdminTickets() {
  const qc = useQueryClient()
  const toast = useToast()
  const [vista, setVista] = useState<'activos' | 'historial'>('activos')
  const [filtro, setFiltro] = useState<Estado | 'todos'>('todos')
  const [mes, setMes] = useState(mesActual())
  const [selId, setSelId] = useState<string | null>(null)
  const [nota, setNota] = useState('')
  const [borrar, setBorrar] = useState<Ticket | null>(null)
  const [detalleMovil, setDetalleMovil] = useState(false)

  const activos = useQuery({ queryKey: ['tickets'], queryFn: async () => (await api.getTickets()).data, refetchInterval: 20_000 })
  const hist = useQuery({ queryKey: ['tickets-historial', mes], queryFn: async () => (await api.getTickets({ mes })).data, enabled: vista === 'historial' })
  const refrescar = () => { void qc.invalidateQueries({ queryKey: ['tickets'] }); void qc.invalidateQueries({ queryKey: ['tickets-historial'] }) }
  useSSE('ticket_new', (d) => { refrescar(); if (d?.ticket?.titulo) toast(`🔔 Nuevo ticket: ${d.ticket.titulo} — Depto ${d.ticket.depto_numero}`, 'info') })
  useSSE('ticket_updated', refrescar)

  const tickets = activos.data ?? []
  const counts = useMemo(() => ({ abierto: tickets.filter(t => t.estado === 'abierto').length, en_revision: tickets.filter(t => t.estado === 'en_revision').length, resuelto: tickets.filter(t => t.estado === 'resuelto').length }), [tickets])
  const filtrados = filtro === 'todos' ? tickets : tickets.filter(t => t.estado === filtro)
  const sel = tickets.find(t => t.id === selId) ?? null

  // En escritorio se selecciona el primero automáticamente.
  useEffect(() => { if (vista === 'activos' && !selId && filtrados.length) setSelId(filtrados[0].id) }, [vista, selId, filtrados])
  useEffect(() => { setNota(sel?.nota_admin ?? '') }, [selId]) // eslint-disable-line react-hooks/exhaustive-deps

  const guardar = useMutation({
    mutationFn: (estado?: Estado) => api.updateTicket(sel!.id, { estado: estado ?? sel!.estado, nota_admin: nota }),
    onSuccess: () => { toast('Ticket actualizado', 'success'); refrescar() },
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo actualizar', 'error'),
  })
  const eliminar = useMutation({
    mutationFn: (id: string) => api.deleteTicket(id),
    onSuccess: (_r, id) => { setBorrar(null); if (selId === id) setSelId(null); toast('Ticket eliminado', 'success'); refrescar() },
  })
  const siguiente: Record<Estado, Estado> = { abierto: 'en_revision', en_revision: 'resuelto', resuelto: 'resuelto' }

  const Item = ({ t }: { t: Ticket }) => {
    const cfg = E[t.estado]
    return (
      <button onClick={() => { setSelId(t.id); setDetalleMovil(true) }}
        className={`relative w-full overflow-hidden rounded-2xl border bg-card p-4 pl-5 text-left transition hover:bg-hover ${selId === t.id ? 'border-primary ring-1 ring-primary/40' : 'border-line'}`}>
        <span className={`absolute inset-y-0 left-0 w-1.5 ${cfg.bar}`} />
        <div className="flex items-start justify-between gap-2"><p className="font-bold leading-tight">{t.titulo}</p><Badge tone={cfg.tone}>{cfg.label}</Badge></div>
        <p className="mt-0.5 text-xs text-fg-2">{t.inquilino_nombre} · Depto {t.depto_numero}</p>
        <p className="mt-1 line-clamp-2 text-sm text-fg-2">{t.descripcion}</p>
        <p className="mt-2 text-[11px] text-muted">{fechaHora(t.created_at)}</p>
      </button>
    )
  }

  const detalle = sel && (
    <Card className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div><h2 className="text-lg font-extrabold leading-tight">{sel.titulo}</h2><p className="text-sm text-fg-2">{sel.inquilino_nombre} · Depto {sel.depto_numero} · {fechaHora(sel.created_at)}</p></div>
        <Badge tone={E[sel.estado].tone}>{E[sel.estado].label}</Badge>
      </div>
      <p className="whitespace-pre-wrap rounded-xl bg-hover p-4 text-sm">{sel.descripcion}</p>
      <Textarea label="Nota o respuesta al inquilino" placeholder="Agrega una nota o respuesta al inquilino…" value={nota} onChange={e => setNota(e.target.value)} />
      {sel.atendido_por_nombre && <p className="text-xs text-muted">Atendido por {sel.atendido_por_nombre}</p>}
      <div className="flex flex-wrap gap-2">
        <Button variant="secondary" loading={guardar.isPending} icon={<Save className="size-4" />} onClick={() => guardar.mutate(undefined)}>Guardar nota</Button>
        {sel.estado !== 'resuelto' && <Button loading={guardar.isPending} onClick={() => guardar.mutate(siguiente[sel.estado])}>{sel.estado === 'abierto' ? 'Marcar en revisión' : 'Marcar resuelto'}</Button>}
        <Button variant="ghost" className="ml-auto text-danger" icon={<Trash2 className="size-4" />} onClick={() => { eliminar.reset(); setBorrar(sel) }}>Eliminar</Button>
      </div>
    </Card>
  )

  return (
    <StaggerList key={vista} className="space-y-6">
      <PageHeader title="Tickets" subtitle={`${counts.abierto + counts.en_revision} activo${counts.abierto + counts.en_revision !== 1 ? 's' : ''}${counts.abierto ? ` · ${counts.abierto} abierto${counts.abierto !== 1 ? 's' : ''}` : ''}`}
        actions={<Segmented value={vista} onChange={setVista} options={[{ value: 'activos', label: 'Activos' }, { value: 'historial', label: 'Historial' }]} />} />

      {vista === 'activos' ? (
        <>
          <div className="grid grid-cols-3 gap-3">
            {(['abierto', 'en_revision', 'resuelto'] as Estado[]).map(e => { const c = E[e]; return (
              <StaggerItem key={e} className="h-full">
                <Card className="h-full p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md"><p className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wide text-muted"><c.icon className={`size-3.5 ${c.text}`} /> {c.label}</p><p className={`mt-1 text-2xl font-extrabold ${c.text}`}><AnimatedNumber value={counts[e]} /></p></Card>
              </StaggerItem>
            ) })}
          </div>
          <StaggerItem className="flex flex-wrap gap-1.5 sm:gap-2">
            {(['todos', 'abierto', 'en_revision', 'resuelto'] as const).map(f => {
              const active = filtro === f
              return (
                <button key={f} onClick={() => { setFiltro(f); setSelId(null) }} className={`relative rounded-full border border-transparent px-3 py-1.5 text-xs font-semibold transition-colors sm:px-4 sm:text-sm ${active ? 'text-white' : 'border-line bg-card text-fg-2 hover:bg-hover'}`}>
                  {active && (
                    <motion.span
                      layoutId="admin-tickets-filter"
                      transition={{ type: 'spring', stiffness: 400, damping: 36, bounce: 0 }}
                      className="absolute inset-0 rounded-full bg-primary shadow-sm shadow-primary/25"
                    />
                  )}
                  <span className="relative z-10">{f === 'todos' ? 'Todos' : E[f].label} ({f === 'todos' ? tickets.length : counts[f]})</span>
                </button>
              )
            })}
          </StaggerItem>
          {activos.isLoading && <div className="grid place-items-center py-16"><Spinner className="size-8" /></div>}
          {!activos.isLoading && filtrados.length === 0 && (
            <StaggerItem>
              <Card><EmptyState icon={MessageSquare} title="Sin tickets">Cuando un inquilino reporte un problema aparecerá aquí.</EmptyState></Card>
            </StaggerItem>
          )}
          {filtrados.length > 0 && (
            <div className="grid gap-5 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
              <div key={filtro} className="space-y-3">
                {filtrados.map(t => (
                  <StaggerItem key={t.id}>
                    <Item t={t} />
                  </StaggerItem>
                ))}
              </div>
              <StaggerItem className="hidden lg:block lg:sticky lg:top-6 lg:self-start">
                <AnimatePresence mode="wait">
                  {sel ? (
                    <motion.div
                      key={sel.id}
                      initial={{ opacity: 0, y: 8 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, y: -6 }}
                      transition={{ duration: 0.2 }}
                    >
                      {detalle}
                    </motion.div>
                  ) : (
                    <Card className="py-16 text-center text-sm text-muted">Selecciona un ticket</Card>
                  )}
                </AnimatePresence>
              </StaggerItem>
            </div>
          )}
          {/* móvil: el detalle abre en un modal */}
          <div className="lg:hidden"><Modal open={detalleMovil && !!sel} title="Ticket" onClose={() => setDetalleMovil(false)}>{detalle}</Modal></div>
        </>
      ) : (
        <>
          <StaggerItem className="flex items-center justify-center gap-3">
            <Button variant="secondary" className="h-10 px-3" aria-label="Mes anterior" onClick={() => setMes(m => moverMes(m, -1))} icon={<ChevronLeft className="size-4" />} />
            <p className="min-w-44 text-center font-bold"><span className="inline-block first-letter:uppercase">{labelMes(mes)}</span></p>
            <Button variant="secondary" className="h-10 px-3" aria-label="Mes siguiente" disabled={mes >= mesActual()} onClick={() => setMes(m => moverMes(m, 1))} icon={<ChevronRight className="size-4" />} />
          </StaggerItem>
          {hist.isLoading && <div className="grid place-items-center py-16"><Spinner className="size-8" /></div>}
          {!hist.isLoading && hist.data?.length === 0 && (
            <StaggerItem>
              <Card><EmptyState icon={MessageSquare} title="Sin tickets en este mes" /></Card>
            </StaggerItem>
          )}
          <div key={mes} className="grid gap-3 md:grid-cols-2">
            {hist.data?.map(t => (
              <StaggerItem key={t.id}>
                <Card className="space-y-2">
                  <div className="flex items-start justify-between gap-2"><p className="font-bold">{t.titulo}</p><Badge tone={E[t.estado].tone}>{E[t.estado].label}</Badge></div>
                  <p className="text-xs text-fg-2">{t.inquilino_nombre} · Depto {t.depto_numero} · {fechaHora(t.created_at)}</p>
                  <p className="text-sm text-fg-2">{t.descripcion}</p>
                  {t.nota_admin && <p className="rounded-xl bg-primary-soft p-3 text-xs">{t.nota_admin}</p>}
                  <div className="flex justify-end"><Button variant="ghost" className="h-8 px-3 text-danger" icon={<Trash2 className="size-4" />} onClick={() => { eliminar.reset(); setBorrar(t) }}>Eliminar</Button></div>
                </Card>
              </StaggerItem>
            ))}
          </div>
        </>
      )}

      <Modal open={!!borrar} title="Eliminar ticket" onClose={() => setBorrar(null)}>
        <div className="space-y-4">
          <p className="text-sm text-fg-2">¿Eliminar el ticket <b className="text-fg">{borrar?.titulo}</b>? Esta acción no se puede deshacer.</p>
          {eliminar.error instanceof Error && (
            <ShakeError error={eliminar.error.message}>
              <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{eliminar.error.message}</p>
            </ShakeError>
          )}
          <div className="grid grid-cols-2 gap-2"><Button variant="secondary" onClick={() => setBorrar(null)}>Cancelar</Button><Button variant="danger" loading={eliminar.isPending} onClick={() => borrar && eliminar.mutate(borrar.id)}>Eliminar</Button></div>
        </div>
      </Modal>
    </StaggerList>
  )
}
