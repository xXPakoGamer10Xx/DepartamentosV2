import { useMutation, useQuery } from '@tanstack/react-query'
import { Banknote, Building2, CalendarDays, User, XCircle } from 'lucide-react'
import { motion } from 'motion/react'
import type { ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { AnimatedNumber } from '../components/ui/AnimatedNumber'
import { Button } from '../components/ui/Button'
import { ConfettiBurst, SuccessCheck } from '../components/ui/Celebration'
import { ShakeError } from '../components/ui/MotionPrimitives'
import { Spinner } from '../components/ui/Spinner'
import { api } from '../lib/api'
import { money, num } from '../lib/format'

interface PagoInfo { nombre_completo?: string; depto_numero?: number; periodo?: string; monto?: string; renta?: string }

/** Página pública a la que lleva el QR de pago en efectivo: el administrador/cobrador la abre al escanear. */
export default function ConfirmarPago() {
  const { token = '' } = useParams()

  const info = useQuery({ queryKey: ['pago-token', token], queryFn: async () => (await api.getPagoByToken(token)).data, retry: false })
  const confirmar = useMutation({ mutationFn: () => api.confirmarPago(token) })

  // El servidor añade nombre y depto a la respuesta de confirmar (no están en el tipo base de Pago).
  const pago = (confirmar.data?.data ?? info.data) as PagoInfo | undefined
  const yaConfirmado = !!info.data?.confirmado || !!confirmar.data?.yaConfirmado
  const done = confirmar.isSuccess || yaConfirmado

  return (
    <div className="grid min-h-full place-items-center bg-gradient-to-br from-[#090A0F] via-[#181528] to-[#07080D] p-5 text-white">
      <ConfettiBurst active={confirmar.isSuccess} />
      <motion.div
        initial={{ opacity: 0, y: 20, scale: 0.96 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ type: 'spring', stiffness: 320, damping: 26 }}
        className="w-full max-w-md rounded-3xl border border-white/10 bg-white/[0.06] p-8 text-center shadow-2xl backdrop-blur-xl sm:p-10"
      >
        {info.isLoading ? (
          <Spinner className="mx-auto size-8" />
        ) : info.isError ? (
          <>
            <Icono tone="danger"><XCircle className="size-12" /></Icono>
            <h1 className="text-2xl font-extrabold">QR inválido</h1>
            <p className="mt-2 text-white/60">{info.error instanceof Error ? info.error.message : 'QR inválido o expirado'}</p>
          </>
        ) : (
          <>
            {done ? <SuccessCheck size={80} /> : <Icono tone="primary"><Banknote className="size-12" /></Icono>}
            <h1 className="text-2xl font-extrabold">
              {done ? (yaConfirmado && !confirmar.isSuccess ? '¡Ya estaba pagado!' : '¡Pago confirmado!') : 'Confirmar pago en efectivo'}
            </h1>

            <dl className="mt-6 space-y-3 text-left">
              <Fila icon={<User className="size-4" />}>{pago?.nombre_completo}</Fila>
              <Fila icon={<Building2 className="size-4" />}>Depto {pago?.depto_numero}</Fila>
              <Fila icon={<CalendarDays className="size-4" />}>{pago?.periodo}</Fila>
              <div className="flex items-center justify-between rounded-2xl bg-white/10 px-4 py-3">
                <span className="text-sm text-white/60">Monto</span>
                <span className="text-2xl font-extrabold">
                  <AnimatedNumber value={num(pago?.monto ?? pago?.renta)} format={money} />
                </span>
              </div>
            </dl>

            {!done && (
              <>
                <ShakeError error={confirmar.isError ? (confirmar.error instanceof Error ? confirmar.error.message : 'Error') : null}>
                  <p role="alert" className="mt-4 rounded-xl bg-red-500/15 px-4 py-3 text-sm text-red-300">{confirmar.error instanceof Error ? confirmar.error.message : 'Error al confirmar'}</p>
                </ShakeError>
                <Button full className="mt-6 h-12 text-base" onClick={() => confirmar.mutate()} loading={confirmar.isPending}>Confirmar pago</Button>
              </>
            )}
          </>
        )}
      </motion.div>
    </div>
  )
}

function Icono({ tone, children }: { tone: 'danger' | 'success' | 'primary'; children: ReactNode }) {
  const c = { danger: 'bg-red-500/15 text-red-400', success: 'bg-emerald-500/15 text-emerald-400', primary: 'bg-blue-500/15 text-blue-400' }[tone]
  return (
    <motion.span
      initial={{ scale: 0.7, opacity: 0 }}
      animate={{ scale: 1, opacity: 1 }}
      transition={{ type: 'spring', stiffness: 320, damping: 20 }}
      className={`mx-auto mb-5 grid size-20 place-items-center rounded-full ${c}`}
    >
      {children}
    </motion.span>
  )
}

function Fila({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return <div className="flex items-center gap-3 text-white/85"><span className="text-white/50">{icon}</span>{children}</div>
}

