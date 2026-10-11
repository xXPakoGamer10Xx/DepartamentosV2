# NethRent Web (React + Vite + PWA)

Reemplazo del frontend Expo (`../frontend`). Mismo API (`/api`), misma sesión (`auth_token`/`auth_user` en localStorage).

## Desarrollo
```bash
# terminal 1 — backend .NET (puerto 3001)
cd backend/VertexRent.Api && dotnet run
# terminal 2 — web (puerto 5173, proxy /api → :3001)
cd web && npm install && npm run dev
```

## Producción
`npm run build` genera `dist/` (incluye `sw.js`, manifest e iconos). El backend lo sirve desde `public/`; el `Dockerfile` (backend/Dockerfile) ya compila la web y la copia ahí.

## Estado de la migración
| Fase | Alcance | Estado |
|---|---|---|
| 1 | Base: login/registro, sesión, layout por rol, notificaciones + SSE, PWA/push | ✅ |
| 2 | Inquilino: inicio con pagos (transferencia + comprobante, efectivo con QR), historial, tickets, confirmación pública `/confirmar/:token` | ✅ |
| 3 | Admin núcleo: inquilinos (lista, detalle, alta/edición con INE-IA y depósito diferido, baja, nuevo contrato), departamentos (inventario, mantenimiento, cuenta asignada), cuentas bancarias | ✅ |
| 4 | Admin cobranza: lista de pagos, cobro por inquilino (abonos, cargos extra, promesas, depósito, recibo), comprobantes, reportes | ✅ |
| 5 | Escáner QR (cámara + jsQR), tickets del admin, configuración (general, usuarios y permisos, invitaciones, respaldos) | ✅ |
| 5b | **Contratos**: plantilla del sistema, subir tu Word (IA detecta los datos), revisión con marcas, varias plantillas, generar PDF + Word con historial | ✅ |
| 6 | Colaboradores (permisos), Dockerfile/dev.sh, retiro del Expo (`frontend-expo-old/`) | ✅ |

## Estructura
`src/lib` (api tipado, sesión, SSE, push, permisos) · `src/components/{ui,layout}` · `src/pages` · `src/sw.ts` (service worker: precache + push).

