import { useEffect, useRef } from 'react'
import { API_URL } from './api'

// Eventos en vivo del backend (Server-Sent Events). Una sola conexión compartida por toda la app.
export type SseEvent = 'ticket_new' | 'ticket_updated' | 'payment_confirmed' | 'payment_rejected' | 'comprobante_subido' | 'notification_new'
const EVENTS: SseEvent[] = ['ticket_new', 'ticket_updated', 'payment_confirmed', 'payment_rejected', 'comprobante_subido', 'notification_new']

type Listener = (data: any) => void // eslint-disable-line @typescript-eslint/no-explicit-any
const listeners = new Map<string, Set<Listener>>()
let es: EventSource | null = null
let retry: ReturnType<typeof setTimeout> | null = null
let currentToken = ''

function open() {
  const base = API_URL.endsWith('/api') ? API_URL : `${API_URL}`
  es = new EventSource(`${base}/events?token=${encodeURIComponent(currentToken)}`)
  es.onopen = () => { if (retry) { clearTimeout(retry); retry = null } }
  es.onerror = () => {
    es?.close(); es = null
    retry = setTimeout(open, 4000)
  }
  for (const evt of EVENTS) {
    es.addEventListener(evt, (e) => {
      try { const data = JSON.parse((e as MessageEvent).data); listeners.get(evt)?.forEach(cb => cb(data)) } catch { /* payload inválido */ }
    })
  }
}

export function connectSSE(token: string) {
  if (es) return
  currentToken = token
  open()
}

export function disconnectSSE() {
  if (retry) { clearTimeout(retry); retry = null }
  es?.close(); es = null
}

// Cerrar el stream al descargar la página: con HTTP/1.1 el navegador permite ~6 conexiones por host y cada recarga
// dejaba una abierta, hasta bloquear todas las peticiones. Si la página vuelve del caché (bfcache), se reconecta.
if (typeof window !== 'undefined') {
  window.addEventListener('pagehide', disconnectSSE)
  window.addEventListener('pageshow', (e) => { if (e.persisted && currentToken && !es) open() })
}

export function useSSE(event: SseEvent, callback: Listener) {
  const ref = useRef(callback)
  useEffect(() => { ref.current = callback })
  useEffect(() => {
    if (!listeners.has(event)) listeners.set(event, new Set())
    const cb: Listener = (d) => ref.current(d)
    listeners.get(event)!.add(cb)
    return () => { listeners.get(event)?.delete(cb) }
  }, [event])
}
