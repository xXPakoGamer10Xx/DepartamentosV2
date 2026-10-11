# ---- Build Web (React + Vite, PWA) ----
FROM node:22-alpine AS web-builder
WORKDIR /app/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
# El API vive en el mismo dominio que la web (la sirve el backend)
ENV VITE_API_URL=/api
# Llave pública VAPID para Web Push (pública por diseño, va embebida en el bundle del cliente)
ENV VITE_VAPID_PUBLIC_KEY=BHyeq9PZ1co5YK671ZOWXLfUn2VwPvRMPFjZVpWf3EB_WbgXAhTlIKfGuAhbZnkipi2Mgr9yGqOtcaHQcOxMIt8
RUN npm run build

# ---- Build Backend (.NET) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-builder
WORKDIR /src
# Restaurar primero (capa cacheable) y luego compilar
COPY backend/VertexRent.Api/VertexRent.Api.csproj VertexRent.Api/
RUN dotnet restore VertexRent.Api/VertexRent.Api.csproj
COPY backend/VertexRent.Api/ VertexRent.Api/
RUN dotnet publish VertexRent.Api/VertexRent.Api.csproj -c Release -o /out --no-restore

# ---- Production Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS production
WORKDIR /app

# - postgresql-client-16: pg_dump para los respaldos (debe ser >= la versión del servidor, postgres:16)
# - curl: healthcheck;  tzdata: zona horaria America/Mexico_City
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates gnupg tzdata \
 && install -d /usr/share/postgresql-common/pgdg \
 && curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc \
 && . /etc/os-release \
 && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends postgresql-client-16 \
 && rm -rf /var/lib/apt/lists/*

# - LibreOffice (solo Writer, sin interfaz): convierte el Word del contrato a PDF idéntico al original
# - Fuentes métricamente compatibles con Arial, Times New Roman, Calibri y Cambria, para que el PDF
#   conserve el mismo salto de líneas y páginas que el Word del usuario
RUN apt-get update \
 && apt-get install -y --no-install-recommends libreoffice-writer-nogui fonts-liberation fonts-crosextra-carlito fonts-crosextra-caladea fonts-dejavu-core \
 && rm -rf /var/lib/apt/lists/*

# Backend publicado (incluye Sql/ y Fonts/)
COPY --from=backend-builder /out ./
# Web compilada (incluye sw.js, manifest e iconos) → servida por el backend desde ./public
COPY --from=web-builder /app/web/dist ./public

# Directorio de backups
RUN mkdir -p /app/backups

# Usuario no-root (la imagen de .NET ya trae el usuario "app")
RUN chown -R app:app /app
USER app

ENV SOFFICE_PATH=/usr/bin/soffice \
    PORT=3001 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1 \
    DOTNET_EnableDiagnostics=0

EXPOSE 3001

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -fsS http://localhost:3001/health || exit 1

# Sin argumentos arranca la API; con `--migrate` recrea el esquema (servicio "migrate" de compose).
ENTRYPOINT ["dotnet", "VertexRent.Api.dll"]
