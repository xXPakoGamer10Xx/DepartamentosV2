import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Modal } from '../../../components/ui/Modal'
import { Select } from '../../../components/ui/Select'
import { Textarea } from '../../../components/ui/Textarea'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { hoyISO } from '../../../lib/cobranza'
import { num, periodoLabel } from '../../../lib/format'
import type { Abono } from '../../../lib/types'
import { errMsg } from './comun'


export function AbonoModal({ state, inquilinoId, metodoDefault, onClose, onSaved }: { state: { tipo: 'renta' | 'deposito'; abono?: Abono; periodo?: string; saldo?: number } | null; inquilinoId: string; metodoDefault: string; onClose: () => void; onSaved: () => void }) {
  return (
    <Modal open={!!state} title={`${state?.abono ? 'Editar' : 'Registrar'} abono ${state?.tipo === 'deposito' ? 'al depósito' : 'a la renta'}${state?.periodo ? ` de ${periodoLabel(state.periodo)}` : ''}`} onClose={onClose}>
      {state && <AbonoForm key={state.abono?.id ?? `${state.tipo}-${state.periodo ?? ''}`} state={state} inquilinoId={inquilinoId} metodoDefault={metodoDefault} onSaved={onSaved} />}
    </Modal>
  )
}


export function AbonoForm({ state, inquilinoId, metodoDefault, onSaved }: { state: { tipo: 'renta' | 'deposito'; abono?: Abono; periodo?: string; saldo?: number }; inquilinoId: string; metodoDefault: string; onSaved: () => void }) {
  const toast = useToast()
  const a = state.abono
  const [monto, setMonto] = useState(a ? String(num(a.monto)) : state.saldo && state.saldo > 0 ? String(state.saldo) : '')
  const [fechaV, setFechaV] = useState(a ? String(a.fecha).slice(0, 10) : hoyISO())
  const [metodo, setMetodo] = useState(a?.metodo ?? metodoDefault)
  const [nota, setNota] = useState(a?.nota ?? '')
  const [error, setError] = useState('')

  const guardar = useMutation({
    mutationFn: () => {
      const m = parseFloat(monto)
      const body = { monto: m, fecha: fechaV || undefined, metodo, nota: nota.trim() || undefined }
      if (state.tipo === 'deposito') return a ? api.editarAbonoDeposito(a.id, body) : api.registrarAbonoDeposito({ inquilino_id: inquilinoId, ...body })
      return a ? api.editarAbono(a.id, body) : api.registrarAbono({ inquilino_id: inquilinoId, periodo: state.periodo, ...body })
    },
    onSuccess: () => { toast('Abono guardado', 'success'); onSaved() },
    onError: (e) => setError(errMsg(e, 'No se pudo guardar el abono')),
  })
  const submit = () => {
    if (!(parseFloat(monto) > 0)) return setError('Ingresa un monto válido mayor a cero')
    if (!/^\d{4}-\d{2}-\d{2}$/.test(fechaV)) return setError('Indica la fecha del abono')
    setError(''); guardar.mutate()
  }
  return (
    <div className="space-y-5">
      <Input label="Monto *" type="number" inputMode="decimal" min={0} step="0.01" value={monto} autoFocus onChange={e => { setError(''); setMonto(e.target.value) }} />
      <div className="grid grid-cols-2 gap-3">
        <Input label="Fecha" type="date" value={fechaV} max={hoyISO()} onChange={e => setFechaV(e.target.value)} />
        <Select label="Método" value={metodo} onChange={e => setMetodo(e.target.value)}><option value="efectivo">Efectivo</option><option value="transferencia">Transferencia</option></Select>
      </div>
      <Textarea label="Nota (opcional)" className="min-h-20" value={nota} onChange={e => setNota(e.target.value)} />
      {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
      <div className="pt-2">
        <Button full loading={guardar.isPending} onClick={submit}>Guardar abono</Button>
      </div>
    </div>
  )
}
