
CREATE TABLE IF NOT EXISTS pagos (
  id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  inquilino_id  UUID NOT NULL REFERENCES inquilinos(id) ON DELETE CASCADE,
  periodo       VARCHAR(7) NOT NULL,
  monto         NUMERIC(10,2) NOT NULL,
  metodo        VARCHAR(20) NOT NULL DEFAULT 'efectivo',
  qr_token      UUID UNIQUE,
  confirmado    BOOLEAN NOT NULL DEFAULT FALSE,
  confirmado_en TIMESTAMPTZ,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(inquilino_id, periodo)
);
CREATE INDEX IF NOT EXISTS idx_pagos_inquilino ON pagos(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_pagos_periodo ON pagos(periodo);
CREATE INDEX IF NOT EXISTS idx_pagos_qr_token ON pagos(qr_token);

CREATE TABLE IF NOT EXISTS tickets (
  id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  inquilino_id  UUID NOT NULL REFERENCES inquilinos(id) ON DELETE CASCADE,
  titulo        VARCHAR(200) NOT NULL,
  descripcion   TEXT NOT NULL,
  estado        VARCHAR(20) NOT NULL DEFAULT 'abierto'
                  CHECK (estado IN ('abierto', 'en_revision', 'resuelto')),
  nota_admin    TEXT,
  creado_por    UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  atendido_por  UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_tickets_inquilino ON tickets(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_tickets_estado ON tickets(estado);

CREATE TABLE IF NOT EXISTS cuotas_extra (
  id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  inquilino_id  UUID NOT NULL REFERENCES inquilinos(id) ON DELETE CASCADE,
  concepto      VARCHAR(200) NOT NULL,
  monto         NUMERIC(10,2) NOT NULL CHECK (monto > 0),
  periodo       VARCHAR(7) NOT NULL,
  estado        VARCHAR(20) NOT NULL DEFAULT 'pendiente'
                  CHECK (estado IN ('pendiente', 'pagado')),
  creado_por    UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  pagado_en     TIMESTAMPTZ,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_cuotas_inquilino ON cuotas_extra(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_cuotas_estado ON cuotas_extra(estado);

CREATE TABLE IF NOT EXISTS notificaciones (
  id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  usuario_id    UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  titulo        VARCHAR(200) NOT NULL,
  mensaje       TEXT NOT NULL,
  leido         BOOLEAN NOT NULL DEFAULT FALSE,
  tipo          VARCHAR(50) NOT NULL,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_notificaciones_usuario ON notificaciones(usuario_id);
CREATE INDEX IF NOT EXISTS idx_notificaciones_leido ON notificaciones(leido);

CREATE TABLE IF NOT EXISTS push_tokens (
  id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  usuario_id  UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  token       TEXT NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(usuario_id, token)
);
CREATE INDEX IF NOT EXISTS idx_push_tokens_usuario ON push_tokens(usuario_id);

-- Comprobante de pago en pagos
ALTER TABLE pagos ADD COLUMN IF NOT EXISTS comprobante_url TEXT;
ALTER TABLE pagos ADD COLUMN IF NOT EXISTS comprobante_subido_en TIMESTAMPTZ;

-- El comprobante se guarda como data URL (imagen en base64): VARCHAR(500) lo rechazaba con error 500.
-- varchar -> text es un cambio solo de metadatos (no reescribe la tabla) y es idempotente.
ALTER TABLE pagos ALTER COLUMN comprobante_url TYPE TEXT;
ALTER TABLE usuarios ALTER COLUMN avatar_url TYPE TEXT;

-- Asegurar columnas y constraints actualizados en inquilinos
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS deposito NUMERIC(10,2) NOT NULL DEFAULT 0;
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS metodo_pago VARCHAR(20) DEFAULT 'efectivo';
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS deposito_tipo VARCHAR(50) DEFAULT 'ninguno';
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS deposito_fechas JSONB DEFAULT '[]';
ALTER TABLE inquilinos DROP CONSTRAINT IF EXISTS inquilinos_metodo_pago_check;
ALTER TABLE inquilinos ADD CONSTRAINT inquilinos_metodo_pago_check
  CHECK (metodo_pago IS NULL OR metodo_pago IN ('efectivo', 'transferencia', 'ambos'));

-- Columnas de auditoría de escaneo en pagos
ALTER TABLE pagos ADD COLUMN IF NOT EXISTS escaneado_por UUID REFERENCES usuarios(id) ON DELETE SET NULL;
ALTER TABLE pagos ADD COLUMN IF NOT EXISTS escaneado_en TIMESTAMPTZ;

-- Rol cobrador en usuarios
ALTER TABLE usuarios DROP CONSTRAINT IF EXISTS usuarios_rol_check;
ALTER TABLE usuarios ADD CONSTRAINT usuarios_rol_check
  CHECK (rol IN ('admin', 'inquilino', 'cobrador'));

-- Permisos granulares para colaboradores (rol 'cobrador')
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS permisos JSONB NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE codigos_invitacion ADD COLUMN IF NOT EXISTS permisos JSONB NOT NULL DEFAULT '[]'::jsonb;

-- Etiqueta de rol/preset elegido (solo informativa, para mostrar en la lista de accesos).
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS rol_label VARCHAR(50);
ALTER TABLE codigos_invitacion ADD COLUMN IF NOT EXISTS rol_label VARCHAR(50);

-- admin_id: a qué administrador pertenece un colaborador/inquilino (multi-tenant).
ALTER TABLE usuarios ADD COLUMN IF NOT EXISTS admin_id UUID REFERENCES usuarios(id) ON DELETE CASCADE;
-- Backfill: en un despliegue de un solo admin, todos los no-admin cuelgan de él.
UPDATE usuarios SET admin_id = (SELECT id FROM usuarios WHERE rol = 'admin' ORDER BY created_at ASC LIMIT 1)
WHERE rol <> 'admin' AND admin_id IS NULL
  AND EXISTS (SELECT 1 FROM usuarios WHERE rol = 'admin');

-- Cuentas bancarias múltiples por admin (transferencias)
CREATE TABLE IF NOT EXISTS cuentas_bancarias (
  id                UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  admin_id          UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  alias             VARCHAR(60),
  banco_nombre      VARCHAR(60),
  banco_clabe       VARCHAR(18),
  banco_titular     VARCHAR(120),
  es_predeterminada BOOLEAN NOT NULL DEFAULT FALSE,
  activo            BOOLEAN NOT NULL DEFAULT TRUE,
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_cuentas_bancarias_admin ON cuentas_bancarias(admin_id);

-- Asignación de cuenta por departamento
ALTER TABLE departamentos ADD COLUMN IF NOT EXISTS cuenta_bancaria_id UUID
  REFERENCES cuentas_bancarias(id) ON DELETE SET NULL;

-- Migración de datos: si un admin ya tenía banco_* en configuracion y aún no
-- tiene ninguna cuenta bancaria, crear una predeterminada con esos datos.
INSERT INTO cuentas_bancarias (admin_id, alias, banco_nombre, banco_clabe, banco_titular, es_predeterminada)
SELECT c.admin_id,
       'Cuenta principal',
       MAX(CASE WHEN c.clave = 'banco_nombre'  THEN c.valor END),
       MAX(CASE WHEN c.clave = 'banco_clabe'   THEN c.valor END),
       MAX(CASE WHEN c.clave = 'banco_titular' THEN c.valor END),
       TRUE
FROM configuracion c
WHERE c.clave IN ('banco_nombre', 'banco_clabe', 'banco_titular')
GROUP BY c.admin_id
HAVING COALESCE(MAX(CASE WHEN c.clave = 'banco_clabe' THEN c.valor END), '') <> ''
   AND NOT EXISTS (SELECT 1 FROM cuentas_bancarias cb WHERE cb.admin_id = c.admin_id);

-- Abonos múltiples por periodo de pago (soporta pagos parciales con historial)
CREATE TABLE IF NOT EXISTS abonos_pago (
  id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  pago_id         UUID NOT NULL REFERENCES pagos(id) ON DELETE CASCADE,
  monto           NUMERIC(10,2) NOT NULL CHECK (monto > 0),
  fecha           DATE NOT NULL DEFAULT CURRENT_DATE,
  metodo          VARCHAR(20) NOT NULL DEFAULT 'efectivo',
  nota            TEXT,
  comprobante_url TEXT,
  registrado_por  UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_abonos_pago_pago  ON abonos_pago(pago_id);
CREATE INDEX IF NOT EXISTS idx_abonos_pago_fecha ON abonos_pago(fecha);

-- Migración de datos: los pagos ya confirmados se tratan como un abono único
-- igual al monto total, para no perder historial al introducir abonos_pago.
INSERT INTO abonos_pago (pago_id, monto, fecha, metodo, nota, registrado_por, created_at)
SELECT p.id, p.monto,
       COALESCE(p.confirmado_en::date, p.created_at::date),
       p.metodo,
       'Migración automática — pago histórico confirmado antes del sistema de abonos',
       p.escaneado_por,
       COALESCE(p.confirmado_en, p.created_at)
FROM pagos p
WHERE p.confirmado = true
  AND NOT EXISTS (SELECT 1 FROM abonos_pago a WHERE a.pago_id = p.id);

-- Fecha en la que el inquilino prometió pagar un periodo adeudado (nota del admin)
ALTER TABLE pagos ADD COLUMN IF NOT EXISTS fecha_promesa DATE;

-- Abonos parciales al depósito (independiente de la renta, sin fila en pagos)
CREATE TABLE IF NOT EXISTS abonos_deposito (
  id              UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  inquilino_id    UUID NOT NULL REFERENCES inquilinos(id) ON DELETE CASCADE,
  monto           NUMERIC(10,2) NOT NULL CHECK (monto > 0),
  fecha           DATE NOT NULL DEFAULT CURRENT_DATE,
  metodo          VARCHAR(20) NOT NULL DEFAULT 'efectivo',
  nota            TEXT,
  comprobante_url TEXT,
  registrado_por  UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_abonos_deposito_inquilino ON abonos_deposito(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_abonos_deposito_fecha ON abonos_deposito(fecha);

-- Historial de fechas de promesa de pago (cada vez que se guarda una fecha
-- se agrega una fila, en vez de solo sobrescribir pagos.fecha_promesa).
CREATE TABLE IF NOT EXISTS promesas_pago (
  id             UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  pago_id        UUID NOT NULL REFERENCES pagos(id) ON DELETE CASCADE,
  fecha_promesa  DATE NOT NULL,
  creado_por     UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_promesas_pago_pago ON promesas_pago(pago_id);

-- Migración de datos: conservar en el historial la promesa vigente que ya
-- estaba guardada en pagos.fecha_promesa antes de introducir este historial.
INSERT INTO promesas_pago (pago_id, fecha_promesa, created_at)
SELECT p.id, p.fecha_promesa, p.created_at
FROM pagos p
WHERE p.fecha_promesa IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM promesas_pago pp WHERE pp.pago_id = p.id);

-- Contrato editado por inquilino: si está lleno, el PDF se genera a partir de
-- este HTML (con {{variables}}) en vez de la plantilla global o la de por defecto.
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS contrato_html TEXT;

-- Suscripciones Web Push (navegador) — independientes de los tokens de Expo (app nativa)
CREATE TABLE IF NOT EXISTS web_push_subscriptions (
  id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  usuario_id  UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  endpoint    TEXT NOT NULL UNIQUE,
  p256dh      TEXT NOT NULL,
  auth        TEXT NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_web_push_subscriptions_usuario ON web_push_subscriptions(usuario_id);

-- ═══ Módulo de Contratos (plantillas Word + historial) ═══════════════════════
-- Plantillas .docx propias de cada admin. `docx_plantilla` es el Word con las {{variables}} ya
-- insertadas; `docx_original` el archivo tal como lo subió el usuario. `marcas` guarda las
-- sustituciones aceptadas (para poder volver a editarlas) y `campos` los datos personalizados.
CREATE TABLE IF NOT EXISTS plantillas_contrato (
  id             UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  admin_id       UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  nombre         VARCHAR(150) NOT NULL,
  descripcion    TEXT,
  docx_original  BYTEA NOT NULL,
  docx_plantilla BYTEA NOT NULL,
  campos         JSONB NOT NULL DEFAULT '[]',
  marcas         JSONB NOT NULL DEFAULT '[]',
  estado         VARCHAR(20) NOT NULL DEFAULT 'borrador',
  predeterminada BOOLEAN NOT NULL DEFAULT FALSE,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_plantillas_contrato_admin ON plantillas_contrato(admin_id);
CREATE UNIQUE INDEX IF NOT EXISTS uq_plantilla_predeterminada ON plantillas_contrato(admin_id) WHERE predeterminada;

ALTER TABLE departamentos ADD COLUMN IF NOT EXISTS plantilla_id UUID REFERENCES plantillas_contrato(id) ON DELETE SET NULL;
ALTER TABLE inquilinos    ADD COLUMN IF NOT EXISTS plantilla_id UUID REFERENCES plantillas_contrato(id) ON DELETE SET NULL;

-- Datos que los contratos suelen pedir y que antes no se guardaban.
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS domicilio        TEXT;
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS identificacion   VARCHAR(100);
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS email_contacto   VARCHAR(150);
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS fiador_domicilio TEXT;

-- Historial de contratos generados (una fila por versión).
CREATE TABLE IF NOT EXISTS contratos (
  id               UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  inquilino_id     UUID NOT NULL REFERENCES inquilinos(id) ON DELETE CASCADE,
  depto_numero     INTEGER,
  datos_contrato   JSONB NOT NULL DEFAULT '{}',
  pdf_url          VARCHAR(500),
  generado_por     UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  fecha_generacion TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS admin_id         UUID REFERENCES usuarios(id) ON DELETE CASCADE;
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS plantilla_id     UUID REFERENCES plantillas_contrato(id) ON DELETE SET NULL;
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS plantilla_nombre VARCHAR(150);
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS version          INTEGER NOT NULL DEFAULT 1;
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS docx             BYTEA;
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS pdf              BYTEA;
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS valores_extra    JSONB NOT NULL DEFAULT '{}';
ALTER TABLE contratos ADD COLUMN IF NOT EXISTS aproximado       BOOLEAN NOT NULL DEFAULT FALSE;
CREATE INDEX IF NOT EXISTS idx_contratos_inquilino ON contratos(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_contratos_admin_inquilino ON contratos(admin_id, inquilino_id);

-- La renta con letra ahora se calcula sola cuando no se captura.
ALTER TABLE inquilinos ALTER COLUMN renta_letra DROP NOT NULL;
