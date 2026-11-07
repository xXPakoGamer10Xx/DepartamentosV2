// Depósito (compensación) diferido: el backend usa estas fechas ISO para recordatorios y para el contrato.
const iso = (y: number, m0: number, d: number) => `${y}-${String(m0 + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`
const at = (isoDate?: string) => (isoDate ? new Date(`${isoDate}T12:00:00`) : new Date())
const lastDay = (y: number, m0: number) => new Date(y, m0 + 1, 0).getDate()

export const toISO = (d: Date) => iso(d.getFullYear(), d.getMonth(), d.getDate())
export const addOneYear = (d: Date) => { const n = new Date(d); n.setFullYear(d.getFullYear() + 1); return n }

const fmtCorta = (s: string) => at(s).toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' })

/** Dos quincenas (15 y fin de mes, tope 30) siguientes al inicio del contrato. */
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

/** 'fechas' = el admin elige cada fecha de pago en el calendario; 'personalizado' = pagos mensuales el mismo día. */
export type DepositoTipo = 'ninguno' | 'quincenas' | 'personalizado' | 'fechas'

export const MAX_PAGOS_DEPOSITO = 24

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
  return { fechas: [...llenas].sort() }
}

export function notaDeposito(tipo: DepositoTipo, fechas: string[]): string {
  if (tipo === 'ninguno') return ''
  if (tipo === 'fechas') return fechas.length === 0 ? '' : `Se pagará la compensación en ${fechas.length} pago${fechas.length > 1 ? 's' : ''}: ${fechas.map(fmtCorta).join(', ')}.`
  if (fechas.length === 0) return tipo === 'quincenas' ? 'Se pagará la compensación en 2 quincenas, los días 15 y 30 del mes en curso.' : ''
  if (tipo === 'quincenas') return `Se pagará la compensación en 2 quincenas: ${fechas.map(fmtCorta).join(' y ')}.`
  const dia = at(fechas[0]).getDate()
  return `Se pagará la compensación en ${fechas.length} pago${fechas.length > 1 ? 's' : ''}, el día ${dia} de cada mes: ${fechas.map(fmtCorta).join(', ')}.`
}

/** Nota generada automáticamente (para no pisar notas que el admin haya escrito a mano). */
export const esNotaAutomatica = (s: string) => !s.trim() || s.startsWith('Se pagará la compensación')

export const buildFechaPago = (day: number | string) => `${String(day).padStart(2, '0')} de cada mes`
export function normalizeFechaPago(v: string): string {
  const t = v.trim()
  if (!t || /de cada mes/i.test(t)) return t
  const m = t.match(/(\d{1,2})/)
  return m ? buildFechaPago(m[1]) : t
}
