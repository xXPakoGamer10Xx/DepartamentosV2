import { describe, expect, it } from 'vitest'
import { can, PRESETS } from './permisos'
import type { User } from './types'

const usuario = (rol: User['rol'], permisos: User['permisos'] = null): User =>
  ({ id: 'u1', email: 'a@b.test', nombre_completo: 'Prueba', rol, permisos })

describe('can', () => {
  it('el admin puede todo', () => { expect(can(usuario('admin'), 'reportes')).toBe(true) })
  it('el colaborador solo lo que tiene', () => {
    const c = usuario('cobrador', ['pagos'])
    expect(can(c, 'pagos')).toBe(true)
    expect(can(c, 'pagos.marcar')).toBe(false)
  })
  it('el inquilino y la sesión vacía no tienen permisos de gestión', () => {
    expect(can(usuario('inquilino', ['pagos']), 'pagos')).toBe(false)
    expect(can(null, 'pagos')).toBe(false)
  })
  it('el preset Contador es de solo lectura (no marca pagos)', () => {
    const contador = PRESETS.find(p => p.key === 'contador')!
    expect(contador.permisos).not.toContain('pagos.marcar')
  })
})
