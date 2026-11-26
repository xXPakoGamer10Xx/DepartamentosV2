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
import { money, num, periodoLabel } from '../../../lib/format'
import type { Abono, AbonoSemanalResult } from '../../../lib/types'
import { errMsg } from './comun'

export type AbonoModalState = {
  tipo: 'renta' | 'deposito' | 'semanal'
  abono?: Abono
  periodo?: string
  saldo?: number
}

export function AbonoModal({
  state, inquilinoId, metodoDefault, onClose, onSaved,
}: {
  state: AbonoModalState | null; inquilinoId: string; metodoDefault: string; onClose: () => void; onSaved: () => void
}) {
  const title = state?.tipo === 'semanal'
    ? 'Registrar pago semanal'
    : `${state?.abono ? 'Editar' : 'Registrar'} abono ${state?.tipo === 'deposito' ? 'al depósito' : 'a la renta'}${state?.periodo ? ` de ${periodoLabel(state.periodo)}` : ''}`

  return (
    <Modal open={!!state} title={title} onClose={onClose}>
      {state && (
        <AbonoForm
          key={state.abono?.id ?? `${state.tipo}-${state.periodo ?? ''}-${state.saldo ?? ''}`}
          state={state}
          inquilinoId={inquilinoId}
          metodoDefault={metodoDefault}
          onSaved={onSaved}
        />
      )}
    </Modal>
  )
}

function resumenSemanal(d: AbonoSemanalResult): string {
  const partes: string[] = []
  if (d.aplicado_renta?.length) {
    partes.push(
      `Renta: ${d.aplicado_renta.map(x => `${periodoLabel(x.periodo)} (${money(x.monto)}${x.completo ? ' · completo' : ''})`).join(', ')}`,
    )
  } else if (d.total_aplicado_renta > 0) {
    partes.push(`Renta: ${money(d.total_aplicado_renta)}`)
  }
  if (d.aplicado_deposito > 0) {
    partes.push(`Depósito: ${money(d.aplicado_deposito)} (pendiente ${money(d.deposito_saldo)})`)
  }
  return partes.length ? `Pago semanal aplicado — ${partes.join(' · ')}` : 'Pago semanal registrado'
}

export function AbonoForm({
  state, inquilinoId, metodoDefault, onSaved,
}: {
  state: AbonoModalState; inquilinoId: string; metodoDefault: string; onSaved: () => void
}) {
  const toast = useToast()
  const a = state.abono
  const [hoy] = useState(() => hoyISO())
  const [monto, setMonto] = useState(a ? String(num(a.monto)) : state.saldo && state.saldo > 0 ? String(state.saldo) : '')
  const [fechaV, setFechaV] = useState(a ? String(a.fecha).slice(0, 10) : hoy)
  const [metodo, setMetodo] = useState(a?.metodo ?? metodoDefault)
  const [nota, setNota] = useState(a?.nota ?? '')
  const [error, setError] = useState('')

  type AbonoMutationRes =
    | Awaited<ReturnType<typeof api.registrarAbonoSemanal>>
    | Awaited<ReturnType<typeof api.registrarAbonoDeposito>>
    | Awaited<ReturnType<typeof api.editarAbonoDeposito>>
    | Awaited<ReturnType<typeof api.registrarAbono>>
    | Awaited<ReturnType<typeof api.editarAbono>>

  const guardar = useMutation<AbonoMutationRes>({
    mutationFn: async (): Promise<AbonoMutationRes> => {
      const m = parseFloat(monto)
      const body = { monto: m, fecha: fechaV || undefined, metodo, nota: nota.trim() || undefined }
      if (state.tipo === 'semanal') return api.registrarAbonoSemanal({ inquilino_id: inquilinoId, ...body })
      if (state.tipo === 'deposito') return a ? api.editarAbonoDeposito(a.id, body) : api.registrarAbonoDeposito({ inquilino_id: inquilinoId, ...body })
      if (a) return api.editarAbono(a.id, body)
      // Sin un mes elegido, el abono paga primero el mes más antiguo con adeudo (un abono al mes actual no baja un atraso anterior).
      return api.registrarAbono({ inquilino_id: inquilinoId, periodo: state.periodo, aplicar_a_deuda_antigua: !state.periodo, ...body })
    },
    onSuccess: (res) => {
      if (state.tipo === 'semanal' && res.data && 'aplicado_deposito' in res.data) {
        toast(resumenSemanal(res.data as AbonoSemanalResult), 'success')
      } else {
        const aplicado = 'aplicado' in (res.data ?? {}) ? (res.data as { aplicado?: { periodo: string; monto: number }[] }).aplicado : undefined
        toast(aplicado?.length ? `Abono aplicado a ${aplicado.map(x => `${periodoLabel(x.periodo)} (${money(x.monto)})`).join(' y ')}` : 'Abono guardado', 'success')
      }
      onSaved()
    },
    onError: (e) => setError(errMsg(e, 'No se pudo guardar el abono')),
  })
  const submit = () => {
    if (!(parseFloat(monto) > 0)) return setError('Ingresa un monto válido mayor a cero')
    if (!/^\d{4}-\d{2}-\d{2}$/.test(fechaV)) return setError('Indica la fecha del abono')
    setError(''); guardar.mutate()
  }
  return (
    <div className="space-y-5">
      {state.tipo === 'semanal' && (
        <p className="rounded-xl border border-primary/25 bg-primary-soft px-4 py-3 text-xs leading-relaxed text-fg-2">
          Este pago cubrirá primero la <b className="text-fg">renta pendiente</b> (empezando por el mes más antiguo) y el <b className="text-fg">excedente irá automáticamente al depósito</b>.
        </p>
      )}
      <Input label="Monto *" type="number" inputMode="decimal" min={0} step="0.01" className="tabular-nums" value={monto} autoFocus onChange={e => { setError(''); setMonto(e.target.value) }} />
      <div className="grid grid-cols-2 gap-3">
        <Input label="Fecha" type="date" value={fechaV} max={hoy} onChange={e => setFechaV(e.target.value)} />
        <Select label="Método" value={metodo} onChange={e => setMetodo(e.target.value)}><option value="efectivo">Efectivo</option><option value="transferencia">Transferencia</option></Select>
      </div>
      {state.tipo === 'renta' && !a && !state.periodo && (
        <p className="rounded-xl bg-primary-soft px-4 py-3 text-xs text-fg-2">Se aplica primero al mes más antiguo con adeudo y, si sobra, al siguiente. Para abonar a un mes en particular usa el botón <b>Abonar</b> junto a ese mes.</p>
      )}
      <Textarea label="Nota (opcional)" className="min-h-20" value={nota} onChange={e => setNota(e.target.value)} />
      {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}
      <div className="pt-2">
        <Button full loading={guardar.isPending} onClick={submit}>
          {state.tipo === 'semanal' ? 'Registrar pago semanal' : 'Guardar abono'}
        </Button>
      </div>
    </div>
  )
}
