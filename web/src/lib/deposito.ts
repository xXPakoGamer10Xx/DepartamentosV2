// Depósito: modalidades de pago, cálculos de fechas/cuotas y notas automáticas para contratos.
const iso = (y: number, m0: number, d: number) => `${y}-${String(m0 + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`
const at = (isoDate?: string) => (isoDate ? new Date(`${isoDate}T12:00:00`) : new Date())
const lastDay = (y: number, m0: number) => new Date(y, m0 + 1, 0).getDate()
const addDays = (d: Date, days: number) => {
  const n = new Date(d)
  n.setDate(n.getDate() + days)
  return n
}
const round2 = (v: number) => Math.round(v * 100) / 100

export const toISO = (d: Date) => iso(d.getFullYear(), d.getMonth(), d.getDate())
export const addOneYear = (d: Date) => { const n = new Date(d); n.setFullYear(d.getFullYear() + 1); return n }

const fmtCorta = (s: string) => at(s).toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' })
const fmtDinero = (n: number) =>
  `$${round2(n).toLocaleString('es-MX', { minimumFractionDigits: n % 1 === 0 ? 0 : 2, maximumFractionDigits: 2 })}`

export type DepositoTipo =
  | 'contado_beneficio'
  | 'diferido_mensual'
  | 'quincenal'
  | 'semanal'
  | 'fechas'
  | 'ninguno'
  | 'quincenas'
  | 'personalizado'

export const PLAZOS_DIFERIDO = [3, 6, 9, 12] as const
export const MONTO_SEMANAL_DEFAULT = 1000
export const MAX_PAGOS_DEPOSITO = 52

/** Dos quincenas (15 y fin de mes, tope 30) siguientes al inicio del contrato (modalidad histórica). */
export function quincenaFechas(startISO?: string): string[] {
  const s = at(startISO)
  const y = s.getFullYear(), m = s.getMonth(), day = s.getDate()
  const fin = (yy: number, mm: number) => Math.min(30, lastDay(yy, mm))
  const next = (yy: number, mm: number) => (mm === 11 ? { y: yy + 1, m: 0 } : { y: yy, m: mm + 1 })
  const finEste = fin(y, m)
  if (day < 15) return [iso(y, m, 15), iso(y, m, finEste)]
  if (day < finEste) { const n = next(y, m); return [iso(y, m, finEste), iso(n.y, n.m, 15)] }
  const n = next(y, m)
  return [iso(n.y, n.m, 15), iso(n.y, n.m, fin(n.y, n.m))]
}

/** Opción 2 · Pago quincenal intercalado: exactamente +15 días y +45 días calendario desde `startISO`. */
export function fechasQuincenalIntercalado(startISO?: string): [string, string] {
  const s = at(startISO)
  return [toISO(addDays(s, 15)), toISO(addDays(s, 45))]
}

/** Un mes calendario después de `startISO` (ajusta al último día si el mes destino tiene menos días). */
export function mesSiguienteISO(startISO?: string): string {
  const s = at(startISO)
  const mm = s.getMonth() + 1
  const yy = s.getFullYear() + Math.floor(mm / 12)
  const mo = mm % 12
  return iso(yy, mo, Math.min(s.getDate(), lastDay(yy, mo)))
}

/** `nPagos` pagos el mismo día `dia` de meses consecutivos (ajusta a fin de mes si el día no existe). */
export function pagosMensuales(dia: number, nPagos: number, startISO?: string): string[] {
  if (!dia || dia < 1 || dia > 31 || !nPagos || nPagos < 1) return []
  const s = at(startISO)
  const m = s.getMonth() + (dia < s.getDate() ? 1 : 0)
  const y = s.getFullYear()
  return Array.from({ length: nPagos }, (_, i) => {
    const mm = m + i
    const yy = y + Math.floor(mm / 12)
    const mo = ((mm % 12) + 12) % 12
    return iso(yy, mo, Math.min(dia, lastDay(yy, mo)))
  })
}

/** Cuota mensual del depósito redondeada a 2 decimales. */
export function cuotaMensualDeposito(deposito: number, nPagos: number): number {
  if (!(deposito > 0) || !(nPagos > 0)) return 0
  return round2(deposito / nPagos)
}

export interface PlanSemanalEstimado {
  equivMensual: number
  excedenteMensual: number
  excedenteSemanal: number
  semanas: number
  fechas: string[]
  insuficiente: boolean
  minimoSugerido: number
}

/** Estima cuántas semanas toma completar el depósito pagando `montoSemanal` cada 7 días (cubre renta primero). */
export function estimarPlanSemanal(
  renta: number,
  deposito: number,
  montoSemanal: number,
  startISO?: string,
): PlanSemanalEstimado {
  const r = Math.max(0, Number.isFinite(renta) ? renta : 0)
  const d = Math.max(0, Number.isFinite(deposito) ? deposito : 0)
  const m = Math.max(0, Number.isFinite(montoSemanal) ? montoSemanal : 0)
  const equivMensualRaw = (m * 52) / 12
  const insuficiente = d > 0 && r > 0 && equivMensualRaw <= r + 50
  const minimoSugerido = r > 0 ? Math.ceil(((r * 12) / 52 + 100) / 50) * 50 : MONTO_SEMANAL_DEFAULT
  const excedenteMensualRaw = Math.max(0, equivMensualRaw - r)
  const excedenteSemanalRaw = (excedenteMensualRaw * 12) / 52
  const semanas = excedenteSemanalRaw > 0 && d > 0
    ? Math.min(MAX_PAGOS_DEPOSITO, Math.max(1, Math.ceil(d / excedenteSemanalRaw)))
    : 0
  const base = at(startISO)
  const fechas = Array.from({ length: Math.min(semanas, MAX_PAGOS_DEPOSITO) }, (_, i) => toISO(addDays(base, (i + 1) * 7)))

  return {
    equivMensual: round2(equivMensualRaw),
    excedenteMensual: round2(excedenteMensualRaw),
    excedenteSemanal: round2(excedenteSemanalRaw),
    semanas,
    fechas,
    insuficiente,
    minimoSugerido,
  }
}

/** Cuota sugerida al registrar un abono al depósito según la modalidad pactada, con tope en el saldo pendiente. */
export function cuotaSugeridaDeposito(
  depositoTotal: number,
  depositoSaldo: number,
  tipo?: string | null,
  fechas?: string[] | null,
): number {
  if (!(depositoSaldo > 0)) return 0
  const saldo = round2(depositoSaldo)
  if (!(depositoTotal > 0)) return saldo

  if ((tipo === 'diferido_mensual' || tipo === 'personalizado' || tipo === 'fechas') && fechas && fechas.length > 0) {
    return round2(Math.min(saldo, cuotaMensualDeposito(depositoTotal, fechas.length)))
  }
  if (tipo === 'quincenal' || tipo === 'quincenas') {
    return round2(Math.min(saldo, cuotaMensualDeposito(depositoTotal, 2)))
  }
  return saldo
}

/** Etiqueta legible de la modalidad de depósito para detalle de inquilino, cobranza y vista del inquilino. */
export function etiquetaModalidadDeposito(
  tipo?: string | null,
  fechasCount?: number,
  montoSemanal?: number | null,
): string {
  switch (tipo) {
    case 'contado_beneficio':
      return 'Contado · Beneficio Mes 13'
    case 'diferido_mensual':
      return fechasCount && fechasCount > 0 ? `Pago diferido (${fechasCount} meses)` : 'Pago diferido mensual'
    case 'quincenal':
      return 'Pago quincenal (50% y 50%)'
    case 'semanal':
      return montoSemanal && montoSemanal > 0
        ? `Pago semanal (${fmtDinero(montoSemanal)}/sem)`
        : 'Pago semanal'
    case 'fechas':
      return fechasCount && fechasCount > 0
        ? `Fechas libres (${fechasCount} pago${fechasCount > 1 ? 's' : ''})`
        : 'Fechas libres'
    case 'quincenas':
      return 'Dos quincenas'
    case 'personalizado':
      return fechasCount && fechasCount > 0 ? `Pagos mensuales (${fechasCount})` : 'Pagos mensuales'
    default:
      return 'Al inicio del contrato'
  }
}

const esFechaReal = (v: string) => {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v)
  if (!m) return false
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return d.getFullYear() === Number(m[1]) && d.getMonth() === Number(m[2]) - 1 && d.getDate() === Number(m[3])
}

/** Limpia las fechas elegidas: quita renglones vacíos, valida, rechaza repetidas y las ordena. */
export function normalizarFechas(entrada: string[]): { fechas: string[]; error?: string } {
  const llenas = entrada.map(x => x.trim()).filter(Boolean)
  if (llenas.length === 0) return { fechas: [], error: 'Elige al menos una fecha de pago del depósito.' }
  if (llenas.length > MAX_PAGOS_DEPOSITO) return { fechas: [], error: `El depósito admite hasta ${MAX_PAGOS_DEPOSITO} pagos.` }
  if (llenas.some(x => !esFechaReal(x))) return { fechas: [], error: 'Hay una fecha del depósito que no es válida.' }
  if (new Set(llenas).size !== llenas.length) return { fechas: [], error: 'Hay fechas del depósito repetidas.' }
  return { fechas: llenas.toSorted() }
}

export function notaDeposito(
  tipo: DepositoTipo,
  fechas: string[],
  opts?: { deposito?: number; montoSemanal?: number },
): string {
  if (tipo === 'ninguno') return ''
  if (tipo === 'contado_beneficio') {
    return 'Depósito de contado al inicio del contrato (Opción 3). Beneficio Mes 13: al cumplir el año y avisar con 15 días de anticipación, el mes 13 se cubre con el depósito. No reembolsable.'
  }
  if (tipo === 'semanal') {
    const monto = opts?.montoSemanal && opts.montoSemanal > 0 ? opts.montoSemanal : MONTO_SEMANAL_DEFAULT
    const plazo = fechas.length > 0 ? ` (aprox. ${fechas.length} semana${fechas.length > 1 ? 's' : ''})` : ''
    return `Se pagará el depósito mediante abono semanal de ${fmtDinero(monto)}${plazo}: cada pago cubre primero la renta y el excedente abona al depósito.`
  }
  if (tipo === 'quincenal') {
    if (fechas.length === 0) return 'Se pagará el depósito en 2 exhibiciones quincenales (50% a los 15 días y 50% a los 45 días).'
    const cuotaTxt = opts?.deposito && opts.deposito > 0 ? ` de ${fmtDinero(cuotaMensualDeposito(opts.deposito, 2))} c/u` : ''
    return `Se pagará el depósito en 2 exhibiciones quincenales (50% y 50%${cuotaTxt}): ${fechas.map(fmtCorta).join(' y ')}.`
  }
  if (tipo === 'diferido_mensual') {
    if (fechas.length === 0) return ''
    const dia = at(fechas[0]).getDate()
    const cuotaTxt = opts?.deposito && opts.deposito > 0 ? ` (${fmtDinero(cuotaMensualDeposito(opts.deposito, fechas.length))}/mes)` : ''
    return `Se pagará el depósito en ${fechas.length} pago${fechas.length > 1 ? 's' : ''} mensuales${cuotaTxt}, el día ${dia} de cada mes: ${fechas.map(fmtCorta).join(', ')}.`
  }
  if (tipo === 'fechas') {
    return fechas.length === 0 ? '' : `Se pagará el depósito en ${fechas.length} pago${fechas.length > 1 ? 's' : ''}: ${fechas.map(fmtCorta).join(', ')}.`
  }
  if (fechas.length === 0) return tipo === 'quincenas' ? 'Se pagará el depósito en 2 quincenas, los días 15 y 30 del mes en curso.' : ''
  if (tipo === 'quincenas') return `Se pagará el depósito en 2 quincenas: ${fechas.map(fmtCorta).join(' y ')}.`
  const dia = at(fechas[0]).getDate()
  return `Se pagará el depósito en ${fechas.length} pago${fechas.length > 1 ? 's' : ''}, el día ${dia} de cada mes: ${fechas.map(fmtCorta).join(', ')}.`
}

const PREFIJOS_NOTA_AUTO = [
  'Se pagará el depósito',
  'Se pagará la compensación',
  'Depósito de contado al inicio',
]

/** Nota generada automáticamente (para no pisar notas que el admin haya escrito a mano). */
export const esNotaAutomatica = (s: string) => {
  const t = s.trim()
  return !t || PREFIJOS_NOTA_AUTO.some(p => t.startsWith(p))
}

export const buildFechaPago = (day: number | string) => `${String(day).padStart(2, '0')} de cada mes`
export function normalizeFechaPago(v: string): string {
  const t = v.trim()
  if (!t || /de cada mes/i.test(t)) return t
  const m = t.match(/(\d{1,2})/)
  return m ? buildFechaPago(m[1]) : t
}
