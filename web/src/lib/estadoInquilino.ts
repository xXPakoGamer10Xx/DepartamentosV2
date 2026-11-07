import type { RowState } from './cobranza'
import type { Inquilino } from './types'

type Tone = 'success' | 'warning' | 'danger' | 'primary' | 'neutral'

export function vencimiento(fecha?: string | null) {
  if (!fecha) return null
  const dias = Math.ceil((new Date(fecha).getTime() - Date.now()) / 86_400_000)
  if (dias < 0) return { expired: true, label: 'Contrato vencido', tone: 'danger' as Tone, short: null as string | null }
  if (dias <= 30) return { expired: false, label: `Vence en ${dias} días`, tone: 'warning' as Tone, short: `Vence en ${dias}d` }
  const meses = Math.round(dias / 30)
  return { expired: false, label: `Vence en ${meses} mes${meses !== 1 ? 'es' : ''}`, tone: 'success' as Tone, short: null }
}

const PAGO_CHIP: Record<RowState, { label: string; tone: Tone }> = {
  pagado: { label: 'Pagado', tone: 'success' }, revision: { label: 'En revisión', tone: 'warning' },
  atrasado: { label: 'Atrasado', tone: 'danger' }, pendiente: { label: 'Por pagar', tone: 'neutral' },
}

/** Etiqueta de estado: el estado de PAGO del mes si se conoce; si no, solo "Activo" (no se afirma que esté al corriente). */
export function estadoChip(i: Pick<Inquilino, 'estado' | 'fecha_termino'>, pago?: RowState): { label: string; tone: Tone } {
  if (i.estado !== 'activo') return { label: 'Archivado', tone: 'danger' }
  if (pago) return PAGO_CHIP[pago]
  if (vencimiento(i.fecha_termino)?.expired) return { label: 'Contrato vencido', tone: 'danger' }
  return { label: 'Activo', tone: 'neutral' }
}
