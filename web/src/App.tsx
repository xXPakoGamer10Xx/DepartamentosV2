import { useUsaApp } from './lib/usaApp'
import { lazy, Suspense, type ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/layout/AppShell'
import { FullPageSpinner } from './components/ui/Spinner'
import { useAuth } from './lib/auth'
import type { Permiso } from './lib/types'

// Carga diferida por pantalla: el primer pintado solo descarga lo necesario.
const Login = lazy(() => import('./pages/Login'))
const Register = lazy(() => import('./pages/Register'))
const AdminHome = lazy(() => import('./pages/AdminHome'))
const InquilinoHome = lazy(() => import('./pages/inquilino/Home'))
const InquilinoPagos = lazy(() => import('./pages/inquilino/Pagos'))
const InquilinoTickets = lazy(() => import('./pages/inquilino/Tickets'))
const ConfirmarPago = lazy(() => import('./pages/ConfirmarPago'))
const Inquilinos = lazy(() => import('./pages/admin/Inquilinos'))
const InquilinoDetalle = lazy(() => import('./pages/admin/InquilinoDetalle'))
const InquilinoForm = lazy(() => import('./pages/admin/InquilinoForm'))
const Departamentos = lazy(() => import('./pages/admin/Departamentos'))
const DepartamentoDetalle = lazy(() => import('./pages/admin/DepartamentoDetalle'))
const Cuentas = lazy(() => import('./pages/admin/Cuentas'))
const Pagos = lazy(() => import('./pages/admin/Pagos'))
const PagoDetalle = lazy(() => import('./pages/admin/PagoDetalle'))
const Comprobantes = lazy(() => import('./pages/admin/Comprobantes'))
const Reportes = lazy(() => import('./pages/admin/Reportes'))
const Scan = lazy(() => import('./pages/admin/Scan'))
const AdminTickets = lazy(() => import('./pages/admin/AdminTickets'))
const Configuracion = lazy(() => import('./pages/admin/Configuracion'))
const Contratos = lazy(() => import('./pages/admin/contratos/Contratos'))
const PlantillaAsistente = lazy(() => import('./pages/admin/contratos/PlantillaAsistente'))
const NotFound = lazy(() => import('./pages/NotFound'))

function RequireAuth({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) return <FullPageSpinner />
  return user ? children : <Navigate to="/login" replace />
}

function GuestOnly({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) return <FullPageSpinner />
  return user ? <Navigate to="/" replace /> : children
}

/** Colaboradores: la ruta solo existe si tienen el permiso (el servidor lo vuelve a comprobar). */
function Guard({ permiso, adminOnly, conApp, children }: { permiso?: Permiso; adminOnly?: boolean; conApp?: boolean; children: ReactNode }) {
  const { user, can } = useAuth()
  const usaApp = useUsaApp()
  if (conApp && !usaApp) return <Navigate to="/" replace />
  if (adminOnly && user?.rol !== 'admin') return <Navigate to="/" replace />
  if (permiso && !can(permiso)) return <Navigate to="/" replace />
  return children
}

function Home() {
  const { user } = useAuth()
  return user?.rol === 'inquilino' ? <InquilinoHome /> : <AdminHome />
}

/** Misma ruta para varios roles (p. ej. /pagos): el inquilino ve su versión, el resto la de gestión. */
function ByRole({ inquilino, gestion }: { inquilino: ReactNode; gestion: ReactNode }) {
  const { user } = useAuth()
  return user?.rol === 'inquilino' ? inquilino : gestion
}

export default function App() {
  return (
    <Suspense fallback={<FullPageSpinner />}>
      <Routes>
        <Route path="/login" element={<GuestOnly><Login /></GuestOnly>} />
        <Route path="/register" element={<GuestOnly><Register /></GuestOnly>} />
        {/* Pública: la abre quien escanea el QR de pago en efectivo (con o sin sesión). */}
        <Route path="/confirmar/:token" element={<ConfirmarPago />} />

        <Route element={<RequireAuth><AppShell /></RequireAuth>}>
          <Route index element={<Home />} />
          <Route path="inquilinos" element={<Guard permiso="inquilinos"><Inquilinos /></Guard>} />
          <Route path="inquilinos/nuevo" element={<Guard permiso="inquilinos.editar"><InquilinoForm /></Guard>} />
          <Route path="inquilinos/:id" element={<Guard permiso="inquilinos"><InquilinoDetalle /></Guard>} />
          <Route path="inquilinos/:id/editar" element={<Guard permiso="inquilinos.editar"><InquilinoForm /></Guard>} />
          <Route path="departamentos" element={<Guard permiso="departamentos"><Departamentos /></Guard>} />
          <Route path="departamentos/:numero" element={<Guard permiso="departamentos"><DepartamentoDetalle /></Guard>} />
          <Route path="cuentas" element={<Guard permiso="cuentas"><Cuentas /></Guard>} />
          <Route path="pagos" element={<ByRole inquilino={<InquilinoPagos />} gestion={<Guard permiso="pagos"><Pagos /></Guard>} />} />
          <Route path="pagos/:id" element={<Guard permiso="pagos"><PagoDetalle /></Guard>} />
          <Route path="tickets/*" element={<ByRole inquilino={<InquilinoTickets />} gestion={<Guard permiso="tickets" conApp><AdminTickets /></Guard>} />} />
          <Route path="comprobantes" element={<Guard permiso="pagos.marcar" conApp><Comprobantes /></Guard>} />
          <Route path="reportes" element={<Guard permiso="reportes"><Reportes /></Guard>} />
          <Route path="contratos" element={<Guard permiso="contratos"><Contratos /></Guard>} />
          <Route path="contratos/plantillas/nueva" element={<Guard permiso="contratos" adminOnly><PlantillaAsistente /></Guard>} />
          <Route path="contratos/plantillas/:id" element={<Guard permiso="contratos" adminOnly><PlantillaAsistente /></Guard>} />
          <Route path="scan" element={<Guard permiso="pagos.marcar" conApp><Scan /></Guard>} />
          <Route path="configuracion" element={<Guard adminOnly><Configuracion /></Guard>} />
        </Route>

        <Route path="*" element={<NotFound />} />
      </Routes>
    </Suspense>
  )
}
