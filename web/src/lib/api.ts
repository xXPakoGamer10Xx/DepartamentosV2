import { getToken, notifyUnauthorized } from './session'
import type {
  Abono, ApiResponse, BackupInfo, CampoPlantilla, CodigoInvitacion, Config, ContratoVersion, CuentaBancaria, CuotaExtra, Departamento,
  DepartamentosStats, Inquilino, LoginResult, Marca, Notificacion, Numeric, Pago, Plantilla, PlantillaDetalle, PlantillaNueva,
  PrepararContrato, ResumenContrato, ResumenDeuda, Ticket, User, VariableContrato,
} from './types'

export const API_URL = (import.meta.env.VITE_API_URL as string | undefined) ?? '/api'

export class ApiError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/** Respuesta estándar `{success, data}` más los campos extra que algunos endpoints añaden al nivel superior. */
type Res<T, X = unknown> = ApiResponse<T> & X
type Params = Record<string, string | number | undefined | null>

function url(path: string, params?: Params) {
  let u = `${API_URL}${path}`
  if (params) {
    const q = new URLSearchParams()
    for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') q.set(k, String(v))
    const s = q.toString()
    if (s) u += `?${s}`
  }
  return u
}

function headers(json = true): HeadersInit {
  const h: Record<string, string> = {}
  if (json) h['Content-Type'] = 'application/json'
  const t = getToken()
  if (t) h.Authorization = `Bearer ${t}`
  return h
}

async function request<T, X = unknown>(method: string, path: string, body?: unknown, params?: Params): Promise<Res<T, X>> {
  const res = await fetch(url(path, params), {
    method,
    headers: headers(),
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  let data: unknown
  try { data = await res.json() } catch { data = null }
  if (!res.ok) {
    // 401 con sesión → token vencido/ inválido. En /auth/login es simplemente "credenciales incorrectas".
    if (res.status === 401 && path !== '/auth/login') notifyUnauthorized()
    const msg = (data as { message?: string } | null)?.message
    throw new ApiError(msg || `Error ${res.status}`, res.status)
  }
  return data as Res<T, X>
}

/** Descarga binaria autenticada (PDF/DOCX generados por el backend). */
async function blob(method: string, path: string, body?: unknown): Promise<Blob> {
  const res = await fetch(url(path), { method, headers: headers(), body: body === undefined ? undefined : JSON.stringify(body) })
  if (!res.ok) {
    let msg = `Error ${res.status}`
    try { msg = (await res.json()).message || msg } catch { /* no era JSON */ }
    if (res.status === 401) notifyUnauthorized()
    throw new ApiError(msg, res.status)
  }
  return res.blob()
}

export const api = {
  // ── Auth ────────────────────────────────────────────────────────────────
  login: (email: string, password: string) => request<LoginResult>('POST', '/auth/login', { email, password }),
  register: (d: { email: string; password: string; nombre_completo: string; rol: string; invite_code?: string }) =>
    request<LoginResult>('POST', '/auth/register', d),
  me: () => request<User>('GET', '/auth/me'),
  changePassword: (passwordActual: string, passwordNueva: string) =>
    request<void>('POST', '/auth/change-password', { passwordActual, passwordNueva }),

  // ── Inquilinos ──────────────────────────────────────────────────────────
  getMiDepto: () => request<Inquilino | null>('GET', '/inquilinos/mi-depto'),
  getInquilinos: (p?: { search?: string; estado?: string; depto?: string }) => request<Inquilino[]>('GET', '/inquilinos', undefined, p),
  getInquilino: (id: string) => request<Inquilino>('GET', `/inquilinos/${id}`),
  createInquilino: (d: object) => request<Inquilino>('POST', '/inquilinos', d),
  updateInquilino: (id: string, d: object) => request<Inquilino>('PUT', `/inquilinos/${id}`, d),
  deleteInquilino: (id: string) => request<void>('DELETE', `/inquilinos/${id}`),
  vincularUsuario: (id: string, usuarioId: string | null) => request<Inquilino>('PUT', `/inquilinos/${id}/vincular-usuario`, { usuario_id: usuarioId }),
  extraerIne: (imagen_base64: string) =>
    request<{ nombre_completo: string; domicilio: string; colonia: string; municipio: string; estado: string; cp: string }>('POST', '/inquilinos/extraer-ine', { imagen_base64 }),

  // PDF del contrato por enlace con token (compatibilidad)
  generarTokenPdf: (id: string) => request<{ token: string }>('POST', `/inquilinos/${id}/pdf-token`),
  /** URL abrible en el navegador (token de un solo uso, 2 min). */
  contratoPdfUrl: (id: string, token: string) => `${API_URL}/inquilinos/${id}/pdf?token=${encodeURIComponent(token)}`,

  // ── Departamentos ───────────────────────────────────────────────────────
  getDepartamentos: () => request<Departamento[]>('GET', '/departamentos'),
  getDepartamentosStats: () => request<DepartamentosStats>('GET', '/departamentos/stats'),
  getDepartamento: (n: number) => request<Departamento>('GET', `/departamentos/${n}`),
  createDepartamento: (d: object) => request<Departamento>('POST', '/departamentos', d),
  updateDepartamento: (n: number, d: object) => request<Departamento>('PUT', `/departamentos/${n}`, d),
  deleteDepartamento: (n: number) => request<void>('DELETE', `/departamentos/${n}`),

  // ── Cuentas bancarias ───────────────────────────────────────────────────
  getCuentas: () => request<CuentaBancaria[]>('GET', '/cuentas-bancarias'),
  createCuenta: (d: { alias?: string; banco_nombre?: string; banco_clabe: string; banco_titular?: string }) => request<CuentaBancaria>('POST', '/cuentas-bancarias', d),
  updateCuenta: (id: string, d: object) => request<CuentaBancaria>('PUT', `/cuentas-bancarias/${id}`, d),
  setCuentaDefault: (id: string) => request<CuentaBancaria>('PATCH', `/cuentas-bancarias/${id}/predeterminada`),
  deleteCuenta: (id: string) => request<void, { inquilinos_a_efectivo: number }>('DELETE', `/cuentas-bancarias/${id}`),
  asignarCuentaDepto: (numero: number, cuenta_bancaria_id: string | null) =>
    request<{ numero: number; cuenta_bancaria_id: string | null }>('PATCH', `/cuentas-bancarias/departamento/${numero}`, { cuenta_bancaria_id }),

  // ── Usuarios / invitaciones ─────────────────────────────────────────────
  getUsuarios: () => request<User[]>('GET', '/usuarios'),
  createUsuario: (d: object) => request<User>('POST', '/usuarios', d),
  toggleUsuario: (id: string) => request<{ id: string; email: string; activo: boolean }>('PUT', `/usuarios/${id}/toggle`),
  deleteUsuario: (id: string) => request<void>('DELETE', `/usuarios/${id}`),
  updatePerfil: (d: object) => request<User>('PATCH', '/usuarios/perfil', d),
  cambiarRol: (id: string, rol: string) => request<User>('PATCH', `/usuarios/${id}/rol`, { rol }),
  actualizarPermisos: (id: string, permisos: string[], rol_label?: string) => request<User>('PATCH', `/usuarios/${id}/permisos`, { permisos, rol_label }),
  getCodigos: () => request<CodigoInvitacion[]>('GET', '/invite-codes'),
  crearCodigo: (rol: string, expira_dias: number | null, permisos?: string[], preset?: string, rol_label?: string) =>
    request<CodigoInvitacion>('POST', '/invite-codes', { rol, expira_dias, permisos, preset, rol_label }),
  revocarCodigo: (id: string) => request<void>('DELETE', `/invite-codes/${id}`),

  // ── Config / contrato global ────────────────────────────────────────────
  getConfig: (deptoNumero?: number) => request<Config>('GET', '/config', undefined, { depto_numero: deptoNumero }),
  updateConfig: (d: object) => request<void>('PUT', '/config', d),

  // ── Contratos (plantillas Word + historial) ──────────────────────────────
  getVariablesContrato: () => request<{ variables: VariableContrato[]; motor: 'libreoffice' | 'aproximado'; ia: boolean }>('GET', '/contratos/variables'),
  getPlantillas: () => request<Plantilla[]>('GET', '/contratos/plantillas'),
  /** Sube el .docx (data URL) y devuelve el documento con las sugerencias de la IA. */
  subirPlantilla: (nombre: string, archivoBase64: string) => request<PlantillaNueva>('POST', '/contratos/plantillas', { nombre, archivoBase64 }),
  getPlantilla: (id: string) => request<PlantillaDetalle>('GET', `/contratos/plantillas/${id}`),
  guardarPlantilla: (id: string, d: { nombre?: string; estado?: 'borrador' | 'lista'; marcas?: Pick<Marca, 'p' | 'ini' | 'fin' | 'key' | 'borrar'>[]; campos?: CampoPlantilla[]; predeterminada?: boolean }) =>
    request<{ id: string; nombre: string; estado: string }>('PUT', `/contratos/plantillas/${id}`, d),
  eliminarPlantilla: (id: string) => request<void>('DELETE', `/contratos/plantillas/${id}`),
  hacerPredeterminada: (id: string) => request<void>('POST', `/contratos/plantillas/${id}/predeterminada`),
  asignarPlantilla: (id: string, departamento_ids: number[]) => request<void>('PUT', `/contratos/plantillas/${id}/asignar`, { departamento_ids }),
  previewPlantilla: (id: string, d: { inquilino_id?: string } = {}) => blob('POST', `/contratos/plantillas/${id}/preview`, d),
  descargarPlantilla: (id: string) => blob('GET', `/contratos/plantillas/${id}/docx`),
  prepararContrato: (inquilinoId: string, plantillaId?: string) => request<PrepararContrato>('GET', `/contratos/inquilino/${inquilinoId}/preparar`, undefined, { plantilla_id: plantillaId }),
  generarContrato: (inquilinoId: string, d: { plantilla_id?: string; valores_extra?: Record<string, string>; completar?: Record<string, string> }) =>
    request<{ id: string; version: number; aproximado: boolean; plantilla_nombre: string }>('POST', `/contratos/inquilino/${inquilinoId}/generar`, d),
  getResumenContratos: () => request<ResumenContrato[]>('GET', '/contratos/resumen'),
  getHistorialContratos: (inquilino_id?: string) => request<(ContratoVersion & { inquilino_id: string; nombre_completo: string; depto_numero: number })[]>('GET', '/contratos', undefined, { inquilino_id }),
  eliminarContrato: (id: string) => request<void>('DELETE', `/contratos/${id}`),
  descargarContrato: (id: string, formato: 'pdf' | 'docx') => blob('GET', `/contratos/${id}/archivo?formato=${formato}`),

  // ── Tickets ─────────────────────────────────────────────────────────────
  getTickets: (p?: { estado?: string; mes?: string }) => request<Ticket[]>('GET', '/tickets', undefined, p),
  createTicket: (d: { titulo: string; descripcion: string; inquilino_id?: string }) => request<Ticket>('POST', '/tickets', d),
  updateTicket: (id: string, d: { estado?: string; nota_admin?: string }) => request<Ticket>('PATCH', `/tickets/${id}`, d),
  deleteTicket: (id: string) => request<void>('DELETE', `/tickets/${id}`),

  // ── Cuotas extra ────────────────────────────────────────────────────────
  getCuotas: (p?: { inquilino_id?: string; estado?: string }) => request<CuotaExtra[]>('GET', '/cuotas', undefined, p),
  createCuota: (d: { inquilino_id: string; concepto: string; monto: number }) => request<CuotaExtra>('POST', '/cuotas', d),
  deleteCuota: (id: string) => request<void>('DELETE', `/cuotas/${id}`),
  pagarCuota: (id: string) => request<unknown>('PUT', `/cuotas/${id}/pagar`),

  // ── Pagos ───────────────────────────────────────────────────────────────
  generarQr: (inquilinoId: string) => request<Pago & { renta_base: number; total_extra: number; cuotas: { id: string; concepto: string; monto: Numeric }[]; inquilino: { nombre_completo: string; depto_numero: number } }>('POST', `/pagos/generar-qr/${inquilinoId}`),
  getEstadoPago: (inquilinoId: string) =>
    request<Pago | null, { cuotas: CuotaExtra[]; total_extra: number; total_abonado: number; saldo_pendiente: number; periodo: string }>('GET', `/pagos/estado/${inquilinoId}`),
  getEstadosActuales: () =>
    request<{ inquilino_id: string; pago_id: string | null; monto: Numeric | null; confirmado: boolean; confirmado_en: string | null; rechazado: boolean; comprobante_url: string | null; total_abonado: Numeric }[]>('GET', '/pagos/estados-actuales'),
  getPagoByToken: (token: string) => request<Pago & { nombre_completo: string; depto_numero: number; renta: Numeric }>('GET', `/pagos/info/${token}`),
  confirmarPago: (token: string) => request<Pago, { yaConfirmado?: boolean }>('POST', `/pagos/confirmar/${token}`, {}),
  subirComprobante: (inquilinoId: string, comprobante_url: string) => request<Pago>('POST', `/pagos/comprobante/${inquilinoId}`, { comprobante_url }),
  confirmarPagoAdmin: (pagoId: string) => request<Pago, { yaConfirmado?: boolean }>('POST', `/pagos/confirmar-admin/${pagoId}`),
  marcarPagado: (inquilinoId: string, soloRenta?: boolean) => request<Pago, { yaConfirmado?: boolean }>('POST', `/pagos/marcar-pagado/${inquilinoId}`, soloRenta ? { soloRenta: true } : {}),
  /** Deshace un pago registrado por error (solo dentro de las 24 h siguientes). */
  cancelarPago: (pagoId: string) => request<void>('DELETE', `/pagos/${pagoId}/cancelar`),
  rechazarPago: (pagoId: string, comentario?: string) => request<Pago>('POST', `/pagos/rechazar-admin/${pagoId}`, { comentario: comentario || '' }),
  getComprobantesPendientes: () => request<(Pick<Pago, 'id' | 'monto' | 'periodo' | 'comprobante_url' | 'comprobante_subido_en'> & { nombre_completo: string; depto_numero: number; inquilino_id: string })[]>('GET', '/pagos/comprobantes-pendientes'),
  getHistorialPagos: (inquilinoId: string) => request<(Pago & { a_tiempo: boolean | null; fecha_pago: string; escaneado_por_nombre: string | null })[]>('GET', `/pagos/historial/${inquilinoId}`),

  registrarAbono: (d: { inquilino_id: string; periodo?: string; monto: number; fecha?: string; metodo?: string; nota?: string; comprobante_url?: string }) =>
    request<{ abono: Abono; pago: Pago; totalAbonado: number; saldoPendiente: number }>('POST', '/pagos/abono', d),
  getAbonosPago: (pagoId: string) => request<Abono[]>('GET', `/pagos/abonos/${pagoId}`),
  editarAbono: (id: string, d: { monto?: number; fecha?: string; metodo?: string; nota?: string }) =>
    request<{ abono: Abono; pago: Pago; totalAbonado: number; saldoPendiente: number }>('PUT', `/pagos/abono/${id}`, d),
  eliminarAbono: (id: string) => request<{ pago: Pago; totalAbonado: number; saldoPendiente: number }>('DELETE', `/pagos/abono/${id}`),

  getSaldoInquilino: (inquilinoId: string) =>
    request<{ deuda_total: number; detalle: { periodo: string; monto_esperado: Numeric; abonado: Numeric; saldo: Numeric }[] }>('GET', `/pagos/saldo/${inquilinoId}`),
  getSaldos: () => request<{ inquilino_id: string; depto_numero: number; nombre_completo: string; deuda_total: Numeric; deuda_vencida: Numeric; periodo_vencido: string | null; deuda_cuotas_extra: Numeric; deuda_deposito: Numeric }[]>('GET', '/pagos/saldos'),
  getResumenDeuda: () => request<ResumenDeuda>('GET', '/pagos/saldos/resumen'),

  setPromesa: (pagoId: string, fecha: string | null) => request<Pago>('PUT', `/pagos/${pagoId}/promesa`, { fecha_promesa: fecha }),
  /** Igual que setPromesa pero cuando aún no existe la fila del pago del periodo (el backend la crea). */
  setPromesaInquilino: (inquilinoId: string, fecha: string | null, periodo?: string) =>
    request<Pago | null>('PUT', `/pagos/promesa/inquilino/${inquilinoId}`, { fecha_promesa: fecha, periodo }),
  getPromesas: (inquilinoId: string) =>
    request<{ id: string; fecha_promesa: string; created_at: string; periodo: string; monto: Numeric; confirmado: boolean; estado_cumplimiento: 'cumplida' | 'incumplida' | 'pendiente' }[]>('GET', `/pagos/promesas/${inquilinoId}`),
  eliminarPromesa: (id: string) => request<void>('DELETE', `/pagos/promesas/${id}`),

  // ── Depósitos ───────────────────────────────────────────────────────────
  getSaldoDeposito: (inquilinoId: string) => request<{ deposito_total: number; deposito_pagado: number; deposito_saldo: number }>('GET', `/depositos/${inquilinoId}/saldo`),
  getAbonosDeposito: (inquilinoId: string) => request<Abono[]>('GET', `/depositos/${inquilinoId}/abonos`),
  registrarAbonoDeposito: (d: { inquilino_id: string; monto: number; fecha?: string; metodo?: string; nota?: string; comprobante_url?: string }) =>
    request<{ abono: Abono; deposito_pagado: number; deposito_saldo: number }>('POST', '/depositos/abono', d),
  editarAbonoDeposito: (id: string, d: { monto?: number; fecha?: string; metodo?: string; nota?: string }) => request<{ abono: Abono }>('PUT', `/depositos/abono/${id}`, d),
  eliminarAbonoDeposito: (id: string) => request<void>('DELETE', `/depositos/abono/${id}`),

  // ── Reportes ────────────────────────────────────────────────────────────
  getReporteAnual: (year: number) => request<{ year: number; renta_total: number; extra_total: number; deposito_total: number; total_general: number }>('GET', '/reportes/anual', undefined, { year }),
  getReporteMensual: (year: number) =>
    request<{ year: number; meses: { mes: number; renta: number; extra: number; deposito: number; total: number }[]; por_departamento: { depto_numero: number; total: number; meses: number[] }[]; total_anual: number; anios_disponibles: number[] }>('GET', '/reportes/mensual', undefined, { year }),

  // ── Notificaciones / push / respaldos ───────────────────────────────────
  getNotificaciones: () => request<Notificacion[]>('GET', '/notificaciones'),
  marcarTodasLeidas: () => request<void>('PUT', '/notificaciones/marcar-leidas'),
  marcarLeida: (id: string) => request<Notificacion>('PUT', `/notificaciones/${id}/leida`),
  subscribeWebPush: (s: { endpoint: string; keys: { p256dh: string; auth: string } }) => request<void>('POST', '/push/web-subscribe', s),
  getBackups: () => request<BackupInfo[]>('GET', '/backups'),
  crearBackup: () => request<{ archivo: string; tamano_bytes: number }>('POST', '/backups/manual', {}),
  descargarBackup: (archivo: string) => blob('GET', `/backups/${encodeURIComponent(archivo)}/descargar`),
}
