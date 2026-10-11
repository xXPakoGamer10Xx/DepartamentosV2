import { api } from './api'

const VAPID = import.meta.env.VITE_VAPID_PUBLIC_KEY as string | undefined

function b64ToBytes(b64: string): Uint8Array<ArrayBuffer> {
  const padded = (b64 + '='.repeat((4 - (b64.length % 4)) % 4)).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(padded)
  const out = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i)
  return out
}

export const pushSupported = () =>
  typeof window !== 'undefined' && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window && !!VAPID

async function subscribeAndSend(): Promise<boolean> {
  const reg = (await navigator.serviceWorker.getRegistration()) ??
    (await Promise.race([
      navigator.serviceWorker.ready,
      new Promise<undefined>(r => setTimeout(() => r(undefined), 2500)),
    ]))
  if (!reg) return Notification.permission === 'granted'
  const sub = (await reg.pushManager.getSubscription()) ??
    (await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: b64ToBytes(VAPID!) }))
  const j = sub.toJSON() as { endpoint?: string; keys?: { p256dh?: string; auth?: string } }
  if (!j.endpoint || !j.keys?.p256dh || !j.keys?.auth) return false
  await api.subscribeWebPush({ endpoint: j.endpoint, keys: { p256dh: j.keys.p256dh, auth: j.keys.auth } })
  return true
}

/** Al abrir la app: restaura la suscripción SOLO si el permiso ya estaba concedido (nunca pide permiso). */
export async function resubscribeIfGranted() {
  if (!pushSupported() || Notification.permission !== 'granted') return
  try { await subscribeAndSend() } catch { /* sin push: SSE cubre con la app abierta */ }
}

/** Llamar solo desde un gesto del usuario (click): Chrome puede bloquear para siempre un permiso pedido sin gesto. */
export async function activatePush(): Promise<boolean> {
  if (!pushSupported()) return false
  try {
    if (Notification.permission === 'default') await Notification.requestPermission()
    if (Notification.permission !== 'granted') return false
    return await subscribeAndSend()
  } catch { return false }
}
