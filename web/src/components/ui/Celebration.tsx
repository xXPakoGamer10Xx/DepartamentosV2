import { motion } from 'motion/react'
import { useEffect, useRef } from 'react'

/**
 * Icono de éxito circular con halo expansivo y trazo SVG animado (check).
 */
export function SuccessCheck({ size = 80 }: { size?: number }) {
  return (
    <div className="relative mx-auto mb-5 grid place-items-center" style={{ width: size, height: size }}>
      <span className="animate-pulse-ring absolute inset-0 rounded-full bg-emerald-500/25" />
      <motion.div
        initial={{ scale: 0.5, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        transition={{ type: 'spring', stiffness: 320, damping: 18 }}
        className="relative grid size-full place-items-center rounded-full bg-emerald-500/15 text-emerald-400 shadow-lg shadow-emerald-500/20"
      >
        <svg viewBox="0 0 52 52" className="size-3/5" fill="none">
          <motion.circle
            cx="26"
            cy="26"
            r="22"
            stroke="currentColor"
            strokeWidth="3.5"
            strokeLinecap="round"
            initial={{ pathLength: 0, opacity: 0.4 }}
            animate={{ pathLength: 1, opacity: 1 }}
            transition={{ duration: 0.45, ease: 'easeOut' }}
          />
          <motion.path
            d="M16 26.5L22.8 33.2L36.5 19.5"
            stroke="currentColor"
            strokeWidth="4"
            strokeLinecap="round"
            strokeLinejoin="round"
            initial={{ pathLength: 0 }}
            animate={{ pathLength: 1 }}
            transition={{ duration: 0.38, delay: 0.22, ease: 'easeOut' }}
          />
        </svg>
      </motion.div>
    </div>
  )
}

interface Particle {
  x: number
  y: number
  vx: number
  vy: number
  size: number
  color: string
  angle: number
  spin: number
  alpha: number
}

const PALETTE = ['#10B981', '#3B82F6', '#F59E0B', '#34D399', '#60A5FA', '#A78BFA']

/**
 * Explosión ligera de confeti en Canvas que se autodestruye al terminar (~2.2s).
 */
export function ConfettiBurst({ active = true }: { active?: boolean }) {
  const ref = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    if (!active) return
    if (typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return
    const canvas = ref.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const dpr = Math.min(window.devicePixelRatio || 1, 2)
    const w = window.innerWidth
    const h = window.innerHeight
    canvas.width = w * dpr
    canvas.height = h * dpr
    ctx.scale(dpr, dpr)

    const originX = w / 2
    const originY = Math.min(h * 0.38, 320)

    const particles: Particle[] = Array.from({ length: 52 }, () => {
      const theta = -Math.PI / 2 + (Math.random() - 0.5) * Math.PI * 1.15
      const speed = 5 + Math.random() * 9
      return {
        x: originX,
        y: originY,
        vx: Math.cos(theta) * speed,
        vy: Math.sin(theta) * speed - 2,
        size: 5 + Math.random() * 5,
        color: PALETTE[Math.floor(Math.random() * PALETTE.length)],
        angle: Math.random() * Math.PI * 2,
        spin: (Math.random() - 0.5) * 0.25,
        alpha: 1,
      }
    })

    let raf = 0
    const start = performance.now()
    const tick = (now: number) => {
      const elapsed = now - start
      ctx.clearRect(0, 0, w, h)
      let alive = false
      for (const p of particles) {
        p.x += p.vx
        p.y += p.vy
        p.vy += 0.24 // gravedad
        p.vx *= 0.985
        p.angle += p.spin
        if (elapsed > 1100) p.alpha = Math.max(0, 1 - (elapsed - 1100) / 900)
        if (p.alpha > 0.02 && p.y < h + 20) alive = true

        ctx.save()
        ctx.globalAlpha = p.alpha
        ctx.translate(p.x, p.y)
        ctx.rotate(p.angle)
        ctx.fillStyle = p.color
        ctx.fillRect(-p.size / 2, -p.size / 3, p.size, p.size * 0.66)
        ctx.restore()
      }
      if (alive && elapsed < 2200) {
        raf = requestAnimationFrame(tick)
      } else {
        ctx.clearRect(0, 0, w, h)
      }
    }

    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  }, [active])

  if (!active) return null
  return <canvas ref={ref} className="pointer-events-none fixed inset-0 z-[110] size-full" aria-hidden />
}
