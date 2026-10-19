import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, CheckCircle2, FileImage } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { EmptyState } from '../../components/ui/EmptyState'
import { Modal } from '../../components/ui/Modal'
import { StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { PageHeader } from '../../components/ui/PageHeader'
import { Spinner } from '../../components/ui/Spinner'
import { Textarea } from '../../components/ui/Textarea'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { haceCuanto, money, periodoLabel } from '../../lib/format'
import { useSSE } from '../../lib/sse'
import { nombreVisible } from '../../lib/nombre'

type Item = Awaited<ReturnType<typeof api.getComprobantesPendientes>>['data'][number]

export default function Comprobantes() {
  const qc = useQueryClient()
  const toast = useToast()
  const [sel, setSel] = useState<Item | null>(null)
  const [rechazando, setRechazando] = useState(false)
  const [motivo, setMotivo] = useState('')

  // Se actualiza sola: al instante por SSE cuando llega o se resuelve un comprobante; además al volver a la pestaña
  // y cada 15 s como respaldo por si la conexión en vivo se cortó.
  const q = useQuery({
    queryKey: ['comprobantes'], queryFn: async () => (await api.getComprobantesPendientes()).data,
    refetchInterval: 15_000, refetchOnWindowFocus: 'always', refetchOnReconnect: 'always',
  })
  const refrescar = () => void qc.invalidateQueries({ queryKey: ['comprobantes'] })
  useSSE('comprobante_subido', refrescar)
  useSSE('payment_confirmed', refrescar)
  useSSE('payment_rejected', refrescar)

  const hecho = (msg: string) => {
    toast(msg, 'success'); setSel(null); setRechazando(false); setMotivo('')
    for (const k of ['comprobantes', 'estados-actuales', 'saldos', 'resumen-deuda', 'estado-pago']) void qc.invalidateQueries({ queryKey: [k] })
  }
  const confirmar = useMutation({ mutationFn: (id: string) => api.confirmarPagoAdmin(id), onSuccess: () => hecho('Pago confirmado'), onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo confirmar', 'error') })
  const rechazar = useMutation({ mutationFn: (id: string) => api.rechazarPago(id, motivo.trim()), onSuccess: () => hecho('Comprobante rechazado'), onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo rechazar', 'error') })

  const n = q.data?.length ?? 0
  return (
    <StaggerList className="space-y-6">
      <PageHeader title="Comprobantes" subtitle={n ? `${n} pendiente${n !== 1 ? 's' : ''} de revisar` : 'Todo al día'} />

      {q.isLoading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}
      {!q.isLoading && n === 0 && (
        <StaggerItem>
          <Card><EmptyState icon={FileImage} title="Sin comprobantes pendientes">Cuando un inquilino suba la imagen de su transferencia aparecerá aquí para que la revises, la confirmes o la rechaces con un comentario.</EmptyState></Card>
        </StaggerItem>
      )}

      {n > 0 && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {q.data?.map(c => (
            <StaggerItem key={c.id} layout>
              <Card className="group overflow-hidden p-0 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md">
                <button onClick={() => setSel(c)} className="block w-full text-left">
                  <div className="grid h-40 place-items-center overflow-hidden bg-hover">
                    {c.comprobante_url ? <img src={c.comprobante_url} alt={`Comprobante de ${nombreVisible(c.nombre_completo)}`} className="size-full object-cover transition-transform duration-300 group-hover:scale-105" loading="lazy" /> : <FileImage className="size-10 text-muted" />}
                  </div>
                  <div className="space-y-1 p-4">
                    <p className="truncate font-bold">{nombreVisible(c.nombre_completo)}</p>
                    <p className="text-sm text-fg-2">Depto {c.depto_numero} · <span className="inline-block first-letter:uppercase">{periodoLabel(c.periodo)}</span></p>
                    <div className="flex items-center justify-between pt-1"><b>{money(c.monto)}</b><span className="text-xs text-muted">{c.comprobante_subido_en ? haceCuanto(c.comprobante_subido_en) : ''}</span></div>
                  </div>
                </button>
              </Card>
            </StaggerItem>
          ))}
        </div>
      )}


      <Modal open={!!sel && !rechazando} title="Revisar comprobante" onClose={() => setSel(null)}>
        {sel && (
          <div className="space-y-4">
            {sel.comprobante_url && <img src={sel.comprobante_url} alt="Comprobante" className="max-h-[55vh] w-full rounded-xl bg-hover object-contain" />}
            <div className="flex items-center justify-between text-sm">
              <span><Link to={`/pagos/${sel.inquilino_id}`} className="font-bold text-primary hover:underline">{nombreVisible(sel.nombre_completo)}</Link><span className="block text-fg-2">Depto {sel.depto_numero} · <span className="inline-block first-letter:uppercase">{periodoLabel(sel.periodo)}</span></span></span>
              <b className="text-lg">{money(sel.monto)}</b>
            </div>
            <div className="grid grid-cols-2 gap-2">
              <Button loading={confirmar.isPending} icon={<CheckCircle2 className="size-4" />} onClick={() => confirmar.mutate(sel.id)}>Confirmar pago</Button>
              <Button variant="danger" icon={<Ban className="size-4" />} onClick={() => setRechazando(true)}>Rechazar</Button>
            </div>
          </div>
        )}
      </Modal>

      <Modal open={!!sel && rechazando} title="Rechazar comprobante" onClose={() => setRechazando(false)}>
        <div className="space-y-4">
          <p className="text-sm text-fg-2">El inquilino recibirá una notificación con el motivo.</p>
          <Textarea label="Motivo (opcional)" placeholder="Ej. La imagen está borrosa" value={motivo} onChange={e => setMotivo(e.target.value)} />
          <Button full variant="danger" loading={rechazar.isPending} onClick={() => sel && rechazar.mutate(sel.id)}>Rechazar comprobante</Button>
        </div>
      </Modal>
    </StaggerList>
  )
}
