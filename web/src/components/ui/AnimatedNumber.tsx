import { useEffect, useRef, useState } from 'react'

/**
 * Interpola suavemente un número desde su valor anterior hasta el nuevo con curva easeOutExpo.
 * Respeta prefers-reduced-motion.
 */
export function AnimatedNumber({
  value,
  format = (n) => Math.round(n).toLocaleString('es-MX'),
  duration = 650,
  className,
}: {
  value: number
  format?: (n: number) => string
  duration?: number
  className?: string
}) {
  const [display, setDisplay] = useState(value)
  const fromRef = useRef(0)
  const currentRef = useRef(value)

  useEffect(() => {
    const safeTarget = Number.isFinite(value) ? value : 0
    const reduce = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    if (reduce || duration <= 0) {
      currentRef.current = safeTarget
      setDisplay(safeTarget)
      return
    }

    const startVal = fromRef.current
    const delta = safeTarget - startVal
    if (Math.abs(delta) < 0.01) {
      currentRef.current = safeTarget
      setDisplay(safeTarget)
      return
    }

    let raf = 0
    const t0 = performance.now()
    const step = (now: number) => {
      const elapsed = now - t0
      const p = Math.min(1, elapsed / duration)
      // easeOutExpo
      const eased = p === 1 ? 1 : 1 - Math.pow(2, -10 * p)
      const next = startVal + delta * eased
      currentRef.current = next
      setDisplay(next)
      if (p < 1) {
        raf = requestAnimationFrame(step)
      } else {
        fromRef.current = safeTarget
      }
    }

    raf = requestAnimationFrame(step)
    return () => {
      cancelAnimationFrame(raf)
      fromRef.current = currentRef.current
    }
  }, [value, duration])

  return <span className={className}>{format(display)}</span>
}
