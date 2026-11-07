/** Lecturas del reloj fuera de los componentes (renderizar no debe llamar a Date directamente). */

export const anioActual = () => new Date().getFullYear()
export const periodoActual = () => new Date().toISOString().slice(0, 7)
export const mesActualLabel = () => new Date().toLocaleDateString('es-MX', { month: 'long', year: 'numeric' })

/** Milisegundos transcurridos desde `iso`. */
export const edadMs = (iso: string) => Date.now() - new Date(iso).getTime()

/** Fecha local `AAAA-MM-DD` dentro de `dias` días. */
export function enDiasISO(dias: number): string {
  const d = new Date(); d.setDate(d.getDate() + dias)
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10)
}

/** Días de calendario desde hoy hasta `iso` (negativo si ya pasó) y la fecha como objeto. */
export function diasHasta(iso: string): { diff: number; fecha: Date } {
  const fecha = new Date(`${iso}T12:00:00`); const hoy = new Date()
  hoy.setHours(0, 0, 0, 0); fecha.setHours(0, 0, 0, 0)
  return { diff: Math.round((fecha.getTime() - hoy.getTime()) / 86_400_000), fecha }
}

/** Día del mes y días del mes en curso. */
export function diaYDiasDelMes(): { dia: number; dim: number } {
  const hoy = new Date()
  return { dia: hoy.getDate(), dim: new Date(hoy.getFullYear(), hoy.getMonth() + 1, 0).getDate() }
}
