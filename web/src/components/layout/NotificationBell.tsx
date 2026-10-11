import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell, BellOff, CheckCheck, CreditCard, FileText, MessageSquare, Wallet } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useEffect, useRef, useState } from 'react'
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
  const [open, setOpen] = useState(false)
  const [ringing, setRinging] = useState(false)
  const [perm, setPerm] = useState(typeof Notification !== 'undefined' ? Notification.permission : 'default')
  const [activating, setActivating] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

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
    if (!open) return
    const onDown = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  const refresh = () => void qc.invalidateQueries({ queryKey: ['notificaciones'] })
  const markAll = async () => {
    qc.setQueryData<Notificacion[]>(['notificaciones'], (old) => old?.map(n => ({ ...n, leido: true })))
    await api.marcarTodasLeidas().catch(refresh)
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
    <div ref={ref} className="relative">
      <button
        onClick={() => {
          if (typeof Notification !== 'undefined') setPerm(Notification.permission)
          setOpen(o => !o)
        }}
        aria-label="Notificaciones"
        title="Notificaciones"
        className={
          subtle
            ? `relative grid place-items-center rounded-lg p-2 transition active:scale-95 ${open ? 'bg-hover text-fg' : 'text-muted hover:bg-hover hover:text-fg'}`
            : `relative grid size-9 place-items-center rounded-xl transition active:scale-95 ${open ? 'bg-hover text-fg' : 'text-fg-2 hover:bg-hover hover:text-fg'}`
        }
      >
        <Bell className={`${subtle ? 'size-[18px]' : 'size-5'} ${ringing ? 'animate-bell-ring text-primary' : ''}`} />
        {unread > 0 && (
          <span className="absolute -right-0.5 -top-0.5 grid min-w-[16px] place-items-center rounded-full bg-danger px-1 text-[10px] font-bold leading-[16px] text-white shadow-sm">
            <span className="animate-pulse-ring absolute inset-0 rounded-full bg-danger" />
            <span className="relative">{unread > 9 ? '9+' : unread}</span>
          </span>
        )}
      </button>

      <AnimatePresence>
        {open && (
          <motion.div
            initial={{ opacity: 0, y: direction === 'up' ? 8 : -8, scale: 0.96 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: direction === 'up' ? 6 : -6, scale: 0.96 }}
            transition={{ type: 'spring', stiffness: 420, damping: 30 }}
            className={`absolute z-50 w-[min(92vw,22rem)] overflow-hidden rounded-2xl border border-line bg-card shadow-2xl shadow-black/30 ${direction === 'up' ? 'bottom-full mb-3' : 'top-11'} ${align === 'right' ? 'right-0' : '-left-3'}`}
          >
            <div className="flex items-center justify-between border-b border-line px-4 py-3">
              <h3 className="text-sm font-bold">Notificaciones</h3>
              {unread > 0 && (
                <button onClick={markAll} className="flex items-center gap-1 text-xs font-semibold text-primary hover:underline">
                  <CheckCheck className="size-3.5" /> Marcar leídas
                </button>
              )}
            </div>
            {showPushBanner && (
              <div className="space-y-2.5 border-b border-line bg-elevated/50 px-4 py-3.5">
                <div className="space-y-1">
                  <h4 className="flex items-center gap-2 text-xs font-bold text-fg">
                    <BellOff className="size-3.5 shrink-0 text-fg-2" />
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
                    <Button className="h-9 px-3.5 text-xs" loading={activating} onClick={activarPush}>
                      Activar notificaciones
                    </Button>
                  </div>
                )}
              </div>
            )}
            <ul className="max-h-[60vh] divide-y divide-line overflow-y-auto">
              {items.length === 0 && <li className="px-4 py-10 text-center text-sm text-muted">Sin notificaciones</li>}
              {items.map((n, idx) => {
                const Icon = iconos[n.tipo] ?? Bell
                return (
                  <motion.li
                    key={n.id}
                    initial={{ opacity: 0, x: -8 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ delay: Math.min(idx * 0.03, 0.2) }}
                  >
                    <button onClick={() => markOne(n)} className={`flex w-full gap-3 px-4 py-3 text-left transition hover:bg-hover ${n.leido ? 'opacity-60' : ''}`}>
                      <span className="mt-0.5 grid size-8 shrink-0 place-items-center rounded-lg bg-primary-soft text-primary"><Icon className="size-4" /></span>
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-semibold">{n.titulo}</span>
                        <span className="line-clamp-2 text-xs text-fg-2">{n.mensaje}</span>
                        <span className="mt-0.5 block text-[11px] text-muted">{haceCuanto(n.created_at)}</span>
                      </span>
                      {!n.leido && <span className="mt-2 size-2 shrink-0 rounded-full bg-primary" />}
                    </button>
                  </motion.li>
                )
              })}
            </ul>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

