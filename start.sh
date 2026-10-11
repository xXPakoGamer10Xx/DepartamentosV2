#!/bin/bash
# start.sh - Producción en VPS con Docker
# Uso:  ./start.sh           → arranque normal (preserva datos)
#       ./start.sh --reset   → borrar todo y empezar desde cero

set -e

echo "╔══════════════════════════════════════════════╗"
echo "║  🏠 VertexRent — Iniciar en Producción       ║"
echo "╚══════════════════════════════════════════════╝"
echo ""

FORCE_RESET=0
if [ "$1" = "--reset" ]; then
  FORCE_RESET=1
  echo "⚠️  MODO RESET: Se borrarán TODOS los datos y se recreará la base de datos."
  echo "   Presiona Ctrl+C en los próximos 5 segundos para cancelar..."
  sleep 5
  echo ""
fi

# ─── Verificar docker ───────────────────────────────────────────────────────
if ! command -v docker &> /dev/null; then
  echo "❌ Docker no está instalado."
  echo "   Instálalo con: curl -fsSL https://get.docker.com | bash"
  exit 1
fi
echo "✅ Docker $(docker --version | cut -d' ' -f3 | tr -d ',')"

if ! docker compose version &> /dev/null; then
  echo "❌ Docker Compose v2 no está disponible."
  echo "   Asegúrate de tener Docker Engine >= 20.10"
  exit 1
fi
echo "✅ Docker Compose $(docker compose version --short)"
echo ""

# ─── Generar .env si no existe ─────────────────────────────────────────────
if [ ! -f ".env" ]; then
  echo "⚙️  Archivo .env no encontrado. Generando configuración automática..."

  JWT_SECRET=$(openssl rand -hex 48 2>/dev/null || cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 64 | head -n 1)
  DB_PASSWORD=$(openssl rand -hex 16 2>/dev/null || cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 24 | head -n 1)

  cat > .env <<EOF
# Generado automáticamente por start.sh — $(date)

# Server
NODE_ENV=production
PORT=3001
TZ=America/Mexico_City

# Database
DB_HOST=db
DB_PORT=5432
DB_NAME=departamentos
DB_USER=depas_user
DB_PASSWORD=${DB_PASSWORD}

# JWT
JWT_SECRET=${JWT_SECRET}
JWT_EXPIRES_IN=7d

# CORS — dominios permitidos en producción (separados por coma)
FRONTEND_URL=https://laspuercotas.xyz

# Google Gemini — necesaria para el procesado IA del contrato (opcional).
# Obtén una clave en https://aistudio.google.com/apikey y pégala aquí.
GOOGLE_AI_API_KEY=

# Backup
BACKUP_DIR=/app/backups
BACKUP_CRON="0 4 * * *"

# Configuración Inicial
ARRENDADOR_NOMBRE=Admin
EOF

  echo "✅ Archivo .env generado"
  echo "   🔑 DB_PASSWORD → ${DB_PASSWORD}"
  echo "   ℹ️  Guarda estas credenciales en un lugar seguro."
  echo ""
else
  echo "✅ Archivo .env encontrado"
  echo ""
fi

# Leer variables esenciales sin hacer 'source'
DB_USER=$(grep -E '^DB_USER=' .env | cut -d'=' -f2 | tr -d '"' | tr -d "'")
DB_NAME=$(grep -E '^DB_NAME=' .env | cut -d'=' -f2 | tr -d '"' | tr -d "'")
DB_USER="${DB_USER:-depas_user}"
DB_NAME="${DB_NAME:-departamentos}"

# ─── Crear directorio de backups ────────────────────────────────────────────
mkdir -p backups
echo "✅ Directorio de backups listo"

# ─── Limpiar contenedores anteriores ────────────────────────────────────────
echo ""
echo "🧹 Limpiando contenedores anteriores..."
docker compose down --remove-orphans 2>/dev/null || true

# ─── Levantar base de datos ─────────────────────────────────────────────────
echo ""
echo "🚀 Levantando base de datos..."
docker compose up -d db

echo ""
echo "⏳ Esperando que PostgreSQL esté listo..."
RETRIES=30
COUNT=0
until docker compose exec -T db pg_isready -U "${DB_USER}" -d "${DB_NAME}" &>/dev/null; do
  COUNT=$((COUNT + 1))
  if [ "$COUNT" -ge "$RETRIES" ]; then
    echo ""
    echo "❌ La base de datos no respondió. Revisa: docker compose logs db"
    exit 1
  fi
  printf "   ... esperando (%d/%d)\r" "$COUNT" "$RETRIES"
  sleep 2
done
echo "✅ PostgreSQL listo                    "

# ─── Detectar si es el primer despliegue ────────────────────────────────────
TABLE_EXISTS=$(docker compose exec -T db psql -U "${DB_USER}" -d "${DB_NAME}" -tAc \
  "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='inquilinos'" \
  2>/dev/null | tr -d '[:space:]' || echo "0")

if [ "$FORCE_RESET" = "1" ] || [ "${TABLE_EXISTS:-0}" = "0" ]; then
  if [ "$FORCE_RESET" = "1" ]; then
    echo ""
    echo "🔄 Reset forzado — ejecutando migraciones completas..."
  else
    echo ""
    echo "🔄 Primer despliegue — creando esquema de base de datos..."
  fi

  docker compose build migrate
  docker compose --profile tools run --rm migrate
  echo "✅ Esquema creado"
else
  echo "✅ Base de datos ya inicializada — omitiendo migraciones"
  echo "   (usa ./start.sh --reset para borrar todo y empezar de cero)"
fi

# ─── Construir y levantar backend ───────────────────────────────────────────
echo ""
echo "🔨 Construyendo backend..."
# Intenta BuildKit primero; si falla (plugin corrupto), usa el builder legacy
if ! docker compose build backend 2>/dev/null; then
  echo "   BuildKit no disponible, usando builder legacy..."
  DOCKER_BUILDKIT=0 docker build -f backend/Dockerfile -t departamentos-backend:latest . || {
    echo "❌ Error al construir la imagen. Revisa los logs."
    exit 1
  }
fi

echo ""
echo "🚀 Iniciando backend..."
if ! docker compose up -d backend 2>/dev/null; then
  # Fallback: arrancar contenedores manualmente
  NETWORK=$(docker network ls --filter name=departamentos_default -q 2>/dev/null)
  [ -z "$NETWORK" ] && docker network create departamentos_default 2>/dev/null || true
  docker rm -f depas_backend 2>/dev/null || true
  DB_PASS=$(grep -E '^DB_PASSWORD=' .env | cut -d'=' -f2)
  JWT=$(grep -E '^JWT_SECRET=' .env | cut -d'=' -f2)
  FRONTEND_U=$(grep -E '^FRONTEND_URL=' .env | cut -d'=' -f2)
  docker run -d --name depas_backend --network departamentos_default \
    --restart unless-stopped -p 127.0.0.1:3001:3001 \
    -e NODE_ENV=production -e PORT=3001 -e TZ=America/Mexico_City \
    -e DB_HOST=db -e DB_PORT=5432 -e DB_NAME="${DB_NAME}" -e DB_USER="${DB_USER}" \
    -e DB_PASSWORD="${DB_PASS}" -e JWT_SECRET="${JWT}" -e JWT_EXPIRES_IN=7d \
    -e FRONTEND_URL="${FRONTEND_U}" -e BACKUP_DIR=/app/backups \
    -v "$(pwd)/backups:/app/backups" \
    departamentos-backend:latest
fi

# ─── Esperar a que el backend responda ──────────────────────────────────────
echo ""
echo "🔍 Verificando que el sistema responde..."
HEALTH_RETRIES=20
HEALTH_COUNT=0
until wget -qO- http://localhost:3001/health &>/dev/null 2>&1 || \
      curl -sf http://localhost:3001/health &>/dev/null 2>&1; do
  HEALTH_COUNT=$((HEALTH_COUNT + 1))
  if [ "$HEALTH_COUNT" -ge "$HEALTH_RETRIES" ]; then
    echo ""
    echo "⚠️  El backend aún no responde. Revisa los logs:"
    echo "   docker compose logs backend"
    break
  fi
  printf "   ... verificando (%d/%d)\r" "$HEALTH_COUNT" "$HEALTH_RETRIES"
  sleep 2
done

if wget -qO- http://localhost:3001/health &>/dev/null 2>&1 || \
   curl -sf http://localhost:3001/health &>/dev/null 2>&1; then
  echo "✅ Sistema respondiendo correctamente"
fi

# ─── Resumen final ──────────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════╗"
echo "║        ✅  Sistema en producción listo        ║"
echo "╚══════════════════════════════════════════════╝"
echo ""
echo "   🌍 Web App: http://localhost:3001"
echo "   📡 API:     http://localhost:3001/api"
echo ""
echo "Comandos útiles:"
echo "  Ver logs:          docker compose logs -f backend"
echo "  Detener todo:      docker compose down"
echo "  Reiniciar backend: docker compose restart backend"
echo "  Reset completo:    ./start.sh --reset"
