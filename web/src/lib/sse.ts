import { useEffect, useRef } from 'react'
import { API_URL, ApiError, api } from './api'

// Eventos en vivo del backend (Server-Sent Events). Una sola conexión compartida por toda la app.
export type SseEvent = 'ticket_new' | 'ticket_updated' | 'payment_confirmed' | 'payment_rejected' | 'comprobante_subido' | 'notification_new'
const EVENTS: SseEvent[] = ['ticket_new', 'ticket_updated', 'payment_confirmed', 'payment_rejected', 'comprobante_subido', 'notification_new']

type Listener = (data: any) => void // eslint-disable-line @typescript-eslint/no-explicit-any
const listeners = new Map<string, Set<Listener>>()
let es: EventSource | null = null
let retry: ReturnType<typeof setTimeout> | null = null
let wanted = false // true mientras la app quiere la conexión abierta (hay sesión)

async function open() {
  retry = null
  if (!wanted || es) return
  let ticket: string
  try { ticket = (await api.sseTicket()).data.ticket }
  catch (e) {
    // 401: la sesión ya no vale (api.ts cierra la sesión); no se reintenta. Otro fallo: se reintenta.
    if (!(e instanceof ApiError && e.status === 401) && wanted) retry = setTimeout(open, 4000)
    return
  }
  if (!wanted || es) return
  const source = new EventSource(`${API_URL}/events?ticket=${encodeURIComponent(ticket)}`)
  es = source
  source.onerror = () => {
    // El boleto vale una conexión: al caerse se pide otro.
    source.close()
    if (es === source) es = null
    if (wanted && !retry) retry = setTimeout(open, 4000)
  }
  for (const evt of EVENTS) {
    source.addEventListener(evt, (e) => {
      try { const data = JSON.parse((e as MessageEvent).data); listeners.get(evt)?.forEach(cb => cb(data)) } catch { /* payload inválido */ }
    })
  }
}

export function connectSSE() {
  wanted = true
  void open()
}

export function disconnectSSE() {
  wanted = false
  if (retry) { clearTimeout(retry); retry = null }
  es?.close(); es = null
}

// Cerrar el stream al descargar la página: con HTTP/1.1 el navegador permite ~6 conexiones por host y cada recarga
// dejaba una abierta, hasta bloquear todas las peticiones. Si la página vuelve del caché (bfcache), se reconecta.
if (typeof window !== 'undefined') {
  window.addEventListener('pagehide', () => { if (es) { es.close(); es = null } })
  window.addEventListener('pageshow', (e) => { if (e.persisted && wanted && !es) void open() })
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
