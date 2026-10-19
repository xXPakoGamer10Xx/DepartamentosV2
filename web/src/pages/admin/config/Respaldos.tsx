import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { DatabaseBackup, Download, Plus } from 'lucide-react'
import { motion } from 'motion/react'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Card } from '../../../components/ui/Card'
import { EmptyState } from '../../../components/ui/EmptyState'
import { StaggerItem, StaggerList } from '../../../components/ui/MotionPrimitives'
import { Spinner } from '../../../components/ui/Spinner'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { fechaHora, num } from '../../../lib/format'

const tam = (b: string) => { const n = num(b); return n >= 1_048_576 ? `${(n / 1_048_576).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1024))} KB` }

export function Respaldos() {
  const qc = useQueryClient()
  const toast = useToast()
  const { data = [], isLoading } = useQuery({ queryKey: ['backups'], queryFn: async () => (await api.getBackups()).data })
  const crear = useMutation({
    mutationFn: () => api.crearBackup(),
    onSuccess: () => { toast('Respaldo creado', 'success'); void qc.invalidateQueries({ queryKey: ['backups'] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo crear el respaldo', 'error'),
  })
  const descargar = useMutation({
    mutationFn: async (archivo: string) => {
      const blob = await api.descargarBackup(archivo)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a'); a.href = url; a.download = archivo; a.click()
      setTimeout(() => URL.revokeObjectURL(url), 1000)
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo descargar', 'error'),
  })

  return (
    <StaggerList className="min-w-0 space-y-5">
      <StaggerItem className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-fg-2">Respaldo automático diario a las 4:00 AM (hora CDMX); se conservan 30 días.</p>
        <Button loading={crear.isPending} icon={<Plus className="size-4" />} onClick={() => crear.mutate()}>Crear respaldo ahora</Button>
      </StaggerItem>
      {isLoading && <div className="grid place-items-center py-16"><Spinner className="size-8" /></div>}
      {!isLoading && data.length === 0 && (
        <StaggerItem>
          <Card><EmptyState icon={DatabaseBackup} title="Sin respaldos">Crea el primero con “Crear respaldo ahora”.</EmptyState></Card>
        </StaggerItem>
      )}
      {data.length > 0 && (
        <StaggerItem>
          <Card className="divide-y divide-line overflow-hidden p-0!">
            {data.map((b, idx) => (
              <motion.div
                key={b.id}
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: Math.min(idx * 0.035, 0.25), duration: 0.22 }}
                className="flex flex-col gap-2 px-4 py-3 text-sm transition hover:bg-hover/40 sm:flex-row sm:items-center sm:gap-3 sm:px-5"
              >
                <div className="flex min-w-0 flex-1 items-center gap-3">
                  <DatabaseBackup className="size-5 shrink-0 text-muted" />
                  <span className="min-w-0 flex-1"><span className="block truncate font-semibold">{b.archivo}</span><span className="text-xs text-muted">{fechaHora(b.created_at)} · {tam(b.tamano_bytes)}</span></span>
                </div>
                <div className="flex items-center justify-between gap-2 pl-8 sm:justify-end sm:pl-0">
                  <span className="flex min-w-0 flex-wrap items-center gap-2">
                    <Badge tone={b.estado === 'completado' ? 'success' : 'danger'}>{b.estado}</Badge>
                    <Badge tone={b.tipo === 'manual' ? 'primary' : 'neutral'}>{b.tipo}</Badge>
                  </span>
                  {b.estado === 'completado' && <Button variant="ghost" className="h-8 shrink-0 px-3" icon={<Download className="size-4" />} loading={descargar.isPending && descargar.variables === b.archivo} aria-label="Descargar" onClick={() => descargar.mutate(b.archivo)}><span className="hidden sm:inline">Descargar</span></Button>}
                </div>
              </motion.div>
            ))}
          </Card>
        </StaggerItem>
      )}
    </StaggerList>
  )
}

