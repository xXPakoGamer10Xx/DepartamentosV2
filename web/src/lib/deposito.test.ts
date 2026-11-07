import { describe, expect, it } from 'vitest'
import { esNotaAutomatica, normalizarFechas, notaDeposito, quincenaFechas, pagosMensuales } from './deposito'

describe('normalizarFechas (depósito con fechas elegidas en el calendario)', () => {
  it('ordena de la más próxima a la más lejana', () => {
    expect(normalizarFechas(['2026-12-15', '2026-11-30', '2027-01-10']).fechas).toEqual(['2026-11-30', '2026-12-15', '2027-01-10'])
  })
  it('pide al menos una fecha', () => {
    expect(normalizarFechas([]).error).toMatch(/al menos una fecha/i)
    expect(normalizarFechas(['', '']).error).toMatch(/al menos una fecha/i)
  })
  it('ignora los renglones vacíos', () => {
    expect(normalizarFechas(['2026-11-30', '']).fechas).toEqual(['2026-11-30'])
  })
  it('rechaza fechas repetidas', () => {
    expect(normalizarFechas(['2026-11-30', '2026-11-30']).error).toMatch(/repetid/i)
  })
  it('rechaza fechas que no existen', () => {
    expect(normalizarFechas(['2026-02-30']).error).toMatch(/válid/i)
    expect(normalizarFechas(['hoy']).error).toMatch(/válid/i)
  })
  it('limita a 24 pagos', () => {
    const muchas = Array.from({ length: 25 }, (_, i) => `2027-${String((i % 12) + 1).padStart(2, '0')}-${String(i < 12 ? 10 : 20)}`)
    expect(normalizarFechas(muchas).error).toMatch(/24/)
  })
})

describe('notaDeposito', () => {
  it('con fechas elegidas lista cada pago', () => {
    const n = notaDeposito('fechas', ['2026-11-30', '2026-12-15'])
    expect(n).toMatch(/^Se pagará la compensación en 2 pagos:/)
    expect(esNotaAutomatica(n)).toBe(true)   // para no pisar una nota escrita a mano
  })
  it('con un solo pago lo dice en singular', () => {
    expect(notaDeposito('fechas', ['2026-11-30'])).toMatch(/en 1 pago:/)
  })
  it('sin fechas aún no escribe nada', () => {
    expect(notaDeposito('fechas', [])).toBe('')
  })
  it('conserva las otras modalidades', () => {
    expect(notaDeposito('ninguno', [])).toBe('')
    expect(notaDeposito('quincenas', quincenaFechas('2026-11-07'))).toMatch(/2 quincenas/)
    expect(notaDeposito('personalizado', pagosMensuales(5, 3, '2026-11-07'))).toMatch(/3 pagos, el día 5 de cada mes/)
  })
})
