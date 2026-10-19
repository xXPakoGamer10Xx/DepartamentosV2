import {
  BarChart3, Building2, CreditCard, FileCheck, FileText, LayoutGrid, MessageSquare, ScanLine, Settings, Users, Wallet,
  type LucideIcon,
} from 'lucide-react'
import type { Permiso, Rol } from '../../lib/types'

export interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  color: string
  permiso?: Permiso
  adminOnly?: boolean
  /** Solo aparece si el administrador usa el servicio con inquilinos (no en uso interno). */
  conApp?: boolean
  /** Prioridad en la barra inferior del celular (el resto va en "Más"). */
  bottom?: boolean
}

// El administrador y los colaboradores (cobrador) comparten las mismas pantallas; cada ítem se filtra por permiso.
const gestion: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: LayoutGrid, color: '#3B82F6', bottom: true },
  { to: '/inquilinos', label: 'Inquilinos', icon: Users, color: '#F59E0B', permiso: 'inquilinos', bottom: true },
  { to: '/departamentos', label: 'Departamentos', icon: Building2, color: '#10B981', permiso: 'departamentos' },
  { to: '/contratos', label: 'Contratos', icon: FileText, color: '#3B82F6', permiso: 'contratos' },
  { to: '/pagos', label: 'Pagos', icon: CreditCard, color: '#3B82F6', permiso: 'pagos', bottom: true },
  { to: '/comprobantes', label: 'Comprobantes', icon: FileCheck, color: '#10B981', permiso: 'pagos.marcar', conApp: true },
  { to: '/scan', label: 'Escanear QR', icon: ScanLine, color: '#3B82F6', permiso: 'pagos.marcar', conApp: true },
  { to: '/reportes', label: 'Reportes', icon: BarChart3, color: '#8B5CF6', permiso: 'reportes', bottom: true },
  { to: '/cuentas', label: 'Cuentas', icon: Wallet, color: '#10B981', permiso: 'cuentas' },
  { to: '/tickets', label: 'Tickets', icon: MessageSquare, color: '#EF4444', permiso: 'tickets', conApp: true },
  { to: '/configuracion', label: 'Configuración', icon: Settings, color: '#6B7280', adminOnly: true },
]

const inquilino: NavItem[] = [
  { to: '/', label: 'Inicio', icon: LayoutGrid, color: '#3B82F6' },
  { to: '/pagos', label: 'Mis pagos', icon: CreditCard, color: '#10B981' },
  { to: '/tickets', label: 'Tickets', icon: MessageSquare, color: '#EF4444' },
]

export const navFor = (rol: Rol): NavItem[] => (rol === 'inquilino' ? inquilino : gestion)
