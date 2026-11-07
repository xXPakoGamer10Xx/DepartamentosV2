import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { anioActual, diasHasta, diaYDiasDelMes, edadMs, enDiasISO, periodoActual } from './fecha'

describe('fecha', () => {
  beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(new Date(2026, 1, 27, 10, 30)) })   // 27 feb 2026, hora local
  afterEach(() => vi.useRealTimers())

  it('anio y periodo actuales', () => {
    expect(anioActual()).toBe(2026)
    expect(periodoActual()).toMatch(/^2026-0[23]$/)   // toISOString usa UTC: puede caer en el mes siguiente
  })

  it('enDiasISO suma días en calendario local', () => {
    expect(enDiasISO(0)).toBe('2026-02-27')
    expect(enDiasISO(2)).toBe('2026-03-01')   // 2026 no es bisiesto
    expect(enDiasISO(-27)).toBe('2026-01-31')
  })

  it('diasHasta cuenta días de calendario, no horas', () => {
    expect(diasHasta('2026-02-27').diff).toBe(0)
    expect(diasHasta('2026-02-28').diff).toBe(1)
    expect(diasHasta('2026-02-26').diff).toBe(-1)
    expect(diasHasta('2026-03-29').diff).toBe(30)
  })

  it('diaYDiasDelMes', () => {
    expect(diaYDiasDelMes()).toEqual({ dia: 27, dim: 28 })
  })

  it('edadMs', () => {
    expect(edadMs(new Date(2026, 1, 27, 10, 29).toISOString())).toBe(60_000)
  })
})
