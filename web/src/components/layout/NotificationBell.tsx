import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell, BellOff, CheckCheck, CreditCard, FileText, MessageSquare, Trash2, Wallet } from 'lucide-react'
import { motion } from 'motion/react'
import { useEffect, useId, useRef, useState, type CSSProperties } from 'react'
import { api } from '../../lib/api'
import { haceCuanto } from '../../lib/format'
import { activatePush, pushSupported } from '../../lib/push'
import { useSSE } from '../../lib/sse'
import type { Notificacion } from '../../lib/types'
import { Button } from '../ui/Button'
import { useToast } from '../ui/Toast'

const iconos = { renta: Wallet, cuota: FileText, ticket: MessageSquare, pago: CreditCard, promesa: CreditCard } as const

export function NotificationBell({
  align = 'left',
  direction = 'down',
  subtle = false,
}: {
  align?: 'left' | 'right'
  direction?: 'up' | 'down'
  subtle?: boolean
}) {
  const qc = useQueryClient()
  const toast = useToast()
  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, '')
  const popoverId = `notif-panel-${uid}`
  const anchorName = `--notif-anchor-${uid}`
  const popoverRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [ringing, setRinging] = useState(false)
  const [perm, setPerm] = useState(typeof Notification !== 'undefined' ? Notification.permission : 'default')
  const [activating, setActivating] = useState(false)

  const { data: items = [] } = useQuery({
    queryKey: ['notificaciones'],
    queryFn: async () => (await api.getNotificaciones()).data,
    refetchInterval: 60_000,
  })
  const unread = items.filter(n => !n.leido).length

  // Eventos en vivo → refresca la lista al instante y agita la campana.
  const triggerLive = () => {
    void qc.invalidateQueries({ queryKey: ['notificaciones'] })
    setRinging(true)
    setTimeout(() => setRinging(false), 850)
  }
  useSSE('notification_new', triggerLive)
  useSSE('payment_confirmed', triggerLive)
  useSSE('ticket_new', triggerLive)

  useEffect(() => {
    const el = popoverRef.current
    if (!el) return
    const onToggle = (e: Event) => {
      const te = e as ToggleEvent
      const isOpen = te.newState === 'open'
      setOpen(isOpen)
      if (isOpen && typeof Notification !== 'undefined') setPerm(Notification.permission)
    }
    el.addEventListener('toggle', onToggle)
    return () => el.removeEventListener('toggle', onToggle)
  }, [])

  const refresh = () => void qc.invalidateQueries({ queryKey: ['notificaciones'] })
  const markAll = async () => {
    qc.setQueryData<Notificacion[]>(['notificaciones'], (old) => old?.map(n => ({ ...n, leido: true })))
    await api.marcarTodasLeidas().catch(refresh)
  }
  const borrarUna = async (n: Notificacion) => {
    qc.setQueryData<Notificacion[]>(['notificaciones'], (old) => old?.filter(x => x.id !== n.id))
    await api.borrarNotificacion(n.id).catch(refresh)
  }
  const borrarLeidas = async () => {
    qc.setQueryData<Notificacion[]>(['notificaciones'], (old) => old?.filter(x => !x.leido))
    await api.borrarLeidas().catch(refresh)
  }
  const markOne = async (n: Notificacion) => {
    if (n.leido) return
    qc.setQueryData<Notificacion[]>(['notificaciones'], (old) => old?.map(x => x.id === n.id ? { ...x, leido: true } : x))
    await api.marcarLeida(n.id).catch(refresh)
  }

  const activarPush = async () => {
    setActivating(true)
    const ok = await activatePush()
    const nextPerm = typeof Notification !== 'undefined' ? Notification.permission : 'default'
    setPerm(nextPerm)
    setActivating(false)
    const activadas = ok || nextPerm === 'granted'
    toast(
      activadas ? 'Notificaciones activadas en este dispositivo' : 'No se pudieron activar las notificaciones',
      activadas ? 'success' : 'error',
    )
  }

  const showPushBanner = pushSupported() && perm !== 'granted'

  return (
    <div className="relative">
      <button
        type="button"
        popoverTarget={popoverId}
        aria-label={unread > 0 ? `Notificaciones (${unread} sin leer)` : 'Notificaciones'}
        aria-expanded={open}
        title="Notificaciones"
        style={{ anchorName } as CSSProperties}
        className={
          subtle
            ? `relative grid place-items-center rounded-lg p-2 transition active:scale-95 ${open ? 'bg-hover text-fg' : 'text-muted hover:bg-hover hover:text-fg'}`
            : `relative grid size-9 place-items-center rounded-xl transition active:scale-95 ${open ? 'bg-hover text-fg' : 'text-fg-2 hover:bg-hover hover:text-fg'}`
        }
      >
        <Bell aria-hidden="true" className={`${subtle ? 'size-[18px]' : 'size-5'} ${ringing ? 'animate-bell-ring text-primary' : ''}`} />
        {unread > 0 && (
          <span aria-hidden="true" className="absolute -right-0.5 -top-0.5 grid min-w-[16px] place-items-center rounded-full bg-danger px-1 text-[10px] font-bold leading-[16px] text-white shadow-sm">
            <span className="animate-pulse-ring absolute inset-0 rounded-full bg-danger" />
            <span className="relative">{unread > 9 ? '9+' : unread}</span>
          </span>
        )}
      </button>

      <div
        ref={popoverRef}
        id={popoverId}
        {...({ popover: 'auto' } as Record<string, string>)}
        data-align={align}
        data-direction={direction}
        style={{ positionAnchor: anchorName } as CSSProperties}
        className="anchored-popover w-[min(92vw,22rem)]"
      >
        <div className="overflow-hidden rounded-2xl border border-line bg-card shadow-2xl shadow-black/30">
          <div className="flex items-center justify-between border-b border-line px-4 py-3">
            <h3 className="text-sm font-bold">Notificaciones</h3>
            <div className="flex items-center gap-3">
              {unread > 0 && (
                <button type="button" onClick={markAll} className="flex items-center gap-1 text-xs font-semibold text-primary hover:underline">
                  <CheckCheck className="size-3.5" aria-hidden="true" /> Marcar leídas
                </button>
              )}
              {items.some(n => n.leido) && (
                <button type="button" onClick={borrarLeidas} className="flex items-center gap-1 text-xs font-semibold text-danger hover:underline">
                  <Trash2 className="size-3.5" aria-hidden="true" /> Borrar leídas
                </button>
              )}
            </div>
          </div>
          {showPushBanner && (
            <div className="space-y-2.5 border-b border-line bg-elevated/50 px-4 py-3.5">
              <div className="space-y-1">
                <h4 className="flex items-center gap-2 text-xs font-bold text-fg">
                  <BellOff className="size-3.5 shrink-0 text-fg-2" aria-hidden="true" />
                  Notificaciones del navegador
                </h4>
                <p className="text-xs leading-relaxed text-fg-2">
                  {perm === 'denied'
                    ? 'Bloqueadas por el navegador. Habilítalas en los ajustes del sitio.'
                    : 'Actívalas para recibir avisos de pagos y tickets aunque la app esté cerrada.'}
                </p>
              </div>
              {perm !== 'denied' && (
                <div className="flex justify-end pt-0.5">
                  <Button type="button" className="min-h-9 px-3.5 text-xs" loading={activating} onClick={activarPush}>
                    Activar notificaciones
                  </Button>
                </div>
              )}
            </div>
          )}
          <ul className="custom-scrollbar max-h-[60dvh] divide-y divide-line overflow-y-auto">
            {items.length === 0 && <li className="px-4 py-10 text-center text-sm text-muted">Sin notificaciones</li>}
            {items.map((n, idx) => {
              const Icon = iconos[n.tipo] ?? Bell
              return (
                <motion.li
                  key={n.id} className="relative"
                  initial={{ opacity: 0, x: -8 }}
                  animate={{ opacity: 1, x: 0 }}
                  transition={{ delay: Math.min(idx * 0.03, 0.2) }}
                >
                  <button type="button" onClick={() => markOne(n)} className={`flex w-full gap-3 px-4 py-3 pr-11 text-left transition hover:bg-hover ${n.leido ? 'opacity-60' : ''}`}>
                    <span className="mt-0.5 grid size-8 shrink-0 place-items-center rounded-lg bg-primary-soft text-primary"><Icon className="size-4" aria-hidden="true" /></span>
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-semibold">{n.titulo}</span>
                      <span className="line-clamp-2 text-xs text-fg-2">{n.mensaje}</span>
                      <span className="mt-0.5 block text-[11px] text-muted">{haceCuanto(n.created_at)}</span>
                    </span>
                    {!n.leido && <span aria-hidden="true" className="mt-2 size-2 shrink-0 rounded-full bg-primary" />}
                  </button>
                  <button type="button" aria-label={`Borrar notificación: ${n.titulo}`} onClick={() => borrarUna(n)}
                    className="absolute right-2 top-2 grid size-8 place-items-center rounded-lg text-muted transition hover:bg-danger-soft hover:text-danger">
                    <Trash2 className="size-4" aria-hidden="true" />
                  </button>
                </motion.li>
              )
            })}
          </ul>
        </div>
      </div>
    </div>
  )
}
