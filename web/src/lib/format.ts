import { nombreVisible } from './nombre'
/** NUMERIC de Postgres llega como string. */
export const num = (v: string | number | null | undefined): number => {
  const n = typeof v === 'number' ? v : parseFloat(v ?? '')
  return Number.isFinite(n) ? n : 0
}

const mxn = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' })
export const money = (v: string | number | null | undefined) => mxn.format(num(v))

/** "2026-03-10T06:00:00.000Z" o "2026-03-10" → "10 mar 2026". */
export function fecha(v: string | null | undefined): string {
  if (!v) return '—'
  const d = /^\d{4}-\d{2}-\d{2}$/.test(v) ? new Date(`${v}T12:00:00`) : new Date(v)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' })
}

export function fechaHora(v: string | null | undefined): string {
  if (!v) return '—'
  const d = new Date(v)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('es-MX', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' })
}

export function haceCuanto(v: string): string {
  const s = Math.round((Date.now() - new Date(v).getTime()) / 1000)
  if (s < 60) return 'ahora'
  if (s < 3600) return `hace ${Math.floor(s / 60)} min`
  if (s < 86400) return `hace ${Math.floor(s / 3600)} h`
  if (s < 86400 * 7) return `hace ${Math.floor(s / 86400)} d`
  return fecha(v)
}

export const iniciales = (nombre: string) =>
  nombreVisible(nombre).split(' ').filter(Boolean).slice(0, 2).map(w => w[0]).join('').toUpperCase()

/** "2026-10" → "octubre de 2026". */
export function periodoLabel(periodo: string): string {
  const [y, m] = periodo.split('-').map(Number)
  if (!y || !m) return periodo
  return new Date(y, m - 1, 1).toLocaleDateString('es-MX', { month: 'long', year: 'numeric' })
}
