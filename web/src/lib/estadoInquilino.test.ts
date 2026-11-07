import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { estadoChip, vencimiento } from './estadoInquilino'

describe('vencimiento', () => {
  beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(new Date('2026-06-01T12:00:00Z')) })
  afterEach(() => vi.useRealTimers())

  it('sin fecha no hay nada que mostrar', () => { expect(vencimiento(null)).toBeNull() })
  it('un contrato pasado está vencido', () => {
    expect(vencimiento('2026-05-01')).toMatchObject({ expired: true, tone: 'danger' })
  })
  it('vence pronto (≤ 30 días) en advertencia', () => {
    expect(vencimiento('2026-06-11T12:00:00Z')).toMatchObject({ expired: false, tone: 'warning', label: 'Vence en 10 días' })
  })
  it('vence lejos en meses', () => {
    expect(vencimiento('2026-12-01T12:00:00Z')).toMatchObject({ tone: 'success', label: 'Vence en 6 meses' })
  })
})

describe('estadoChip', () => {
  it('un inquilino de baja está archivado', () => {
    expect(estadoChip({ estado: 'inactivo', fecha_termino: '2030-01-01' }).label).toBe('Archivado')
  })
  it('usa el estado de pago del mes si se conoce', () => {
    expect(estadoChip({ estado: 'activo', fecha_termino: '2030-01-01' }, 'atrasado')).toEqual({ label: 'Atrasado', tone: 'danger' })
  })
  it('sin estado de pago solo dice Activo (no afirma que esté al corriente)', () => {
    expect(estadoChip({ estado: 'activo', fecha_termino: '2099-01-01' }).label).toBe('Activo')
  })
})
