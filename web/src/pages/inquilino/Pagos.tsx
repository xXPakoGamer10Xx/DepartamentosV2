import { useQuery } from '@tanstack/react-query'
import { AlertCircle, ArrowLeft, Banknote, Calendar, CheckCircle2, CircleDashed, CreditCard, FileImage, Receipt, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Badge } from '../../components/ui/Badge'
import { Card } from '../../components/ui/Card'
import { Modal } from '../../components/ui/Modal'
import { StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { Spinner } from '../../components/ui/Spinner'
import { api } from '../../lib/api'
import { fechaHora, money, periodoLabel } from '../../lib/format'

export default function InquilinoPagos() {
  const [verComprobante, setVerComprobante] = useState<string | null>(null)

  const { data: pagos, isLoading } = useQuery({
    queryKey: ['historial-pagos'],
    queryFn: async () => {
      const inq = (await api.getMiDepto()).data
      return inq ? (await api.getHistorialPagos(inq.id)).data : []
    },
  })

  const confirmados = pagos?.filter(p => p.confirmado) ?? []
  const aTiempo = confirmados.filter(p => p.a_tiempo === true).length
  const tarde = confirmados.filter(p => p.a_tiempo === false).length

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-3">
        <Link to="/" aria-label="Volver" className="grid size-10 place-items-center rounded-xl text-fg-2 hover:bg-hover"><ArrowLeft className="size-5" /></Link>
        <div>
          <h1 className="text-2xl font-extrabold tracking-tight">Historial de pagos</h1>
          <p className="text-sm text-fg-2">Todos tus pagos registrados</p>
        </div>
      </div>

      {isLoading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}

      {pagos && pagos.length > 0 && (
        <div className="grid grid-cols-3 gap-3">
          <Chip tone="primary" icon={<CheckCircle2 className="size-4" />} n={confirmados.length} label={confirmados.length === 1 ? 'pagado' : 'pagados'} />
          <Chip tone="success" icon={<Calendar className="size-4" />} n={aTiempo} label="a tiempo" />
          <Chip tone="danger" icon={<AlertCircle className="size-4" />} n={tarde} label="tarde" />
        </div>
      )}

      {pagos && pagos.length === 0 && (
        <Card className="py-16 text-center">
          <Receipt className="mx-auto mb-3 size-12 text-muted" />
          <p className="font-bold">Sin historial de pagos</p>
          <p className="mt-1 text-sm text-fg-2">Tus pagos confirmados aparecerán aquí.</p>
        </Card>
      )}

      {pagos && pagos.length > 0 && (
        <StaggerList className="grid grid-cols-1 gap-3 md:grid-cols-2">
          {pagos.map(p => {
            const estado = !p.confirmado
              ? { tone: 'neutral' as const, label: 'Pendiente', bar: 'bg-muted', icon: <CircleDashed className="size-3" /> }
              : p.a_tiempo === false
                ? { tone: 'warning' as const, label: 'Con retraso', bar: 'bg-warning', icon: <TriangleAlert className="size-3" /> }
                : { tone: 'success' as const, label: p.a_tiempo === true ? 'A tiempo' : 'Confirmado', bar: 'bg-success', icon: <CheckCircle2 className="size-3" /> }
            return (
              <StaggerItem key={p.id}>
                <Card className="relative overflow-hidden py-4 pl-6">
                  <span className={`absolute inset-y-0 left-0 w-1.5 ${estado.bar}`} />
                  <div className="flex items-center justify-between gap-2">
                    <p className="text-xs font-extrabold uppercase tracking-wide">{periodoLabel(p.periodo)}</p>
                    <Badge tone={estado.tone}><span className="mr-1">{estado.icon}</span>{estado.label}</Badge>
                  </div>
                  <p className="mt-1 text-2xl font-extrabold tracking-tight">{money(p.monto)}</p>
                  <div className="mt-2 space-y-1 text-xs text-fg-2">
                    {p.confirmado && p.confirmado_en && <p className="flex items-center gap-1.5"><Calendar className="size-3.5" /> Pagado: {fechaHora(p.confirmado_en)}</p>}
                    <p className="flex items-center gap-1.5">
                      {p.metodo === 'transferencia' ? <CreditCard className="size-3.5" /> : <Banknote className="size-3.5" />}
                      {p.metodo === 'transferencia' ? 'Transferencia bancaria' : 'Efectivo (QR)'}
                    </p>
                  </div>
                  {p.comprobante_url && (
                    <button onClick={() => setVerComprobante(p.comprobante_url)} className="mt-3 inline-flex items-center gap-1.5 rounded-lg bg-primary-soft px-3 py-1.5 text-xs font-bold text-primary transition hover:bg-primary/20">
                      <FileImage className="size-3.5" /> Ver comprobante
                    </button>
                  )}
                </Card>
              </StaggerItem>
            )
          })}
        </StaggerList>
      )}

      <Modal open={!!verComprobante} title="Comprobante de pago" onClose={() => setVerComprobante(null)}>
        {verComprobante && <img src={verComprobante} alt="Comprobante" className="max-h-[70vh] w-full rounded-xl object-contain" />}
      </Modal>
    </div>
  )
}

function Chip({ tone, icon, n, label }: { tone: 'primary' | 'success' | 'danger'; icon: React.ReactNode; n: number; label: string }) {
  const c = { primary: 'bg-primary-soft text-primary', success: 'bg-success-soft text-success', danger: 'bg-danger-soft text-danger' }[tone]
  return (
    <div className={`flex items-center justify-center gap-2 rounded-2xl p-3 ${c}`}>
      {icon}<span className="text-xl font-black"><AnimatedNumber value={n} /></span><span className="text-[11px] font-semibold text-fg-2">{label}</span>
    </div>
  )
}
