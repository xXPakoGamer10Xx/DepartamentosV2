import { describe, expect, it } from 'vitest'
import { diaPago, restanteMes, rowState, toSaldo, type EstadoActual, type Saldo } from './cobranza'

const estado = (o: Partial<EstadoActual> = {}): EstadoActual => ({
  inquilino_id: 'i1', pago_id: 'p1', monto: '5000.00', confirmado: false, confirmado_en: null,
  rechazado: false, tiene_comprobante: false, total_abonado: '0', ...o,
})
const saldo = (o: Partial<Saldo> = {}): Saldo => ({ total: 0, vencida: 0, periodoVencido: null, cuotasExtra: 0, deposito: 0, ...o })

describe('rowState', () => {
  it('pagado manda sobre todo lo demás', () => {
    expect(rowState(estado({ confirmado: true, tiene_comprobante: true }), saldo({ vencida: 900 }))).toBe('pagado')
  })
  it('un comprobante sin rechazar queda en revisión', () => {
    expect(rowState(estado({ tiene_comprobante: true }), saldo())).toBe('revision')
  })
  it('un comprobante rechazado ya no está en revisión y cuenta como atrasado', () => {
    expect(rowState(estado({ tiene_comprobante: true, rechazado: true }), saldo())).toBe('atrasado')
  })
  it('atrasado solo si hay deuda vencida real (ignora centavos)', () => {
    expect(rowState(estado(), saldo({ vencida: 0.3 }))).toBe('pendiente')
    expect(rowState(estado(), saldo({ vencida: 1200 }))).toBe('atrasado')
  })
  it('sin datos del mes es pendiente', () => {
    expect(rowState(undefined, undefined)).toBe('pendiente')
  })
})

describe('restanteMes', () => {
  it('descuenta lo abonado del monto del pago', () => {
    expect(restanteMes(estado({ total_abonado: '1500.00' }), 5000)).toBe(3500)
  })
  it('sin pago registrado falta la renta completa', () => {
    expect(restanteMes(undefined, 4200)).toBe(4200)
    expect(restanteMes(estado({ monto: null }), 4200)).toBe(4200)
  })
  it('un pago confirmado ya no debe nada y nunca baja de cero', () => {
    expect(restanteMes(estado({ confirmado: true }), 5000)).toBe(0)
    expect(restanteMes(estado({ total_abonado: '9000' }), 5000)).toBe(0)
  })
})

describe('diaPago', () => {
  it('extrae el día del texto libre', () => {
    expect(diaPago('10 de cada mes')).toBe(10)
    expect(diaPago('5')).toBe(5)
  })
  it('rechaza lo que no es un día del mes', () => {
    expect(diaPago('')).toBeNull()
    expect(diaPago(null)).toBeNull()
    expect(diaPago('mañana')).toBeNull()
    expect(diaPago('45')).toBeNull()
  })
})

describe('toSaldo', () => {
  it('convierte los numéricos de Postgres (texto) a números', () => {
    expect(toSaldo({ deuda_total: '9000.00', deuda_vencida: '5000.00', periodo_vencido: '2026-10', deuda_cuotas_extra: '150', deuda_deposito: '0.00' }))
      .toEqual({ total: 9000, vencida: 5000, periodoVencido: '2026-10', cuotasExtra: 150, deposito: 0 })
  })
})
