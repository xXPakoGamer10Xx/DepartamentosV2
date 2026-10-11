import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { Spinner } from '../../../components/ui/Spinner'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import type { Plantilla } from '../../../lib/types'

/** Elige a qué propiedades se aplica una plantilla (los contratos de esas propiedades la usarán). */
export function AsignarPlantillaModal({ plantilla, onClose }: { plantilla: Plantilla | null; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [sel, setSel] = useState<Set<number>>(new Set())
  const { data: deptos = [], isLoading } = useQuery({ queryKey: ['departamentos'], queryFn: async () => (await api.getDepartamentos()).data, enabled: !!plantilla })

  useEffect(() => {
    if (!plantilla) return
    // Las ya asignadas a esta plantilla llegan marcadas (el listado trae plantilla_id por propiedad).
    setSel(new Set(deptos.filter(d => d.plantilla_id === plantilla.id).map(d => d.id)))
  }, [plantilla, deptos])

  const guardar = useMutation({
    mutationFn: () => api.asignarPlantilla(plantilla!.id, [...sel]),
    onSuccess: () => {
      toast('Plantilla asignada', 'success')
      void qc.invalidateQueries({ queryKey: ['plantillas'] })
      void qc.invalidateQueries({ queryKey: ['departamentos'] })
      onClose()
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo asignar', 'error'),
  })

  const toggle = (id: number) => setSel(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n })

  return (
    <Modal open={!!plantilla} title="Asignar a propiedades" onClose={onClose}>
      <p className="mb-4 text-sm text-fg-2">Los contratos de las propiedades marcadas usarán <b className="text-fg">{plantilla?.nombre}</b>. Las demás usan tu plantilla predeterminada.</p>
      {isLoading && <div className="grid place-items-center py-10"><Spinner className="size-7" /></div>}
      {!isLoading && deptos.length === 0 && <p className="py-6 text-center text-sm text-muted">Aún no tienes propiedades.</p>}
      <ul className="space-y-1.5">
        {deptos.map(d => (
          <li key={d.id}>
            <label className="flex cursor-pointer items-center gap-3 rounded-xl border border-line px-4 py-3 transition hover:bg-hover has-[:checked]:border-primary has-[:checked]:bg-primary-soft">
              <input type="checkbox" checked={sel.has(d.id)} onChange={() => toggle(d.id)} className="size-4 accent-[var(--primary)]" />
              <span className="min-w-0 flex-1"><span className="block font-semibold">Departamento {d.numero}</span>{d.descripcion && <span className="block truncate text-xs text-muted">{d.descripcion}</span>}</span>
            </label>
          </li>
        ))}
      </ul>
      <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <Button variant="ghost" onClick={onClose}>Cancelar</Button>
        <Button loading={guardar.isPending} onClick={() => guardar.mutate()}>Guardar ({sel.size})</Button>
      </div>
    </Modal>
  )
}
