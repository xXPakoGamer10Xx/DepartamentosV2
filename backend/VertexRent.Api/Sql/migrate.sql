
-- Wipe existing tables for SaaS rewrite
DROP TABLE IF EXISTS plantillas_contrato CASCADE;
DROP TABLE IF EXISTS usuarios, departamentos, inquilinos, pagos, contratos, auditoria, backups, configuracion, tickets, cuotas_extra CASCADE;

-- Extensiones
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "unaccent";

-- Tabla de usuarios (admins e inquilinos)
CREATE TABLE IF NOT EXISTS usuarios (
  id          UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  email       VARCHAR(255) UNIQUE NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  nombre_completo VARCHAR(255) NOT NULL,
  rol         VARCHAR(20) NOT NULL DEFAULT 'inquilino'
                CHECK (rol IN ('admin', 'inquilino', 'cobrador')),
  avatar_url  TEXT,
  activo      BOOLEAN NOT NULL DEFAULT TRUE,
  rol_label   VARCHAR(50),
  ultimo_acceso TIMESTAMPTZ,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Tabla de departamentos
CREATE TABLE IF NOT EXISTS departamentos (
  id          SERIAL PRIMARY KEY,
  admin_id    UUID REFERENCES usuarios(id) ON DELETE CASCADE,
  numero      INTEGER NOT NULL,
  descripcion TEXT,
  estado      VARCHAR(20) NOT NULL DEFAULT 'disponible'
                CHECK (estado IN ('disponible', 'ocupado', 'mantenimiento')),
  inventario_base JSONB NOT NULL DEFAULT '[]',
  ubicacion   VARCHAR(500) NOT NULL DEFAULT 'Callejón Zaragoza s/n, San Juan Chiautla, C.P. 56030',
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(numero)
);

-- Tabla de inquilinos
CREATE TABLE IF NOT EXISTS inquilinos (
  id                UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  admin_id          UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  nombre_completo   VARCHAR(255) NOT NULL,
  depto_numero      INTEGER NOT NULL,
  renta             NUMERIC(10,2) NOT NULL,
  deposito          NUMERIC(10,2) NOT NULL DEFAULT 0,
  renta_letra       VARCHAR(255) NOT NULL,
  fecha_pago        VARCHAR(100) NOT NULL,
  fecha_inicio      DATE NOT NULL,
  fecha_termino     DATE NOT NULL,
  fiador_nombre     VARCHAR(255),
  tel_arrendatario  VARCHAR(20),
  fiador_telefono   VARCHAR(20),
  observaciones     TEXT,
  inventario        JSONB NOT NULL DEFAULT '[]',
  estado            VARCHAR(20) NOT NULL DEFAULT 'activo'
                      CHECK (estado IN ('activo', 'inactivo', 'vencido')),
  usuario_id        UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  email_invitacion  VARCHAR(255),
  invitation_token  UUID UNIQUE,
  deposito_tipo     VARCHAR(50) DEFAULT 'ninguno',
  deposito_fechas   JSONB DEFAULT '[]',
  metodo_pago       VARCHAR(20) DEFAULT 'efectivo'
                      CHECK (metodo_pago IS NULL OR metodo_pago IN ('efectivo', 'transferencia', 'ambos')),
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Tabla de pagos
CREATE TABLE IF NOT EXISTS pagos (
  id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  inquilino_id  UUID NOT NULL REFERENCES inquilinos(id) ON DELETE CASCADE,
  periodo       VARCHAR(7) NOT NULL,
  monto         NUMERIC(10,2) NOT NULL,
  metodo        VARCHAR(20) NOT NULL DEFAULT 'efectivo',
  qr_token      UUID UNIQUE,
  confirmado    BOOLEAN NOT NULL DEFAULT FALSE,
  confirmado_en TIMESTAMPTZ,
  escaneado_por UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  escaneado_en  TIMESTAMPTZ,
  comprobante_url       TEXT,
  comprobante_subido_en TIMESTAMPTZ,
  rechazado             BOOLEAN NOT NULL DEFAULT FALSE,
  rechazado_en          TIMESTAMPTZ,
  comentario_admin      TEXT,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(inquilino_id, periodo)
);

-- Tabla de contratos
CREATE TABLE IF NOT EXISTS contratos (
  id                UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  inquilino_id      UUID NOT NULL REFERENCES inquilinos(id) ON DELETE CASCADE,
  depto_numero      INTEGER NOT NULL,
  datos_contrato    JSONB NOT NULL,
  pdf_url           VARCHAR(500),
  generado_por      UUID REFERENCES usuarios(id),
  fecha_generacion  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Tabla de auditoría (quién hizo qué y cuándo)
CREATE TABLE IF NOT EXISTS auditoria (
  id                UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  usuario_id        UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  tabla_afectada    VARCHAR(50) NOT NULL,
  accion            VARCHAR(20) NOT NULL
                      CHECK (accion IN ('crear', 'editar', 'eliminar')),
  registro_id       UUID,
  datos_anteriores  JSONB,
  datos_nuevos      JSONB,
  ip_address        VARCHAR(50),
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Tabla de backups
CREATE TABLE IF NOT EXISTS backups (
  id            SERIAL PRIMARY KEY,
  archivo       VARCHAR(500) NOT NULL,
  tamano_bytes  BIGINT NOT NULL DEFAULT 0,
  tipo          VARCHAR(20) NOT NULL DEFAULT 'automatico'
                  CHECK (tipo IN ('automatico', 'manual')),
  estado        VARCHAR(20) NOT NULL DEFAULT 'completado'
                  CHECK (estado IN ('completado', 'fallido')),
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Tabla de configuración global (por admin)
CREATE TABLE IF NOT EXISTS configuracion (
  admin_id UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  clave   VARCHAR(100) NOT NULL,
  valor   TEXT NOT NULL,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (admin_id, clave)
);

-- Tabla de tickets (reportes de problemas)
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

-- Tabla de cuotas extra (cargos adicionales por departamento)
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

-- Tabla de códigos de invitación con expiración
CREATE TABLE IF NOT EXISTS codigos_invitacion (
  id         UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
  admin_id   UUID NOT NULL REFERENCES usuarios(id) ON DELETE CASCADE,
  codigo     VARCHAR(10) NOT NULL UNIQUE,
  rol        VARCHAR(20) NOT NULL DEFAULT 'inquilino'
             CHECK (rol IN ('inquilino', 'cobrador')),
  expira_en  TIMESTAMPTZ NULL,
  usado      BOOLEAN NOT NULL DEFAULT FALSE,
  usado_en   TIMESTAMPTZ,
  usado_por  UUID REFERENCES usuarios(id) ON DELETE SET NULL,
  rol_label  VARCHAR(50),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Índices para mejorar búsquedas
CREATE INDEX IF NOT EXISTS idx_codigos_invitacion_codigo ON codigos_invitacion(codigo);
CREATE INDEX IF NOT EXISTS idx_inquilinos_estado ON inquilinos(estado);
CREATE INDEX IF NOT EXISTS idx_inquilinos_depto ON inquilinos(depto_numero);
CREATE INDEX IF NOT EXISTS idx_auditoria_usuario ON auditoria(usuario_id);
CREATE INDEX IF NOT EXISTS idx_auditoria_tabla ON auditoria(tabla_afectada, registro_id);
CREATE INDEX IF NOT EXISTS idx_contratos_inquilino ON contratos(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_pagos_inquilino ON pagos(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_pagos_periodo ON pagos(periodo);
CREATE INDEX IF NOT EXISTS idx_pagos_qr_token ON pagos(qr_token);
CREATE INDEX IF NOT EXISTS idx_tickets_inquilino ON tickets(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_tickets_estado ON tickets(estado);
CREATE INDEX IF NOT EXISTS idx_cuotas_inquilino ON cuotas_extra(inquilino_id);
CREATE INDEX IF NOT EXISTS idx_cuotas_estado ON cuotas_extra(estado);

-- Función para actualizar updated_at automáticamente
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
   NEW.updated_at = NOW();
   RETURN NEW;
END;
$$ language 'plpgsql';

-- Triggers de updated_at
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_usuarios_updated_at') THEN
    CREATE TRIGGER update_usuarios_updated_at
      BEFORE UPDATE ON usuarios
      FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_departamentos_updated_at') THEN
    CREATE TRIGGER update_departamentos_updated_at
      BEFORE UPDATE ON departamentos
      FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'update_inquilinos_updated_at') THEN
    CREATE TRIGGER update_inquilinos_updated_at
      BEFORE UPDATE ON inquilinos
      FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
  END IF;
END;
$$;
