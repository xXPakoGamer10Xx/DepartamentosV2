import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Modal } from '../../../components/ui/Modal'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { errMsg } from './comun'


export function CuotaModal({ open, inquilinoId, onClose, onSaved }: { open: boolean; inquilinoId: string; onClose: () => void; onSaved: () => void }) {
  const toast = useToast()
  const [concepto, setConcepto] = useState('')
  const [monto, setMonto] = useState('')
  const [error, setError] = useState('')
  const crear = useMutation({
    mutationFn: () => api.createCuota({ inquilino_id: inquilinoId, concepto: concepto.trim(), monto: parseFloat(monto) }),
    onSuccess: () => { toast('Cargo agregado', 'success'); setConcepto(''); setMonto(''); onSaved() },
    onError: (e) => setError(errMsg(e, 'No se pudo agregar el cargo')),
  })
  const submit = () => {
    if (!concepto.trim()) return setError('El concepto es requerido')
    if (!(parseFloat(monto) > 0)) return setError('Ingresa un monto válido mayor a cero')
    setError(''); crear.mutate()
  }
  return (
    <Modal open={open} title="Nuevo cargo extra" onClose={onClose}>
      <div className="space-y-5">
        <Input label="Concepto *" placeholder="Ej. Reparación de llave" value={concepto} onChange={e => { setError(''); setConcepto(e.target.value) }} />
        <Input label="Monto *" type="number" inputMode="decimal" min={0} step="0.01" value={monto} onChange={e => { setError(''); setMonto(e.target.value) }} />
        {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
        <div className="pt-2">
          <Button full loading={crear.isPending} onClick={submit}>Agregar cargo</Button>
        </div>
      </div>
    </Modal>
  )
}
