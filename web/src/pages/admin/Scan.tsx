import { CameraOff, ScanLine, Search, XCircle } from 'lucide-react'
import { AnimatePresence, motion } from 'motion/react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfettiBurst, SuccessCheck } from '../../components/ui/Celebration'
import { ShakeError, StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { PageHeader } from '../../components/ui/PageHeader'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { money, num, periodoLabel } from '../../lib/format'

const TOKEN_RX = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
type Stage = 'scan' | 'confirm' | 'done' | 'error'
interface PagoInfo { nombre_completo?: string; depto_numero?: number; periodo?: string; monto?: string; renta?: string; confirmado?: boolean }

export default function Scan() {
  const toast = useToast()
  const [stage, setStage] = useState<Stage>('scan')
  const [pago, setPago] = useState<PagoInfo | null>(null)
  const [token, setToken] = useState('')
  const [error, setError] = useState('')
  const [manual, setManual] = useState('')
  const [buscando, setBuscando] = useState(false)
  const [confirmando, setConfirmando] = useState(false)
  const [recienConfirmado, setRecienConfirmado] = useState(false)
  const [camara, setCamara] = useState<'iniciando' | 'activa' | 'denegada' | 'no-soportada'>('iniciando')
  const procesado = useRef(false)

  const procesar = useCallback(async (tok: string) => {
    if (!tok) return
    setBuscando(true); setError(''); setRecienConfirmado(false)
    try {
      const r = await api.getPagoByToken(tok)
      setPago(r.data as PagoInfo); setToken(tok)
      setStage(r.data.confirmado ? 'done' : 'confirm')
    } catch { setError('QR inválido o expirado'); setStage('error') }
    finally { setBuscando(false) }
  }, [])

  // Cámara + decodificación de QR en el propio navegador (jsQR se carga solo al abrir esta pantalla).
  const video = useRef<HTMLVideoElement>(null)
  useEffect(() => {
    if (stage !== 'scan') return
    procesado.current = false
    if (!navigator.mediaDevices?.getUserMedia) { setCamara('no-soportada'); return }
    let stream: MediaStream | null = null
    let raf = 0
    let cancelled = false

    ;(async () => {
      try {
        const [{ default: jsQR }, s] = await Promise.all([
          import('jsqr'),
          navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false }),
        ])
        if (cancelled) { s.getTracks().forEach(t => t.stop()); return }
        stream = s
        const v = video.current!
        v.srcObject = s
        await v.play().catch(() => {})
        setCamara('activa')
        const canvas = document.createElement('canvas')
        const ctx = canvas.getContext('2d', { willReadFrequently: true })!
        let last = 0
        const tick = (t: number) => {
          raf = requestAnimationFrame(tick)
          if (t - last < 140 || v.readyState < 2 || procesado.current) return
          last = t
          const w = v.videoWidth, h = v.videoHeight
          if (!w || !h) return
          const scale = Math.min(1, 640 / w)
          canvas.width = Math.round(w * scale); canvas.height = Math.round(h * scale)
          ctx.drawImage(v, 0, 0, canvas.width, canvas.height)
          const img = ctx.getImageData(0, 0, canvas.width, canvas.height)
          const code = jsQR(img.data, img.width, img.height, { inversionAttempts: 'dontInvert' })
          const m = code?.data.match(TOKEN_RX)
          if (m) { procesado.current = true; void procesar(m[0]) }
        }
        raf = requestAnimationFrame(tick)
      } catch (e) {
        if (!cancelled) setCamara(e instanceof DOMException && e.name === 'NotAllowedError' ? 'denegada' : 'no-soportada')
      }
    })()

    return () => { cancelled = true; cancelAnimationFrame(raf); stream?.getTracks().forEach(t => t.stop()) }
  }, [stage, procesar])

  const reset = () => { setStage('scan'); setPago(null); setToken(''); setError(''); setManual(''); setRecienConfirmado(false); setCamara('iniciando') }

  const confirmar = async () => {
    setConfirmando(true)
    try { await api.confirmarPago(token); setRecienConfirmado(true); setStage('done'); toast('Pago confirmado', 'success') }
    catch (e) { setError(e instanceof Error ? e.message : 'Error al confirmar el pago') }
    finally { setConfirmando(false) }
  }

  const buscarManual = () => {
    const raw = manual.trim()
    if (!raw) return
    const m = raw.match(TOKEN_RX)
    void procesar(m ? m[0] : raw)
  }

  return (
    <div className="mx-auto max-w-lg space-y-6">
      <ConfettiBurst active={stage === 'done' && recienConfirmado} />
      <PageHeader title="Escanear QR" subtitle="Acepta pagos en efectivo escaneando el código del inquilino" />

      <AnimatePresence mode="wait">
        {stage === 'scan' && (
          <StaggerList key="scan" className="space-y-6">
            <StaggerItem className="relative aspect-square overflow-hidden rounded-3xl bg-black shadow-lg">
              <video ref={video} className="size-full object-cover" playsInline muted aria-label="Vista de la cámara" />
              {camara === 'activa' && (
                <div className="pointer-events-none absolute inset-0 grid place-items-center">
                  <div className="relative size-2/3 rounded-3xl border-2 border-white/75 shadow-[0_0_0_9999px_rgba(0,0,0,0.48)]">
                    {/* Esquinas resaltadas */}
                    <span className="absolute -left-0.5 -top-0.5 size-6 rounded-tl-3xl border-l-4 border-t-4 border-primary" />
                    <span className="absolute -right-0.5 -top-0.5 size-6 rounded-tr-3xl border-r-4 border-t-4 border-primary" />
                    <span className="absolute -bottom-0.5 -left-0.5 size-6 rounded-bl-3xl border-b-4 border-l-4 border-primary" />
                    <span className="absolute -bottom-0.5 -right-0.5 size-6 rounded-br-3xl border-b-4 border-r-4 border-primary" />
                    {/* Línea láser animada */}
                    <div className="animate-qr-laser absolute inset-x-3 top-1/2 h-0.5 rounded-full bg-gradient-to-r from-transparent via-primary to-transparent shadow-[0_0_12px_2px_rgba(59,130,246,0.85)]" />
                  </div>
                </div>
              )}
              {camara !== 'activa' && (
                <div className="absolute inset-0 grid place-items-center p-8 text-center text-white">
                  {camara === 'iniciando' ? <p className="text-sm text-white/70">Iniciando cámara…</p> : (
                    <div className="space-y-2"><CameraOff className="mx-auto size-10 text-white/70" />
                      <p className="font-bold">{camara === 'denegada' ? 'Permiso de cámara denegado' : 'Cámara no disponible'}</p>
                      <p className="text-sm text-white/70">{camara === 'denegada' ? 'Actívala en los ajustes del navegador o ingresa el código a mano.' : 'La cámara necesita HTTPS. Ingresa el código a mano.'}</p></div>
                  )}
                </div>
              )}
            </StaggerItem>

            <StaggerItem>
              <Card className="space-y-3">
                <p className="text-sm font-semibold">¿Sin cámara? Ingresa el código</p>
                <div className="flex gap-2">
                  <input value={manual} onChange={e => { setManual(e.target.value); setError('') }} onKeyDown={e => e.key === 'Enter' && buscarManual()}
                    placeholder="Código del QR (o pega la liga)" aria-label="Código del QR" autoCapitalize="none" autoCorrect="off"
                    className="h-12 flex-1 rounded-xl border border-line bg-card px-4 text-sm placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25" />
                  <Button onClick={buscarManual} loading={buscando} disabled={!manual.trim()} aria-label="Buscar" icon={<Search className="size-4" />} className="px-4" />
                </div>
                <ShakeError error={error}>
                  <p role="alert" className="text-sm text-danger">{error}</p>
                </ShakeError>
              </Card>
            </StaggerItem>
          </StaggerList>
        )}

        {stage === 'confirm' && pago && (
          <motion.div
            key="confirm"
            initial={{ opacity: 0, y: 16, scale: 0.97 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, scale: 0.96 }}
            transition={{ type: 'spring', stiffness: 340, damping: 26 }}
          >
            <Card className="space-y-5 text-center">
              <motion.span
                initial={{ scale: 0.7 }}
                animate={{ scale: 1 }}
                transition={{ type: 'spring', stiffness: 340, damping: 18 }}
                className="mx-auto grid size-20 place-items-center rounded-full bg-primary-soft text-primary"
              >
                <ScanLine className="size-10" />
              </motion.span>
              <div><h2 className="text-xl font-extrabold">Confirmar pago en efectivo</h2><p className="text-sm text-fg-2">Verifica los datos antes de confirmar</p></div>
              <dl className="space-y-2 text-left text-sm">
                <Fila k="Inquilino" v={pago.nombre_completo} /><Fila k="Departamento" v={`Depto ${pago.depto_numero}`} />
                <Fila k="Periodo" v={pago.periodo ? periodoLabel(pago.periodo) : '—'} />
                <div className="flex items-center justify-between rounded-2xl bg-hover px-4 py-3">
                  <dt className="text-fg-2">Monto</dt>
                  <dd className="text-2xl font-extrabold"><AnimatedNumber value={num(pago.monto ?? pago.renta)} format={money} /></dd>
                </div>
              </dl>
              <ShakeError error={error}>
                <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>
              </ShakeError>
              <div className="grid grid-cols-2 gap-2"><Button variant="secondary" onClick={reset} disabled={confirmando}>Cancelar</Button><Button loading={confirmando} onClick={confirmar}>Confirmar pago</Button></div>
            </Card>
          </motion.div>
        )}

        {stage === 'done' && pago && (
          <motion.div
            key="done"
            initial={{ opacity: 0, scale: 0.94 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0 }}
            transition={{ type: 'spring', stiffness: 340, damping: 24 }}
          >
            <Card className="space-y-4 text-center">
              <SuccessCheck size={80} />
              <h2 className="text-xl font-extrabold">{pago.confirmado ? '¡Ya estaba pagado!' : '¡Pago confirmado!'}</h2>
              <p className="text-sm text-fg-2">{pago.nombre_completo} · Depto {pago.depto_numero} · {money(pago.monto ?? pago.renta)}</p>
              <Button full onClick={reset} icon={<ScanLine className="size-4" />}>Escanear otro</Button>
            </Card>
          </motion.div>
        )}

        {stage === 'error' && (
          <motion.div
            key="error"
            initial={{ opacity: 0, scale: 0.94 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0 }}
          >
            <Card className="space-y-4 text-center">
              <span className="mx-auto grid size-20 place-items-center rounded-full bg-danger-soft text-danger"><XCircle className="size-10" /></span>
              <h2 className="text-xl font-extrabold">QR inválido</h2><p className="text-sm text-fg-2">{error}</p>
              <Button full onClick={reset}>Intentar de nuevo</Button>
            </Card>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}

const Fila = ({ k, v }: { k: string; v?: string }) => <div className="flex justify-between gap-3"><dt className="text-fg-2">{k}</dt><dd className="font-semibold">{v ?? '—'}</dd></div>

