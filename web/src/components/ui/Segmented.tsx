import clsx from 'clsx'
import { motion } from 'motion/react'
import { useEffect, useLayoutEffect, useRef, useState } from 'react'

export function Segmented<T extends string>({
  value,
  onChange,
  options,
}: {
  value: T
  onChange: (v: T) => void
  options: { value: T; label: string }[]
}) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const btnRefs = useRef<Record<string, HTMLButtonElement | null>>({})
  const [pill, setPill] = useState<{ left: number; width: number } | null>(null)

  const updatePill = () => {
    const el = btnRefs.current[value]
    if (el) {
      setPill({ left: el.offsetLeft, width: el.offsetWidth })
    }
  }

  useLayoutEffect(() => {
    updatePill()
  }, [value, options.length])

  useEffect(() => {
    window.addEventListener('resize', updatePill)
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(updatePill) : null
    if (ro && containerRef.current) ro.observe(containerRef.current)
    return () => {
      window.removeEventListener('resize', updatePill)
      ro?.disconnect()
    }
  }, [value])

  return (
    <div
      ref={containerRef}
      role="tablist"
      className="relative flex w-full max-w-full items-center gap-0.5 overflow-hidden rounded-full border border-line bg-bg/90 p-1 shadow-inner sm:inline-flex sm:w-auto sm:gap-1"
    >
      {pill && (
        <motion.span
          initial={false}
          animate={{ x: pill.left, width: pill.width }}
          transition={{ type: 'spring', stiffness: 400, damping: 36, bounce: 0 }}
          className="pointer-events-none absolute top-1 bottom-1 left-0 rounded-full border border-primary/45 bg-primary-soft shadow-sm shadow-primary/15"
        />
      )}
      {options.map(o => {
        const active = value === o.value
        return (
          <button
            key={o.value}
            ref={el => {
              btnRefs.current[o.value] = el
            }}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(o.value)}
            className={clsx(
              'relative z-10 flex-1 cursor-pointer truncate rounded-full px-2 py-1.5 text-center text-xs font-semibold transition-colors duration-200 sm:flex-initial sm:px-4 sm:text-sm',
              active ? 'text-fg' : 'text-fg-2 hover:text-fg',
            )}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}



