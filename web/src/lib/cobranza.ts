import type { Numeric } from './types'
import { num } from './format'

/** Umbral para ignorar centavos de redondeo al decidir si hay deuda. */
export const EPS = 0.5

export type RowState = 'pagado' | 'revision' | 'atrasado' | 'pendiente'

export interface EstadoActual {
  inquilino_id: string; pago_id: string | null; monto: Numeric | null
  confirmado: boolean; confirmado_en: string | null; rechazado: boolean
  comprobante_url: string | null; total_abonado: Numeric
}

export interface Saldo { total: number; vencida: number; periodoVencido: string | null; cuotasExtra: number; deposito: number }

/** `vencida` = meses cuya fecha de pago (+gracia) ya pasó ⇒ atrasado; `total` incluye el mes en curso. */
export function rowState(e: EstadoActual | undefined, s: Saldo | undefined): RowState {
  if (e?.confirmado) return 'pagado'
  if (e?.comprobante_url && !e.rechazado) return 'revision'
  if ((s?.vencida ?? 0) > EPS || e?.rechazado) return 'atrasado'
  return 'pendiente'
}

/** Día pactado de pago extraído del texto libre ("10 de cada mes"). */
export function diaPago(fechaPago?: string | null): number | null {
  const m = String(fechaPago ?? '').match(/\d{1,2}/)
  if (!m) return null
  const d = parseInt(m[0], 10)
  return d >= 1 && d <= 31 ? d : null
}

export const hoyISO = () => {
  const d = new Date()
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10)
}

/** ¿Un pago confirmado todavía puede cancelarse? (24 h) */
export const puedeCancelar = (e?: EstadoActual) =>
  !!e?.pago_id && e.confirmado && !!e.confirmado_en && Date.now() - new Date(e.confirmado_en).getTime() < 24 * 3600 * 1000

export const toSaldo = (s: { deuda_total: Numeric; deuda_vencida: Numeric; periodo_vencido: string | null; deuda_cuotas_extra: Numeric; deuda_deposito: Numeric }): Saldo => ({
  total: num(s.deuda_total), vencida: num(s.deuda_vencida), periodoVencido: s.periodo_vencido,
  cuotasExtra: num(s.deuda_cuotas_extra), deposito: num(s.deuda_deposito),
})
