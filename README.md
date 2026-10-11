# 🏠 NethRent — Sistema de Gestión Inteligente de Arrendamientos

[![TypeScript](https://img.shields.io/badge/TypeScript-007ACC?style=for-the-badge&logo=typescript&logoColor=white)](https://www.typescriptlang.org/)
[![.NET](https://img.shields.io/badge/.NET_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://learn.microsoft.com/aspnet/core)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-316192?style=for-the-badge&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![React](https://img.shields.io/badge/React_19-20232A?style=for-the-badge&logo=react&logoColor=61DAFB)](https://react.dev/)
[![Vite](https://img.shields.io/badge/Vite-646CFF?style=for-the-badge&logo=vite&logoColor=white)](https://vite.dev/)
[![Docker](https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white)](https://www.docker.com/)

**NethRent** es una solución integral y moderna diseñada para la administración y automatización de departamentos en alquiler, cobro de rentas, gestión de contratos, control de incidencias (tickets) y comunicación fluida entre el arrendador y los inquilinos. 

El sistema está optimizado para funcionar tanto en dispositivos móviles (Android/iOS) como en navegadores web de escritorio, ofreciendo una experiencia responsiva, fluida y en tiempo real.

---

## 📌 Índice de Contenidos
1. [✨ Características Principales](#-características-principales)
2. [⚙️ Arquitectura y Tecnologías](#️-arquitectura-y-tecnologías)
3. [📁 Estructura del Proyecto](#-estructura-del-proyecto)
4. [🚀 Despliegue en Producción (VPS)](#-despliegue-en-producción-vps)
5. [💻 Configuración para Desarrollo Local](#-configuración-para-desarrollo-local)
6. [🔐 Variables de Entorno](#-variables-de-entorno)
7. [👥 Credenciales de Prueba (Seed)](#-credenciales-de-prueba-seed)
8. [💾 Copias de Seguridad (Backups)](#-copias-de-seguridad-backups)
9. [🔌 API Endpoints](#-api-endpoints)

---

## ✨ Características Principales

### 👨‍💼 Módulo de Administración (Arrendador)
- **Control General**: Visualización de estadísticas financieras en tiempo real, ocupación y alertas de atrasos.
- **Gestión de Inquilinos**: Filtros avanzados de búsqueda, registro, edición e historial completo de pagos.
- **Contratos con tu propio Word**: incluye una plantilla lista para usar y permite subir tu contrato en `.docx`; el sistema (con IA) detecta dónde van los nombres, montos y fechas, y genera el contrato de cada inquilino idéntico a tu documento, en PDF y Word. Admite varias plantillas (predeterminada o por propiedad), campos personalizados e historial de versiones.
- **Auditoría Interna**: Registro transparente de actividades críticas dentro del sistema.

### 👥 Módulo del Inquilino (Multiplataforma)
- **Diseño Adaptativo Premium**: Panel web con vista en rejilla de doble columna (desktop) y diseño optimizado a una columna en dispositivos móviles.
- **Estado de Cuenta**: Desglose automático de cargos pendientes (renta del mes + cuotas o servicios extra).
- **Recibos de Pago QR**: Descarga de códigos QR únicos y comprobantes oficiales para agilizar los pagos.
- **Sistema de Incidencias (Tickets)**: Reporte y seguimiento interactivo de fallos, reparaciones o solicitudes de soporte con estados de progreso.

### 🔔 Notificaciones Inteligentes y en Tiempo Real
- **Canal Web (SSE)**: Conexión mediante Server-Sent Events (SSE) para alertas instantáneas sin recargar la página.
- **Canal Push (Web Push)**: Notificaciones del navegador / PWA instalada (Android, escritorio y iPhone con la app en la pantalla de inicio) para recordar fechas de pago aunque la app esté cerrada.

---

## ⚙️ Arquitectura y Tecnologías

El sistema utiliza una arquitectura desacoplada y modular diseñada para la escalabilidad:

- **Frontend (Web / PWA)**: React 19 + Vite + TypeScript + Tailwind, con TanStack Query y React Router. Carga diferida por pantalla (~100 KB gzip iniciales) y se instala en el celular como PWA (service worker con precache y Web Push).
- **Backend (API REST)**: ASP.NET Core (.NET 10, C#) con Minimal APIs y Npgsql. Un archivo de endpoints por módulo, filtros de autenticación/permisos, servicios en segundo plano (recordatorios y respaldos) y generación de PDF/DOCX con QuestPDF y Open XML.
- **Base de Datos**: PostgreSQL para almacenamiento relacional robusto con integridad referencial.
- **Infraestructura**: Orquestación nativa mediante Docker Compose para garantizar consistencia entre desarrollo y producción.

---

## 📁 Estructura del Proyecto

```text
Departamentos/
├── web/                    # Frontend web / PWA (React + Vite + TypeScript)
│   ├── src/pages/          # Pantallas (inquilino/, admin/, auth)
│   ├── src/components/     # UI reutilizable y layout (sidebar / barra inferior)
│   ├── src/lib/            # Cliente del API, sesión, SSE, push, permisos
│   └── src/sw.ts           # Service worker (precache + Web Push)
├── backend/                # Backend API REST (ASP.NET Core / .NET 10)
│   ├── VertexRent.Api/
│   │   ├── Program.cs      # Arranque: middleware, CORS, límites, migraciones y servicios
│   │   ├── Endpoints/      # Un archivo por módulo (auth, pagos, inquilinos, ...)
│   │   ├── Auth/           # JWT, filtros de autorización y catálogo de permisos
│   │   ├── Services/       # Push/SSE, scheduler, respaldos, PDF, DOCX, Gemini
│   │   ├── Templates/      # Plantilla de contrato del sistema (Word) y respaldo QuestPDF
│   │   ├── Infrastructure/ # Acceso a datos (Npgsql), validación y helpers
│   │   ├── Sql/            # startup.sql (migraciones idempotentes) y migrate.sql
│   │   └── Fonts/          # Liberation Sans (SIL OFL) para los PDF
│   ├── Dockerfile          # Empaquetado Docker (frontend + backend)
│   └── .env                # Configuración local del backend
├── backups/                # Directorio automático para copias de seguridad (.sql)
├── docker-compose.yml      # Definición de servicios para producción (API + DB)
├── dev.sh                  # Script interactivo de desarrollo local
└── start.sh                # Script autogestionado de arranque en producción
```

---

## 🚀 Despliegue en Producción (VPS)

> [!IMPORTANT]
> El sistema incluye un script automatizado (`start.sh`) que realiza todo el proceso de orquestación, verificación de dependencias, migración y configuración inicial por ti.

### Requisitos Previos
Tener instalados **Docker** y **Docker Compose** en tu servidor.

### Pasos para Arrancar el Sistema:

1. **Clona el repositorio** en tu servidor VPS:
   ```bash
   git clone https://github.com/xXPakoGamer10Xx/Departamentos.git
   cd Departamentos
   ```

2. **Ejecuta el script de inicio en producción**:
   ```bash
   bash start.sh
   ```

### ¿Qué hace `start.sh` por ti de manera automática?
- **Comprobación de Herramientas**: Verifica que Docker y Docker Compose estén activos en el host.
- **Gestión de Variables**: Si no existe el archivo `.env` en la raíz, lo genera automáticamente a partir de la plantilla y solicita los ajustes necesarios de producción de manera segura.
- **Aislamiento de Puertos (Seguridad)**: Configura la base de datos PostgreSQL en una red interna privada. El puerto `5432` no se expone a internet, evitando ataques externos de fuerza bruta.
- **Construcción y Despliegue**: Levanta los contenedores en segundo plano (`db` y `backend`).
- **Migraciones e Inicialización**: Ejecuta las migraciones de base de datos para estructurar las tablas e inserta los datos semilla iniciales (`seed`) sin necesidad de configuraciones manuales.

---

## 💻 Configuración para Desarrollo Local

Para trabajar localmente sin interferir con producción, el sistema cuenta con el script autogestionado `dev.sh`.

### Pasos para iniciar en local:

1. Asegúrate de tener instalados el **SDK de .NET 10** (backend) y **Node.js** (web) en tu máquina local.
2. Inicia el entorno de desarrollo ejecutando:
   ```bash
   bash dev.sh
   ```

### ¿Qué realiza `dev.sh`?
- Levanta únicamente la base de datos PostgreSQL local dentro de un contenedor Docker para no saturar de dependencias locales tu sistema.
- Inicia el servidor backend en modo "Hot Reload" mediante `dotnet watch` en el puerto `3001`.
- Aplica automáticamente las migraciones incrementales (idempotentes) al arrancar.
- Lanza Vite (`http://localhost:5173`, también en tu red local para probar desde el celular) con un proxy `/api → :3001`.

---

## 🔐 Variables de Entorno

El archivo `.env` se ubica en la raíz del proyecto. A continuación se listan las variables necesarias:

| Variable | Tipo / Valor por defecto | Descripción |
|----------|--------------------------|-------------|
| `NODE_ENV` | `development` \| `production` | Entorno de ejecución (el backend .NET sigue leyendo esta variable; también acepta `ASPNETCORE_ENVIRONMENT=Production`). |
| `PORT` | `3001` | Puerto en el que escucha el servidor backend. |
| `TZ` | `America/Mexico_City` | Zona horaria del servidor para consistencia en fechas de pago. |
| `DB_HOST` | `db` | Host de conexión a la base de datos. |
| `DB_PORT` | `5432` | Puerto interno de PostgreSQL. |
| `DB_NAME` | `departamentos` | Nombre de la base de datos. |
| `DB_USER` | `depas_user` | Nombre de usuario de la base de datos. |
| `DB_PASSWORD` | *Generado en arranque* | Contraseña de conexión a PostgreSQL. |
| `JWT_SECRET` | *Generado en arranque* | Secreto criptográfico de firma para tokens JWT de sesión. |
| `JWT_EXPIRES_IN` | `7d` | Duración de validez de la sesión del usuario. |
| `BACKUP_CRON` | `0 4 * * *` | Formato cron de ejecución automática para respaldos diarios. |
| `SOFFICE_PATH` | *Autodetectado* | Ruta de LibreOffice (`soffice`), que convierte el Word del contrato a PDF. En Docker ya viene instalado; en desarrollo se detecta en `/opt/homebrew/bin`, `/usr/bin` o `/Applications/LibreOffice.app` (`brew install --cask libreoffice`). Sin LibreOffice el PDF sale aproximado (sin tablas ni imágenes). |

---

## 👥 Credenciales de Prueba (Seed)

Al iniciar el sistema por primera vez, el cargador de datos iniciales inserta los siguientes usuarios de pruebas:

### 👤 Administrador (Arrendador)
- **Correo Electrónico**: `admin@departamentos.local`
- **Contraseña**: `Admin2024!`

### 🏠 Inquilino de Prueba (Depto 1)
- **Nombre**: Valeria Irene Ortiz
- **Correo Electrónico**: `inquilino@departamentos.local`
- **Contraseña**: `Inquilino2024!`

> [!WARNING]
> Recuerda cambiar estas contraseñas en tu primer inicio de sesión para asegurar el control de tu información.

---

## 💾 Copias de Seguridad (Backups)

El sistema incluye una tarea programada interna (`cron`) que crea respaldos automáticos de la base de datos de manera periódica.

- **Frecuencia**: Todos los días a las **4:00 AM (Hora de la CDMX)**.
- **Ubicación**: Carpeta `./backups/` en la raíz del proyecto.
- **Retención**: Los backups antiguos se eliminan automáticamente tras 30 días para optimizar espacio.

### Ejecución de Copia Manual
Puedes generar copias de seguridad de forma instantánea de dos maneras:
1. **Desde la App**: Menú `Configuración` ➡️ `Backups` ➡️ `Crear copia ahora`.
2. **Vía API REST**:
   ```bash
   curl -X POST http://localhost:3001/api/backups/manual \
     -H "Authorization: Bearer TU_TOKEN_DE_SESION"
   ```

---

## 🔌 API Endpoints

A continuación se detallan los endpoints principales expuestos por el backend:

### 🔑 Autenticación y Cuentas
- `POST /api/auth/login` - Iniciar sesión y obtener token JWT.
- `GET /api/auth/me` - Obtener información del usuario en sesión.
- `GET /api/usuarios` - Listar cuentas registradas en el sistema.

### 👤 Inquilinos y Departamentos
- `GET /api/inquilinos` - Listar inquilinos con soporte de búsqueda y paginación.
- `POST /api/inquilinos` - Crear un nuevo registro de inquilino.
- `PUT /api/inquilinos/:id` - Actualizar información detallada del inquilino.
- `DELETE /api/inquilinos/:id` - Dar de baja un inquilino del sistema.
- `GET /api/departamentos` - Consultar inventario y estado de departamentos.
- `GET /api/departamentos/stats` - Obtener métricas rápidas de ocupación e ingresos.

### 💳 Finanzas, Pagos e Incidencias
- `GET /api/pagos` - Listar historial completo de transacciones realizadas.
- `POST /api/pagos` - Registrar un nuevo cobro o pago de mensualidad.
- `GET /api/cuotas` - Listar cargos extraordinarios y servicios adicionales.
- `POST /api/cuotas` - Asignar una nueva cuota a un inquilino.
- `GET /api/tickets` - Listar reportes de mantenimiento activos y cerrados.
- `POST /api/tickets` - Crear reporte de incidencia (por inquilino o admin).

### 🔔 Notificaciones, Sistema y Copias de Seguridad
- `GET /api/events` - Canal en tiempo real (Server-Sent Events) para recibir notificaciones inmediatas.
- `GET /api/notificaciones` - Historial de notificaciones del usuario logueado.
- `POST /api/push/register` - Guardar token para notificaciones push en móviles.
- `GET /api/backups` - Listar archivos de respaldo disponibles en disco.
- `POST /api/backups/manual` - Forzar la generación de una copia de seguridad.
- `GET /api/config` - Consultar variables editables del arrendador.
- `PUT /api/config` - Modificar datos de contacto y datos fiscales del arrendador.

---

Desarrollado con dedicación y enfoque profesional para simplificar la gestión diaria de departamentos. 🏢
