import { useMutation } from '@tanstack/react-query'
import { Ban } from 'lucide-react'
import { useState } from 'react'
import { Button } from '../../../components/ui/Button'
import { Modal } from '../../../components/ui/Modal'
import { Textarea } from '../../../components/ui/Textarea'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { errMsg } from './comun'


export function RechazarModal({ open, pagoId, onClose, onDone }: { open: boolean; pagoId?: string; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [c, setC] = useState('')
  const rechazar = useMutation({
    mutationFn: () => api.rechazarPago(pagoId!, c.trim()),
    onSuccess: () => { toast('Comprobante rechazado', 'success'); setC(''); onDone() },
    onError: (e) => toast(errMsg(e, 'No se pudo rechazar'), 'error'),
  })
  return (
    <Modal open={open} title="Rechazar comprobante" onClose={onClose}>
      <div className="space-y-5">
        <p className="text-sm text-fg-2">El inquilino recibirá una notificación. Puedes indicarle el motivo.</p>
        <Textarea label="Motivo (opcional)" placeholder="Ej. La imagen está borrosa" value={c} onChange={e => setC(e.target.value)} />
        <div className="pt-2">
          <Button full variant="danger" loading={rechazar.isPending} icon={<Ban className="size-4" />} onClick={() => rechazar.mutate()}>Rechazar comprobante</Button>
        </div>
      </div>
    </Modal>
  )
}
