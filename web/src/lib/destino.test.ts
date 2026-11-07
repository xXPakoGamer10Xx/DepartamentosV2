import { describe, expect, it } from 'vitest'
import { destinoTrasLogin } from './destino'

describe('destinoTrasLogin', () => {
  it('vuelve a la ruta interna pedida (p. ej. el enlace de un QR)', () => {
    expect(destinoTrasLogin({ from: '/confirmar/abc-123' })).toBe('/confirmar/abc-123')
    expect(destinoTrasLogin({ from: '/pagos?tab=atrasados' })).toBe('/pagos?tab=atrasados')
  })
  it('sin destino va al inicio', () => {
    expect(destinoTrasLogin(null)).toBe('/')
    expect(destinoTrasLogin(undefined)).toBe('/')
    expect(destinoTrasLogin({})).toBe('/')
  })
  it('no admite redirecciones externas', () => {
    expect(destinoTrasLogin({ from: 'https://malo.test' })).toBe('/')
    expect(destinoTrasLogin({ from: '//malo.test/x' })).toBe('/')
    expect(destinoTrasLogin({ from: 42 })).toBe('/')
  })
})
