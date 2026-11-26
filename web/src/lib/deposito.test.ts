import { describe, expect, it } from 'vitest'
import {
  cuotaMensualDeposito,
  cuotaSugeridaDeposito,
  esNotaAutomatica,
  estimarPlanSemanal,
  etiquetaModalidadDeposito,
  fechasQuincenalIntercalado,
  MAX_PAGOS_DEPOSITO,
  mesSiguienteISO,
  MONTO_SEMANAL_DEFAULT,
  normalizarFechas,
  notaDeposito,
  pagosMensuales,
  PLAZOS_DIFERIDO,
  quincenaFechas,
} from './deposito'

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
  it('limita a MAX_PAGOS_DEPOSITO (52 pagos)', () => {
    expect(MAX_PAGOS_DEPOSITO).toBe(52)
    const base = new Date('2027-01-01T12:00:00')
    const muchas = Array.from({ length: MAX_PAGOS_DEPOSITO + 1 }, (_, i) => {
      const d = new Date(base)
      d.setDate(d.getDate() + i * 7)
      return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    })
    expect(normalizarFechas(muchas).error).toMatch(/52/)
  })
})

describe('Opción 1 · Pago diferido (3, 6, 9, 12 meses)', () => {
  it('expone los plazos oficiales 3, 6, 9 y 12', () => {
    expect(PLAZOS_DIFERIDO).toEqual([3, 6, 9, 12])
  })
  it('genera fechas mensuales consecutivas para cada plazo oficial', () => {
    for (const plazo of PLAZOS_DIFERIDO) {
      const fechas = pagosMensuales(5, plazo, '2026-11-01')
      expect(fechas).toHaveLength(plazo)
      expect(fechas[0]).toBe('2026-11-05')
    }
    // Ajuste de fin de mes (31 -> 28 en febrero no bisiesto)
    expect(pagosMensuales(31, 3, '2026-01-31')).toEqual(['2026-01-31', '2026-02-28', '2026-03-31'])
  })
  it('calcula la cuota mensual redondeada a 2 decimales', () => {
    expect(cuotaMensualDeposito(3600, 3)).toBe(1200)
    expect(cuotaMensualDeposito(3500, 3)).toBe(1166.67)
    expect(cuotaMensualDeposito(3500, 6)).toBe(583.33)
    expect(cuotaMensualDeposito(3500, 9)).toBe(388.89)
    expect(cuotaMensualDeposito(3500, 12)).toBe(291.67)
    expect(cuotaMensualDeposito(0, 3)).toBe(0)
    expect(cuotaMensualDeposito(3500, 0)).toBe(0)
  })
})

describe('Opción 2 · Pago quincenal intercalado (+15d y +45d)', () => {
  it('calcula exactamente +15 y +45 días calendario desde el inicio', () => {
    expect(fechasQuincenalIntercalado('2026-11-01')).toEqual(['2026-11-16', '2026-12-16'])
    // Cruce de fin de año y febrero
    expect(fechasQuincenalIntercalado('2026-12-20')).toEqual(['2027-01-04', '2027-02-03'])
    // Año bisiesto (2028)
    expect(fechasQuincenalIntercalado('2028-02-01')).toEqual(['2028-02-16', '2028-03-17'])
  })
  it('calcula el mes siguiente ajustando días inexistentes', () => {
    expect(mesSiguienteISO('2026-11-01')).toBe('2026-12-01')
    expect(mesSiguienteISO('2026-01-31')).toBe('2026-02-28')
    expect(mesSiguienteISO('2026-12-15')).toBe('2027-01-15')
  })
})

describe('Opción 4 · Pago semanal (estimarPlanSemanal)', () => {
  it('estima semanas y fechas cuando el abono semanal cubre la renta con margen', () => {
    const plan = estimarPlanSemanal(3500, 3500, MONTO_SEMANAL_DEFAULT, '2026-11-01')
    expect(plan.insuficiente).toBe(false)
    expect(plan.equivMensual).toBe(4333.33)
    expect(plan.excedenteMensual).toBe(833.33)
    expect(plan.semanas).toBe(19)
    expect(plan.fechas).toHaveLength(19)
    expect(plan.fechas[0]).toBe('2026-11-08')
    expect(plan.fechas[1]).toBe('2026-11-15')
  })
  it('marca insuficiente y sugiere mínimo redondeado a múltiplos de 50 cuando no alcanza la renta', () => {
    const plan = estimarPlanSemanal(4500, 4500, 1000, '2026-11-01')
    expect(plan.insuficiente).toBe(true)
    expect(plan.minimoSugerido).toBe(1150)
    expect(plan.semanas).toBe(0)
    expect(plan.fechas).toEqual([])
  })
  it('marca insuficiente cuando equivMensual <= renta + 50 (margen demasiado estrecho)', () => {
    // 1000 * 52 / 12 = 4333.33 <= 4300 + 50 = 4350
    const plan = estimarPlanSemanal(4300, 3500, 1000, '2026-11-01')
    expect(plan.insuficiente).toBe(true)
    expect(plan.minimoSugerido).toBe(1100)
    // Aún si hay un excedente diminuto, las fechas nunca pasan de 52
    expect(plan.semanas).toBeLessThanOrEqual(MAX_PAGOS_DEPOSITO)
    expect(plan.fechas.length).toBeLessThanOrEqual(MAX_PAGOS_DEPOSITO)
  })
  it('sin depósito pendiente no marca insuficiente y devuelve 0 semanas', () => {
    const plan = estimarPlanSemanal(4500, 0, 1000, '2026-11-01')
    expect(plan.insuficiente).toBe(false)
    expect(plan.semanas).toBe(0)
    expect(plan.fechas).toEqual([])
  })
})

describe('cuotaSugeridaDeposito', () => {
  it('sugiere la cuota proporcional para diferido_mensual y personalizado, con tope en el saldo', () => {
    const fechas3 = ['2026-11-05', '2026-12-05', '2027-01-05']
    expect(cuotaSugeridaDeposito(3600, 3600, 'diferido_mensual', fechas3)).toBe(1200)
    expect(cuotaSugeridaDeposito(3600, 800, 'diferido_mensual', fechas3)).toBe(800)
    expect(cuotaSugeridaDeposito(3500, 3500, 'personalizado', fechas3)).toBe(1166.67)
  })
  it('sugiere el 50% para quincenal y quincenas, con tope en el saldo', () => {
    expect(cuotaSugeridaDeposito(3500, 3500, 'quincenal')).toBe(1750)
    expect(cuotaSugeridaDeposito(3500, 1000, 'quincenas')).toBe(1000)
  })
  it('devuelve el saldo completo para contado_beneficio, semanal o modalidades sin fechas', () => {
    expect(cuotaSugeridaDeposito(3500, 3500, 'contado_beneficio')).toBe(3500)
    expect(cuotaSugeridaDeposito(3500, 2100, 'semanal')).toBe(2100)
    expect(cuotaSugeridaDeposito(3500, 0, 'diferido_mensual', ['2026-11-05'])).toBe(0)
  })
})

describe('etiquetaModalidadDeposito', () => {
  it('devuelve etiquetas legibles para todas las modalidades nuevas e históricas', () => {
    expect(etiquetaModalidadDeposito('contado_beneficio')).toMatch(/Beneficio Mes 13/)
    expect(etiquetaModalidadDeposito('diferido_mensual', 6)).toBe('Pago diferido (6 meses)')
    expect(etiquetaModalidadDeposito('quincenal')).toMatch(/quincenal/i)
    expect(etiquetaModalidadDeposito('semanal', 19, 1000)).toMatch(/1,000\/sem/)
    expect(etiquetaModalidadDeposito('fechas', 4)).toBe('Fechas libres (4 pagos)')
    expect(etiquetaModalidadDeposito('quincenas')).toBe('Dos quincenas')
    expect(etiquetaModalidadDeposito('personalizado', 3)).toBe('Pagos mensuales (3)')
    expect(etiquetaModalidadDeposito('ninguno')).toBe('Al inicio del contrato')
    expect(etiquetaModalidadDeposito(null)).toBe('Al inicio del contrato')
  })
})

describe('notaDeposito y esNotaAutomatica', () => {
  it('con fechas elegidas lista cada pago', () => {
    const n = notaDeposito('fechas', ['2026-11-30', '2026-12-15'])
    expect(n).toMatch(/^Se pagará el depósito en 2 pagos:/)
    expect(esNotaAutomatica(n)).toBe(true)
    expect(esNotaAutomatica('Se pagará la compensación en 2 pagos: 30 nov 2026.')).toBe(true)
  })
  it('con un solo pago lo dice en singular', () => {
    expect(notaDeposito('fechas', ['2026-11-30'])).toMatch(/en 1 pago:/)
  })
  it('sin fechas aún no escribe nada', () => {
    expect(notaDeposito('fechas', [])).toBe('')
  })
  it('conserva las modalidades históricas', () => {
    expect(notaDeposito('ninguno', [])).toBe('')
    expect(notaDeposito('quincenas', quincenaFechas('2026-11-07'))).toMatch(/2 quincenas/)
    expect(notaDeposito('personalizado', pagosMensuales(5, 3, '2026-11-07'))).toMatch(/3 pagos, el día 5 de cada mes/)
  })
  it('genera notas reconocidas por esNotaAutomatica para las 4 opciones nuevas', () => {
    const n1 = notaDeposito('diferido_mensual', pagosMensuales(5, 6, '2026-11-01'), { deposito: 3600 })
    const n2 = notaDeposito('quincenal', fechasQuincenalIntercalado('2026-11-01'), { deposito: 3500 })
    const n3 = notaDeposito('contado_beneficio', [])
    const n4 = notaDeposito('semanal', estimarPlanSemanal(3500, 3500, 1000, '2026-11-01').fechas, { montoSemanal: 1000 })

    expect(n1).toMatch(/6 pagos mensuales/)
    expect(n1).toMatch(/600\/mes/)
    expect(n2).toMatch(/2 exhibiciones quincenales/)
    expect(n2).toMatch(/1,750/)
    expect(n3).toMatch(/Beneficio Mes 13/)
    expect(n3).toMatch(/No reembolsable/)
    expect(n4).toMatch(/abono semanal de \$1,000/)
    expect(n4).toMatch(/19 semanas/)

    for (const nota of [n1, n2, n3, n4]) {
      expect(esNotaAutomatica(nota)).toBe(true)
    }
  })
  it('no marca como automática una nota escrita a mano por el administrador', () => {
    expect(esNotaAutomatica('')).toBe(true)
    expect(esNotaAutomatica('   ')).toBe(true)
    expect(esNotaAutomatica('Entregó 2 juegos de llaves y tiene mascota pequeña.')).toBe(false)
    expect(esNotaAutomatica('Calle Reforma 123, Col. Centro')).toBe(false)
  })
})
