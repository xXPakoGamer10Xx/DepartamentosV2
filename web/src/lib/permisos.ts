import type { Permiso, User } from './types'

// Espejo de backend/VertexRent.Api/Auth/Permisos.cs — mantener sincronizado.
export const PERMISO_LABELS: { key: Permiso; label: string; grupo: string }[] = [
  { key: 'pagos', label: 'Ver Pagos y Cobranza', grupo: 'Pagos' },
  { key: 'pagos.marcar', label: 'Marcar pagos / registrar abonos', grupo: 'Pagos' },
  { key: 'inquilinos', label: 'Ver Inquilinos', grupo: 'Inquilinos' },
  { key: 'inquilinos.editar', label: 'Crear, editar y dar de baja inquilinos', grupo: 'Inquilinos' },
  { key: 'departamentos', label: 'Ver Departamentos', grupo: 'Propiedad' },
  { key: 'contratos', label: 'Ver y generar Contratos', grupo: 'Propiedad' },
  { key: 'reportes', label: 'Ver Reportes e ingresos', grupo: 'Reportes' },
  { key: 'cuentas', label: 'Ver Cuentas bancarias', grupo: 'Reportes' },
  { key: 'tickets', label: 'Ver y atender Tickets', grupo: 'Otros' },
  { key: 'notificaciones.pagos', label: 'Recibir notificaciones de pagos', grupo: 'Otros' },
]

export const PRESETS: { key: string; label: string; descripcion: string; permisos: Permiso[] }[] = [
  { key: 'cobrador', label: 'Cobrador', descripcion: 'Registra pagos y recibe avisos de cobranza.', permisos: ['pagos', 'pagos.marcar', 'inquilinos', 'notificaciones.pagos'] },
  { key: 'contador', label: 'Contador', descripcion: 'Ve pagos, reportes y cuentas — solo lectura.', permisos: ['pagos', 'reportes', 'cuentas', 'inquilinos', 'departamentos'] },
  { key: 'asistente', label: 'Asistente', descripcion: 'Administra inquilinos, departamentos y contratos.', permisos: ['inquilinos', 'inquilinos.editar', 'departamentos', 'contratos', 'tickets'] },
]

/** El admin puede todo; el colaborador (cobrador) solo lo que tenga; el inquilino nada de gestión. */
export function can(user: User | null, key: Permiso): boolean {
  if (!user) return false
  if (user.rol === 'admin') return true
  if (user.rol !== 'cobrador') return false
  return (user.permisos ?? []).includes(key)
}

export const isAdmin = (user: User | null) => user?.rol === 'admin'
