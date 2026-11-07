import { useMutation } from '@tanstack/react-query'
import { CalendarClock } from 'lucide-react'
import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { Card } from '../../../components/ui/Card'
import { Input } from '../../../components/ui/Input'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { diasHasta, enDiasISO } from '../../../lib/fecha'
import { errMsg } from './comun'


export function Promesa({ inquilinoId, pagoId, fechaActual, bloqueado, puedeEditar, onChange }: { inquilinoId: string; pagoId: string | null; fechaActual: string | null; bloqueado: boolean; puedeEditar: boolean; onChange: () => void }) {
  const toast = useToast()
  const guardada = fechaActual ? String(fechaActual).slice(0, 10) : ''
  const [valor, setValor] = useState(guardada)
  const [editando, setEditando] = useState(!guardada)
  // si el servidor cambia la fecha (otro admin, refresco), se refleja salvo que se esté editando
  const [prev, setPrev] = useState(guardada)
  if (prev !== guardada) { setPrev(guardada); if (!editando) setValor(guardada) }

  const guardar = useMutation({
    mutationFn: (f: string | null) => (pagoId ? api.setPromesa(pagoId, f) : api.setPromesaInquilino(inquilinoId, f)),
    onSuccess: (_r, f) => { toast(f ? 'Promesa guardada' : 'Promesa eliminada', 'success'); setEditando(!f); if (!f) setValor(''); onChange() },
    onError: (e) => toast(errMsg(e, 'No se pudo guardar la promesa'), 'error'),
  })

  const rapido = (dias: number) => { setValor(enDiasISO(dias)); setEditando(true) }
  const preview = (() => {
    if (!valor) return null
    const { diff, fecha: d } = diasHasta(valor)
    const raw = d.toLocaleDateString('es-MX', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
    const rel = diff === 0 ? 'hoy' : diff === 1 ? 'mañana' : diff === -1 ? 'ayer' : diff > 1 ? `en ${diff} días` : `hace ${Math.abs(diff)} días`
    return `${raw.charAt(0).toUpperCase()}${raw.slice(1)} · ${rel}`
  })()

  return (
    <Card className="space-y-3">
      <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2"><CalendarClock className="size-4" /> Promesa de pago</h2>
      {bloqueado ? <p className="text-sm text-muted">El periodo ya está pagado.</p> : !puedeEditar && !guardada ? <p className="text-sm text-muted">Sin promesa registrada.</p> : (
        <>
          {!editando && guardada ? (
            <div className="flex items-center justify-between gap-3 rounded-xl bg-warning-soft p-3">
              <p className="text-sm font-semibold">{preview}</p>
              {puedeEditar && <Button variant="ghost" className="h-8 px-3" onClick={() => setEditando(true)}>Cambiar</Button>}
            </div>
          ) : puedeEditar && (
            <>
              <div className="flex flex-wrap gap-2">
                {[['Hoy', 0], ['Mañana', 1], ['En 3 días', 3], ['En 1 semana', 7]].map(([l, d]) => (
                  <button key={l as string} onClick={() => rapido(d as number)} className="rounded-full border border-line bg-card px-3 py-1 text-xs font-semibold text-fg-2 hover:bg-hover">{l}</button>
                ))}
              </div>
              <Input type="date" aria-label="Fecha prometida" value={valor} onChange={e => setValor(e.target.value)} />
              {preview && <p className="text-xs text-fg-2">{preview}</p>}
              <div className="flex gap-2">
                <Button className="h-10 flex-1" loading={guardar.isPending} disabled={!valor} onClick={() => guardar.mutate(valor)}>Guardar promesa</Button>
                {guardada && <Button variant="secondary" className="h-10" loading={guardar.isPending} onClick={() => guardar.mutate(null)}>Quitar</Button>}
              </div>
            </>
          )}
        </>
      )}
    </Card>
  )
}
