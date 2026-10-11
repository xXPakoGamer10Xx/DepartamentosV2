/// <reference lib="webworker" />
import { cleanupOutdatedCaches, createHandlerBoundToURL, precacheAndRoute } from 'workbox-precaching'
import { NavigationRoute, registerRoute } from 'workbox-routing'

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<string | { url: string; revision: string | null }> }

// Precache del shell de la app (instalable y rápida en el celular). La API nunca se cachea.
precacheAndRoute(self.__WB_MANIFEST)
cleanupOutdatedCaches()
registerRoute(new NavigationRoute(createHandlerBoundToURL('/index.html'), { denylist: [/^\/api\//] }))

self.addEventListener('install', () => { void self.skipWaiting() })
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))

// ── Web Push ────────────────────────────────────────────────────────────────
self.addEventListener('push', (event) => {
  let payload: { title?: string; body?: string; data?: unknown } = {}
  try { payload = event.data ? event.data.json() : {} } catch { /* sin payload JSON */ }
  event.waitUntil(
    self.registration.showNotification(payload.title || 'NethRent', {
      body: payload.body || '',
      icon: '/icons/icon-192x192.png',
      badge: '/icons/favicon-32x32.png',
      data: payload.data || {},
    }),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((list) => {
      for (const client of list) if ('focus' in client) return client.focus()
      return self.clients.openWindow('/')
    }),
  )
})
