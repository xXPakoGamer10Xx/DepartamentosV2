import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, CalendarDays, CheckCircle2, Circle, Clock, CloudUpload, Home as HomeIcon, Info, MessageSquarePlus, QrCode, Receipt, RefreshCw, Send } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import QRCode from 'qrcode'
import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'

import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { BankCard3D } from '../../components/ui/BankCard3D'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfettiBurst } from '../../components/ui/Celebration'
import { ProgressBar } from '../../components/ui/ProgressBar'
import { Spinner } from '../../components/ui/Spinner'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { fechaHora, money, num, periodoLabel } from '../../lib/format'
import { compressImage, pickImage } from '../../lib/image'
import { useSSE } from '../../lib/sse'
import type { Config, Inquilino } from '../../lib/types'

export default function InquilinoHome() {
  const { user } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const [celebrate, setCelebrate] = useState(false)

  const miDepto = useQuery({ queryKey: ['mi-depto'], queryFn: async () => (await api.getMiDepto()).data })
  const inq = miDepto.data ?? null

  const config = useQuery({ queryKey: ['config'], queryFn: async () => (await api.getConfig()).data, enabled: !!inq })
  const estado = useQuery({
    queryKey: ['estado-pago', inq?.id],
    queryFn: () => api.getEstadoPago(inq!.id),
    enabled: !!inq?.id,
    refetchInterval: 15_000, // respaldo; los cambios llegan al instante por SSE
  })

  const refresh = () => void qc.invalidateQueries({ queryKey: ['estado-pago'] })
  useSSE('payment_confirmed', () => {
    refresh()
    setCelebrate(true)
    setTimeout(() => setCelebrate(false), 2600)
    toast('¡Pago confirmado! ✅', 'success')
  })
  useSSE('payment_rejected', (d) => {
    refresh()
    toast(d?.pago?.comentario_admin ? `Comprobante rechazado. Razón: ${d.pago.comentario_admin}` : 'Comprobante rechazado. Envía uno nuevo.', 'error')
  })

  if (miDepto.isLoading) return <div className="grid place-items-center py-24"><Spinner className="size-8" /></div>

  const header = (
    <div>
      <p className="text-sm text-fg-2">Bienvenido,</p>
      <h1 className="text-2xl font-extrabold tracking-tight">{user?.nombre_completo}</h1>
    </div>
  )

  if (!inq) {
    return (
      <div className="space-y-6">
        {header}
        <Card className="mx-auto max-w-lg py-12 text-center">
          <span className="mx-auto mb-4 grid size-16 place-items-center rounded-2xl bg-warning-soft text-warning"><Clock className="size-8" /></span>
          <h2 className="text-lg font-bold">Cuenta pendiente de activación</h2>
          <p className="mt-2 text-sm text-fg-2">Tu cuenta está siendo configurada por el administrador. En cuanto sea vinculada a tu departamento podrás ver aquí tu información de pago.</p>
        </Card>
      </div>
    )
  }

  const pago = estado.data?.data ?? null
  const cuotas = (estado.data?.cuotas ?? []).filter(c => c.estado === 'pendiente')
  const totalExtra = estado.data?.total_extra ?? 0
  const totalAbonado = estado.data?.total_abonado ?? 0
  const saldo = estado.data?.saldo_pendiente ?? 0
  const cfg = config.data ?? {}
  const esTransferencia = inq.metodo_pago === 'transferencia' || inq.metodo_pago === 'ambos'
  const esEfectivo = inq.metodo_pago === 'efectivo' || inq.metodo_pago === 'ambos'
  const periodo = estado.data?.periodo

  return (
    <div className="space-y-6">
      <ConfettiBurst active={celebrate} />
      {header}
      <div className="grid gap-5 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className="space-y-5">
          {/* Departamento y renta */}
          <Card className="flex items-center gap-4 transition duration-200 hover:border-line-strong">
            <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-primary-soft text-primary"><HomeIcon className="size-6" /></span>
            <div className="min-w-0 flex-1">
              <p className="text-xs font-semibold text-fg-2">Tu departamento</p>
              <p className="text-lg font-extrabold">Depto {inq.depto_numero}</p>
            </div>
            <div className="text-right">
              <p className="text-xs font-semibold text-fg-2">Renta mensual</p>
              <p className="text-lg font-extrabold"><AnimatedNumber value={num(inq.renta)} format={money} /></p>
            </div>
          </Card>

          {/* Estado del mes */}
          <Card className={`transition-colors duration-300 ${pago?.confirmado ? 'border-success/30 bg-success-soft' : ''}`}>
            <div className="flex items-center gap-3">
              {pago?.confirmado ? (
                <motion.span
                  initial={{ scale: 0.6, opacity: 0 }}
                  animate={{ scale: 1, opacity: 1 }}
                  transition={{ type: 'spring', stiffness: 340, damping: 18 }}
                  className="relative grid size-9 place-items-center text-success"
                >
                  <CheckCircle2 className="size-8" />
                </motion.span>
              ) : (
                <Circle className="size-8 text-muted" />
              )}
              <div className="min-w-0 flex-1">
                <p className="text-xs font-bold uppercase tracking-wide text-fg-2">{periodo ? periodoLabel(periodo) : '—'}</p>
                <p className={`text-lg font-extrabold ${pago?.confirmado ? 'text-success' : ''}`}>
                  {pago?.confirmado ? '¡Pago confirmado!' : pago ? 'Pago pendiente' : 'Sin registro de pago'}
                </p>
                {pago?.confirmado && pago.confirmado_en && <p className="text-xs text-fg-2">{fechaHora(pago.confirmado_en)}</p>}
              </div>
              <p className="text-xl font-extrabold">
                <AnimatedNumber value={num(pago ? pago.monto : inq.renta)} format={money} />
              </p>
            </div>
            {!pago?.confirmado && totalAbonado > 0 && (
              <div className="mt-4">
                <ProgressBar value={Math.min(100, (totalAbonado / Math.max(num(pago?.monto), 1)) * 100)} tone="success" />
                <p className="mt-1.5 text-xs text-fg-2">Abonado {money(totalAbonado)} · Saldo pendiente <b className="text-fg">{money(saldo)}</b></p>
              </div>
            )}
          </Card>

          {pago?.rechazado && !pago.confirmado && (
            <motion.div
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              className="flex gap-3 rounded-2xl border border-danger/30 bg-danger-soft p-4 text-sm"
            >
              <AlertTriangle className="mt-0.5 size-5 shrink-0 text-danger" />
              <div>
                <p className="font-bold text-danger">Comprobante rechazado</p>
                <p className="text-fg-2">{pago.comentario_admin ? `Razón: ${pago.comentario_admin}` : 'Por favor envía un nuevo comprobante.'}</p>
              </div>
            </motion.div>
          )}

          {!pago?.confirmado && (
            <>
              {!esTransferencia && !esEfectivo && (
                <Aviso>El administrador aún no ha configurado tu método de pago. Contáctalo para más información.</Aviso>
              )}
              {esTransferencia && <Transferencia inq={inq} cfg={cfg} comprobanteUrl={pago?.comprobante_url ?? null} />}
              {esEfectivo && <PagoEfectivo inq={inq} cfg={cfg} qrToken={pago?.qr_token ?? null} ambos={inq.metodo_pago === 'ambos'} />}
            </>
          )}
        </div>

        <div className="space-y-5">
          {cuotas.length > 0 && (
            <Card className="border-danger/30 bg-danger-soft">
              <p className="mb-3 flex items-center gap-2 text-sm font-bold text-danger"><AlertTriangle className="size-4" /> Cargos adicionales este mes</p>
              <ul className="space-y-1.5">
                {cuotas.map(c => (
                  <li key={c.id} className="flex justify-between gap-3 text-sm"><span>{c.concepto}</span><span className="font-bold text-danger">+{money(c.monto)}</span></li>
                ))}
              </ul>
              <div className="mt-3 flex justify-between border-t border-danger/25 pt-3 font-extrabold">
                <span>Total a pagar</span><span><AnimatedNumber value={num(inq.renta) + totalExtra} format={money} /></span>
              </div>
            </Card>
          )}

          <Card className="flex items-center gap-3 p-4 text-sm text-fg-2">
            <CalendarDays className="size-4 text-primary" /> Fecha de pago: <b className="text-fg">{inq.fecha_pago}</b>
          </Card>

          <div className="space-y-3">
            <LinkRow to="/pagos" icon={<Receipt className="size-[18px]" />} label="Ver historial de pagos" primary />
            <LinkRow to="/tickets" icon={<MessageSquarePlus className="size-[18px]" />} label="Reportar un problema" />
          </div>
        </div>
      </div>
    </div>
  )
}

function Aviso({ children }: { children: ReactNode }) {
  return (
    <div className="flex gap-3 rounded-2xl border border-line bg-hover p-4 text-sm text-fg-2">
      <Info className="mt-0.5 size-5 shrink-0" /><p>{children}</p>
    </div>
  )
}

function LinkRow({ to, icon, label, primary }: { to: string; icon: ReactNode; label: string; primary?: boolean }) {
  return (
    <Link to={to} className={`group flex items-center gap-3 rounded-2xl border border-line bg-card px-4 py-3.5 text-sm font-semibold transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:bg-hover ${primary ? 'text-primary' : 'text-fg-2'}`}>
      {icon}<span className="flex-1">{label}</span><span aria-hidden className="transition-transform duration-200 group-hover:translate-x-1">›</span>
    </Link>
  )
}

// ── Transferencia bancaria + comprobante ──────────────────────────────────────
function Transferencia({ inq, cfg, comprobanteUrl }: { inq: Inquilino; cfg: Config; comprobanteUrl: string | null }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [preview, setPreview] = useState<string | null>(null)
  const hasBank = !!(cfg.banco_clabe || cfg.banco_nombre)

  const elegir = async () => {
    const file = await pickImage()
    if (!file) return
    try { setPreview(await compressImage(file)) } catch (e) { toast(e instanceof Error ? e.message : 'No se pudo abrir la imagen', 'error') }
  }

  const enviar = useMutation({
    mutationFn: () => api.subirComprobante(inq.id, preview!),
    onSuccess: () => { setPreview(null); toast('Comprobante enviado. El administrador revisará tu pago pronto.', 'success'); void qc.invalidateQueries({ queryKey: ['estado-pago'] }) },
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo subir el comprobante', 'error'),
  })

  return (
    <section className="space-y-3">
      <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">Datos para transferencia</h2>
      {!hasBank ? (
        <Aviso>El administrador aún no ha configurado los datos bancarios. Contáctalo.</Aviso>
      ) : (
        <>
          <BankCard3D
            bancoNombre={cfg.banco_nombre}
            clabe={cfg.banco_clabe}
            titular={cfg.banco_titular}
          />

          {preview ? (
            <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }}>
              <Card className="space-y-3 border-primary/30 bg-primary-soft">
                <p className="text-sm font-bold text-primary">Vista previa del comprobante</p>
                <img src={preview} alt="Comprobante" className="mx-auto max-h-80 rounded-xl object-contain" />
                <div className="grid grid-cols-2 gap-2">
                  <Button variant="secondary" onClick={elegir} disabled={enviar.isPending}>Cambiar</Button>
                  <Button onClick={() => enviar.mutate()} loading={enviar.isPending} icon={<Send className="size-4" />}>Enviar</Button>
                </div>
              </Card>
            </motion.div>
          ) : comprobanteUrl ? (
            <Card className="space-y-3 border-success/30 bg-success-soft">
              <p className="flex items-center gap-2 text-sm font-bold text-success"><CheckCircle2 className="size-4" /> Comprobante enviado</p>
              <img src={comprobanteUrl} alt="Comprobante enviado" className="mx-auto max-h-80 rounded-xl object-contain" />
              <Button variant="secondary" full onClick={elegir} icon={<RefreshCw className="size-4" />}>Actualizar comprobante</Button>
            </Card>
          ) : (
            <Button full onClick={elegir} icon={<CloudUpload className="size-[18px]" />}>Subir comprobante de pago</Button>
          )}
        </>
      )}
    </section>
  )
}

// ── Efectivo con QR ───────────────────────────────────────────────────────────
function PagoEfectivo({ inq, cfg, qrToken, ambos }: { inq: Inquilino; cfg: Config; qrToken: string | null; ambos: boolean }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [qr, setQr] = useState<string | null>(null)

  const generar = useMutation({
    mutationFn: () => api.generarQr(inq.id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['estado-pago'] }),
    onError: (e) => toast(e instanceof Error ? e.message : 'No se pudo generar el QR', 'error'),
  })
  const token = generar.data?.data.qr_token ?? qrToken

  // El QR se dibuja en el propio navegador: el token de pago no sale a ningún servicio externo.
  useEffect(() => {
    if (!token) { setQr(null); return }
    const base = cfg.app_url?.trim().replace(/\/$/, '') || window.location.origin
    let cancelled = false
    QRCode.toDataURL(`${base}/confirmar/${token}`, { width: 280, margin: 2, errorCorrectionLevel: 'M' })
      .then(u => { if (!cancelled) setQr(u) })
      .catch(() => { if (!cancelled) setQr(null) })
    return () => { cancelled = true }
  }, [token, cfg.app_url])

  const pasos = ['Genera el código QR', 'Muéstraselo al administrador', 'El admin lo escanea y confirma']
  return (
    <section className="space-y-3">
      <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">{ambos ? 'O paga en efectivo' : 'Pago en efectivo'}</h2>
      <Card className="space-y-4">
        <ol className="space-y-2">
          {pasos.map((p, i) => (
            <li key={p} className="flex items-center gap-3 text-sm text-fg-2">
              <span className="grid size-6 place-items-center rounded-full bg-primary text-xs font-bold text-white shadow-xs shadow-primary/30">{i + 1}</span>{p}
            </li>
          ))}
        </ol>
        <AnimatePresence mode="wait">
          {qr && (
            <motion.div
              key={token}
              initial={{ opacity: 0, scale: 0.88, filter: 'blur(6px)' }}
              animate={{ opacity: 1, scale: 1, filter: 'blur(0px)' }}
              exit={{ opacity: 0, scale: 0.92 }}
              transition={{ type: 'spring', stiffness: 300, damping: 24 }}
              className="mx-auto w-fit rounded-2xl bg-white p-3 shadow-inner"
            >
              <img src={qr} alt="Código QR de pago" className="size-60" />
            </motion.div>
          )}
        </AnimatePresence>
        <Button full onClick={() => generar.mutate()} loading={generar.isPending} icon={<QrCode className="size-[18px]" />}>
          {qr ? 'Regenerar QR' : 'Generar QR'}
        </Button>
      </Card>
    </section>
  )
}


