#!/bin/bash
# dev.sh - Entorno de DESARROLLO local
# Requiere: .NET SDK 10 (backend), Node.js (web), Docker (solo para levantar PostgreSQL)

export PATH="$HOME/.local/bin:$PATH"

echo "╔══════════════════════════════════════════╗"
echo "║  🏠 VertexRent — Entorno de Desarrollo   ║"
echo "╚══════════════════════════════════════════╝"
echo ""

# ─── Verificar .NET SDK y Node.js ───────────────────────────────────────────
if ! command -v dotnet &> /dev/null; then
  echo "❌ .NET SDK no encontrado. Instálalo desde https://dotnet.microsoft.com/download (versión 10)."
  exit 1
fi
echo "✅ .NET SDK $(dotnet --version)"
if ! command -v node &> /dev/null; then
  echo "❌ Node.js no encontrado (necesario para la web). Instálalo o agrégalo al PATH."
  exit 1
fi
echo "✅ Node.js $(node -v)"

# ─── Detectar docker compose (v2 preferido sobre v1) ───────────────────────
if docker compose version &> /dev/null 2>&1; then
  DC="docker compose"
elif command -v docker-compose &> /dev/null; then
  DC="docker-compose"
else
  DC=""
fi

# ─── Limpieza al salir (guard contra doble ejecución) ──────────────────────
_CLEANUP_DONE=0
cleanup() {
  [ "$_CLEANUP_DONE" = "1" ] && return
  _CLEANUP_DONE=1
  echo ""
  echo "🛑 Deteniendo todos los procesos..."
  jobs -p | xargs -r kill -TERM 2>/dev/null || true
  pkill -f "VertexRent.Api" 2>/dev/null || true
  pkill -f "vite" 2>/dev/null || true
  sleep 0.5
  jobs -p | xargs -r kill -KILL 2>/dev/null || true
  echo "✅ Procesos detenidos"
}
trap 'cleanup; exit 0' SIGINT SIGTERM
trap 'cleanup' EXIT

# ─── Instalar dependencias si faltan ───────────────────────────────────────
echo "📦 Restaurando dependencias del backend (.NET)..."
(cd backend/VertexRent.Api && dotnet restore --verbosity quiet)
if [ ! -d "web/node_modules" ]; then
  echo "📦 Instalando dependencias de la web..."
  (cd web && npm install)
fi

# ─── Levantar PostgreSQL en Docker ─────────────────────────────────────────
if [ -n "$DC" ]; then
  echo "🐳 Iniciando base de datos PostgreSQL..."
  $DC -f docker-compose.yml -f docker-compose.dev.yml up -d db

  echo "⏳ Esperando a que PostgreSQL esté listo..."
  RETRIES=30
  COUNT=0
  until docker exec depas_db pg_isready -U "depas_user" -d "departamentos" &>/dev/null 2>&1; do
    COUNT=$((COUNT + 1))
    if [ "$COUNT" -ge "$RETRIES" ]; then
      echo ""
      echo "❌ PostgreSQL no respondió. Revisa: docker logs depas_db"
      exit 1
    fi
    printf "   ... esperando (%d/%d)\r" "$COUNT" "$RETRIES"
    sleep 1
  done
  echo "✅ PostgreSQL listo                    "
else
  echo "⚠️  Docker no encontrado. Asegúrate de tener PostgreSQL corriendo en localhost:5432"
fi

# ─── Detectar IP de la red local (para abrir la web desde el celular) ──────
LAN_IP=$(ipconfig getifaddr en0 2>/dev/null \
  || ipconfig getifaddr en1 2>/dev/null \
  || hostname -I 2>/dev/null | awk '{print $1}' \
  || echo "TU_IP_LOCAL")

echo ""
echo "🚀 Iniciando servicios en modo desarrollo..."
echo "   Backend:  http://localhost:3001"
echo "   Web:      http://localhost:5173   (desde el celular: http://${LAN_IP}:5173)"
echo ""
echo "   La web usa un proxy: /api → http://localhost:3001, no hace falta configurar la URL del API."
echo "   Nota: la cámara (escáner QR) y las notificaciones push del celular requieren HTTPS."
echo "   Presiona Ctrl+C para detener ambos servicios"
echo ""

# ─── Iniciar Backend ───────────────────────────────────────────────────────
# Las migraciones seguras (ALTER TABLE IF NOT EXISTS) se ejecutan
# automáticamente dentro del backend al arrancar (Sql/startup.sql)
# `dotnet watch` recompila y reinicia al guardar cambios (equivalente al antiguo ts-node-dev).
# La configuración se lee de backend/.env (o de las variables de entorno del shell).
(cd backend/VertexRent.Api && PORT=3001 dotnet watch run --non-interactive) &
BACKEND_PID=$!

# Esperar a que el backend responda en /health
echo "⏳ Esperando que el backend arranque..."
RETRIES=30
COUNT=0
until curl -sf http://localhost:3001/health &>/dev/null 2>&1; do
  COUNT=$((COUNT + 1))
  if [ "$COUNT" -ge "$RETRIES" ]; then
    echo "⚠️  Backend tardó más de lo esperado — iniciando la web de todos modos..."
    break
  fi
  sleep 1
done
[ "$COUNT" -lt "$RETRIES" ] && echo "✅ Backend listo (http://localhost:3001)"

# ─── Iniciar Web (Vite) ────────────────────────────────────────────────────
# --host expone la web en la red local para probarla desde el celular.
(cd web && npm run dev -- --host) &
FRONTEND_PID=$!

# Esperar ambos procesos
wait $BACKEND_PID $FRONTEND_PID
