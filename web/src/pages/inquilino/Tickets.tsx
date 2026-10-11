import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertCircle, ArrowLeft, CheckCircle2, Clock, MessageSquare, Plus, Trash2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { ShakeError, StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { Spinner } from '../../components/ui/Spinner'
import { Textarea } from '../../components/ui/Textarea'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { fecha } from '../../lib/format'
import { useSSE } from '../../lib/sse'
import type { Ticket } from '../../lib/types'

const ESTADOS = {
  abierto: { label: 'Abierto', tone: 'danger', bar: 'bg-danger', icon: AlertCircle },
  en_revision: { label: 'En revisión', tone: 'warning', bar: 'bg-warning', icon: Clock },
  resuelto: { label: 'Resuelto', tone: 'success', bar: 'bg-success', icon: CheckCircle2 },
} as const

const CINCO_MIN = 5 * 60 * 1000

/** Reloj que se actualiza cada segundo, solo mientras `active`. */
function useNow(active: boolean) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!active) return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [active])
  return now
}

export default function InquilinoTickets() {
  const qc = useQueryClient()
  const toast = useToast()
  const [nuevo, setNuevo] = useState(false)
  const [titulo, setTitulo] = useState('')
  const [descripcion, setDescripcion] = useState('')
  const [error, setError] = useState('')

  const { data: tickets, isLoading } = useQuery({
    queryKey: ['mis-tickets'],
    queryFn: async () => (await api.getTickets()).data,
    refetchInterval: 20_000,
  })
  useSSE('ticket_updated', (d) => {
    void qc.invalidateQueries({ queryKey: ['mis-tickets'] })
    const e = d?.ticket?.estado
    if (e) toast(`Tu reporte fue marcado como ${e === 'resuelto' ? 'resuelto ✅' : 'en revisión 🔍'}`, 'info')
  })

  // El contador de "Eliminar (Ns)" solo corre si hay algún ticket dentro de la ventana de 5 min.
  const hayEliminables = !!tickets?.some(t => Date.now() - new Date(t.created_at).getTime() < CINCO_MIN)
  const now = useNow(hayEliminables)

  const crear = useMutation({
    mutationFn: () => api.createTicket({ titulo: titulo.trim(), descripcion: descripcion.trim() }),
    onSuccess: () => {
      setTitulo(''); setDescripcion(''); setNuevo(false); setError('')
      toast('Reporte enviado', 'success')
      void qc.invalidateQueries({ queryKey: ['mis-tickets'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'No se pudo crear el reporte'),
  })

  const eliminar = useMutation({
    mutationFn: (id: string) => api.deleteTicket(id),
    onSuccess: (_r, id) => qc.setQueryData<Ticket[]>(['mis-tickets'], old => old?.filter(t => t.id !== id)),
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo eliminar el reporte', 'error'),
  })

  const submit = () => {
    if (!titulo.trim()) return setError('El título es requerido')
    if (!descripcion.trim()) return setError('La descripción es requerida')
    setError(''); crear.mutate()
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Link to="/" aria-label="Volver" className="grid size-10 place-items-center rounded-xl text-fg-2 hover:bg-hover"><ArrowLeft className="size-5" /></Link>
        <h1 className="flex-1 text-2xl font-extrabold tracking-tight">Mis reportes</h1>
        <Button onClick={() => setNuevo(true)} icon={<Plus className="size-4" />} className="h-10 px-4">Nuevo</Button>
      </div>

      {isLoading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}

      {tickets?.length === 0 && (
        <Card className="mx-auto max-w-md py-14 text-center">
          <MessageSquare className="mx-auto mb-3 size-12 text-muted" />
          <p className="font-bold">Sin reportes</p>
          <p className="mb-5 mt-1 text-sm text-fg-2">¿Tienes algún problema con tu departamento? Crea un reporte y el administrador lo atenderá.</p>
          <Button onClick={() => setNuevo(true)} icon={<Plus className="size-4" />}>Crear primer reporte</Button>
        </Card>
      )}

      {tickets && tickets.length > 0 && (
        <StaggerList className="grid gap-3 md:grid-cols-2">
          {tickets.map(t => {
            const cfg = ESTADOS[t.estado]
            const restante = Math.max(0, CINCO_MIN - (now - new Date(t.created_at).getTime()))
            const puedeBorrar = Date.now() - new Date(t.created_at).getTime() < CINCO_MIN && restante > 0
            return (
              <StaggerItem key={t.id}>
                <Card className="relative overflow-hidden py-4 pl-6">
                  <span className={`absolute inset-y-0 left-0 w-1.5 ${cfg.bar}`} />
                  <div className="flex items-start justify-between gap-2">
                    <h3 className="font-bold leading-tight">{t.titulo}</h3>
                    <Badge tone={cfg.tone}>{cfg.label}</Badge>
                  </div>
                  <p className="mt-1 line-clamp-3 text-sm text-fg-2">{t.descripcion}</p>
                  {t.nota_admin && (
                    <p className="mt-3 flex gap-2 rounded-xl bg-primary-soft p-3 text-xs"><MessageSquare className="mt-0.5 size-3.5 shrink-0 text-primary" /><span>{t.nota_admin}</span></p>
                  )}
                  <div className="mt-3 flex items-center justify-between">
                    <span className="text-xs text-muted">{fecha(t.created_at)}</span>
                    {puedeBorrar && (
                      <button onClick={() => { if (confirm('¿Seguro que quieres eliminar este reporte?')) eliminar.mutate(t.id) }}
                        className="inline-flex items-center gap-1 rounded-lg bg-danger-soft px-2.5 py-1 text-[11px] font-bold text-danger">
                        <Trash2 className="size-3" /> Eliminar ({Math.ceil(restante / 1000)}s)
                      </button>
                    )}
                  </div>
                </Card>
              </StaggerItem>
            )
          })}
        </StaggerList>
      )}

      <Modal open={nuevo} title="Nuevo reporte" onClose={() => setNuevo(false)}>
        <div className="space-y-5">
          <Input label="Título" placeholder="Ej. Fuga de agua en el baño" value={titulo} maxLength={200} onChange={e => { setError(''); setTitulo(e.target.value) }} />
          <Textarea label="Descripción" placeholder="Cuéntanos qué pasa…" value={descripcion} onChange={e => { setError(''); setDescripcion(e.target.value) }} />
          {error && (
            <ShakeError error={error}>
              <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>
            </ShakeError>
          )}
          <div className="pt-2">
            <Button full onClick={submit} loading={crear.isPending}>Enviar reporte</Button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
