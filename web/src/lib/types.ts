// Tipos del API. PostgreSQL devuelve NUMERIC/BIGINT como string ("3500.00"); usa `num()` de format.ts.
export type Numeric = string
export type Rol = 'admin' | 'inquilino' | 'cobrador'

export type Permiso =
  | 'pagos' | 'pagos.marcar'
  | 'inquilinos' | 'inquilinos.editar'
  | 'departamentos' | 'contratos' | 'reportes' | 'cuentas' | 'tickets'
  | 'notificaciones.pagos'

export interface ApiResponse<T> {
  success: boolean
  data: T
  message?: string
  total?: number
}

export interface User {
  id: string
  email: string
  nombre_completo: string
  rol: Rol
  avatar_url?: string | null
  /** null para el admin (puede todo); lista para colaboradores. */
  permisos: Permiso[] | null
  rol_label?: string | null
  activo?: boolean
  ultimo_acceso?: string | null
  created_at?: string
}

export interface LoginResult { token: string; user: User }

export interface Departamento {
  id: number
  admin_id: string
  numero: number
  descripcion: string | null
  estado: 'disponible' | 'ocupado' | 'mantenimiento'
  inventario_base: string[]
  ubicacion: string
  cuenta_bancaria_id: string | null
  plantilla_id?: string | null
  inquilino_actual?: { id: string; nombre: string; renta: Numeric; fecha_termino: string; tel: string | null; estado: string } | null
  historial_inquilinos?: { id: string; nombre: string; renta: Numeric; fecha_inicio: string; fecha_termino: string; estado: string; created_at: string }[] | null
}

export interface DepartamentosStats {
  disponibles: Numeric; ocupados: Numeric; mantenimiento: Numeric; total: Numeric
  ingresos_mensuales: Numeric; contratos_por_vencer: Numeric; contratos_vencidos: Numeric
}

export interface Inquilino {
  id: string
  admin_id: string
  nombre_completo: string
  depto_numero: number
  renta: Numeric
  deposito: Numeric
  renta_letra: string
  fecha_pago: string
  fecha_inicio: string
  fecha_termino: string
  fiador_nombre: string | null
  tel_arrendatario: string | null
  fiador_telefono: string | null
  observaciones: string | null
  inventario: string[]
  estado: 'activo' | 'inactivo' | 'vencido'
  usuario_id: string | null
  deposito_tipo: string | null
  deposito_fechas: string[] | null
  metodo_pago: 'efectivo' | 'transferencia' | 'ambos' | null
  plantilla_id?: string | null
  domicilio?: string | null
  identificacion?: string | null
  email_contacto?: string | null
  fiador_domicilio?: string | null
  usuario?: { id: string; nombre: string; email: string } | null
  auditoria?: { ultimo_accion: string; ultimo_usuario: string | null; ultima_fecha: string } | null
  historial?: { id: string; accion: string; usuario: string | null; datos_anteriores: unknown; datos_nuevos: unknown; fecha: string }[] | null
}

export interface Pago {
  id: string
  inquilino_id: string
  periodo: string
  monto: Numeric
  metodo: string
  qr_token: string | null
  confirmado: boolean
  confirmado_en: string | null
  rechazado: boolean
  comentario_admin: string | null
  comprobante_url: string | null
  comprobante_subido_en: string | null
  fecha_promesa: string | null
  created_at: string
  [extra: string]: unknown
}

export interface Abono {
  id: string; pago_id: string; monto: Numeric; fecha: string; metodo: string
  nota: string | null; comprobante_url: string | null; registrado_por_nombre?: string | null; created_at: string
}

export interface CuentaBancaria {
  id: string; alias: string | null; banco_nombre: string | null; banco_clabe: string | null
  banco_titular: string | null; es_predeterminada: boolean; activo: boolean
  departamentos: number[] | null; renta_mensual: Numeric
}

export interface CuotaExtra {
  id: string; inquilino_id: string; concepto: string; monto: Numeric; periodo: string
  estado: 'pendiente' | 'pagado'; creado_por_nombre?: string | null; created_at: string; pagado_en: string | null
}

export interface Ticket {
  id: string; inquilino_id: string; titulo: string; descripcion: string
  estado: 'abierto' | 'en_revision' | 'resuelto'; nota_admin: string | null
  inquilino_nombre?: string; depto_numero?: number; atendido_por_nombre?: string | null
  created_at: string; updated_at: string
}

export interface Notificacion {
  id: string; usuario_id: string; titulo: string; mensaje: string; leido: boolean
  tipo: 'renta' | 'cuota' | 'ticket' | 'pago' | 'promesa'; created_at: string
}

export interface CodigoInvitacion {
  id: string; codigo: string; rol: 'inquilino' | 'cobrador'; expira_en: string | null
  usado: boolean; usado_en: string | null; usado_por_nombre?: string | null
  permisos: Permiso[]; rol_label: string | null; created_at: string
}

export interface BackupInfo { id: number; archivo: string; tamano_bytes: string; tipo: 'automatico' | 'manual'; estado: 'completado' | 'fallido'; created_at: string }

export interface ResumenDeuda {
  total_general: number
  total_por_cobrar: number
  por_departamento: { inquilino_id: string; depto_numero: number; nombre_completo: string; deuda_total: Numeric; deuda_vencida: Numeric }[]
}

export type Config = Record<string, string>

// ── Contratos ──────────────────────────────────────────────────────────────
export interface VariableContrato { key: string; etiqueta: string; grupo: string; ejemplo: string }
export interface CampoPlantilla { key: string; etiqueta: string; tipo: 'texto' | 'numero' | 'fecha'; requerido: boolean }

export interface Plantilla {
  id: string
  nombre: string
  descripcion?: string | null
  estado: 'borrador' | 'lista'
  sistema: boolean
  predeterminada: boolean
  campos: CampoPlantilla[]
  propiedades: number
  inquilinos: number
  updated_at?: string
}

/** Rango [ini, fin) del párrafo `p` que se sustituye por {{key}}. */
export interface Marca { p: string; ini: number; fin: number; key: string; texto?: string; campo?: CampoPlantilla | null; borrar?: string[] | null }
export interface Sugerencia extends Marca { texto: string; confianza: number }

export interface PlantillaNueva {
  id: string; nombre: string; html: string; sugerencias: Sugerencia[]; variables_presentes: string[]; ia_error: string | null; desde_pdf: boolean; aviso_conversion: string | null
}
export interface PlantillaDetalle {
  id: string; nombre: string; sistema: boolean; estado: 'borrador' | 'lista'; predeterminada?: boolean
  campos: CampoPlantilla[]; marcas: Marca[]; html: string; variables_presentes?: string[]
}

export interface ContratoVersion { id: string; version: number; plantilla_nombre: string | null; fecha_generacion: string; aproximado: boolean }
export interface FaltanteContrato { key: string; etiqueta: string; destino: 'inquilino' | 'propiedad' | 'config' | 'cuenta' }
export interface PrepararContrato {
  inquilino: { id: string; nombre_completo: string; depto_numero: string }
  plantilla_id: string
  origen: 'sistema' | 'inquilino' | 'propiedad' | 'predeterminada'
  plantillas: { id: string; nombre: string; campos: CampoPlantilla[]; sistema: boolean }[]
  faltantes: FaltanteContrato[]
  versiones: ContratoVersion[]
  motor: 'libreoffice' | 'aproximado'
}
export interface ResumenContrato {
  id: string; nombre_completo: string; depto_numero: number; renta: Numeric; deposito: Numeric
  fecha_inicio: string; fecha_termino: string; estado: string
  contrato_id: string | null; version: number | null; plantilla_nombre: string | null; fecha_generacion: string | null
  /** La plantilla que le toca ahora dice algo distinto de la usada al generar su contrato. */
  desactualizado?: boolean
}
