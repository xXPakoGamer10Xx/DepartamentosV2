import { Check, Copy, Wifi } from 'lucide-react'
import { motion } from 'motion/react'
import { useState, type MouseEvent, type ReactNode } from 'react'
import { detectBankInfo, getBankBrand } from '../../lib/bank'

/**
 * Tarjeta bancaria interactiva con inclinación 3D sutil (tilt), brillo radial y botón para copiar CLABE.
 */
export function BankCard3D({
  bancoNombre,
  clabe,
  titular,
  alias,
  badges,
  rounded = 'rounded-3xl',
}: {
  bancoNombre?: string | null
  clabe?: string | null
  titular?: string | null
  alias?: string | null
  badges?: ReactNode
  rounded?: string
}) {
  const brand = getBankBrand(bancoNombre ?? undefined)
  const info = detectBankInfo(clabe || '')
  const [tilt, setTilt] = useState({ rx: 0, ry: 0, gx: 50, gy: 50, active: false })
  const [copied, setCopied] = useState(false)

  const onMove = (e: MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const px = (e.clientX - rect.left) / rect.width
    const py = (e.clientY - rect.top) / rect.height
    const ry = (px - 0.5) * 9
    const rx = (0.5 - py) * 7
    setTilt({ rx, ry, gx: Math.round(px * 100), gy: Math.round(py * 100), active: true })
  }

  const onLeave = () => setTilt({ rx: 0, ry: 0, gx: 50, gy: 50, active: false })

  const copyClabe = async () => {
    const raw = (clabe || '').replace(/\s+/g, '')
    if (!raw) return
    try {
      await navigator.clipboard.writeText(raw)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Ignorar si el navegador no permite portapapeles
    }
  }

  return (
    <motion.div
      onMouseMove={onMove}
      onMouseLeave={onLeave}
      animate={{ rotateX: tilt.rx, rotateY: tilt.ry, scale: tilt.active ? 1.01 : 1 }}
      transition={{ type: 'spring', stiffness: 260, damping: 22 }}
      style={{
        transformPerspective: 900,
        background: `linear-gradient(135deg, ${brand.gradient[0]}, ${brand.gradient[1]})`,
      }}
      className={`group relative overflow-hidden p-6 text-white shadow-xl ${rounded}`}
    >
      {/* Brillo especular interactivo */}
      <div
        className="pointer-events-none absolute inset-0 transition-opacity duration-300"
        style={{
          opacity: tilt.active ? 0.22 : 0,
          background: `radial-gradient(circle at ${tilt.gx}% ${tilt.gy}%, rgba(255,255,255,0.65), transparent 60%)`,
        }}
      />
      {/* Círculo decorativo de marca */}
      <div
        className="pointer-events-none absolute -right-10 -top-10 size-40 rounded-full opacity-20 transition-transform duration-500 group-hover:scale-110"
        style={{ background: brand.accent }}
      />

      <div className="relative flex items-start justify-between gap-2">
        <div className="flex items-center gap-3">
          <span className="h-8 w-11 shrink-0 rounded-md shadow-inner" style={{ background: brand.chip }} />
          <div>
            <p className="text-xs font-bold uppercase tracking-widest text-white/75">{bancoNombre || 'BANCO'}</p>
            {alias && <p className="text-base font-extrabold leading-tight">{alias}</p>}
          </div>
        </div>
        {badges ? (
          <div className="flex flex-col items-end gap-1">{badges}</div>
        ) : (
          <Wifi className="size-5 rotate-90 text-white/60" />
        )}
      </div>

      <div className="relative mt-6 space-y-1.5">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="font-mono text-lg font-bold tracking-wider sm:text-xl">{info.formato}</p>
          {clabe && (
            <button
              type="button"
              onClick={copyClabe}
              className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-xs font-bold text-white backdrop-blur-xs transition hover:bg-white/25 active:scale-95"
              aria-label="Copiar número CLABE"
            >
              {copied ? (
                <>
                  <Check className="size-3.5 text-emerald-300" />
                  <span>¡Copiada!</span>
                </>
              ) : (
                <>
                  <Copy className="size-3.5" />
                  <span>Copiar</span>
                </>
              )}
            </button>
          )}
        </div>
        {titular && <p className="text-sm uppercase text-white/80">{titular}</p>}
      </div>

      <p className="relative mt-4 text-right text-xs font-bold uppercase tracking-widest" style={{ color: brand.accent }}>
        {info.tipo}
      </p>
    </motion.div>
  )
}
