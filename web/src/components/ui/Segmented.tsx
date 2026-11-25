import clsx from 'clsx'
import { motion } from 'motion/react'
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react'

export function Segmented<T extends string>({
  value,
  onChange,
  options,
  ariaLabel = 'Opciones',
}: {
  value: T
  onChange: (v: T) => void
  options: { value: T; label: string }[]
  ariaLabel?: string
}) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const btnRefs = useRef<Record<string, HTMLButtonElement | null>>({})
  const [pill, setPill] = useState<{ left: number; width: number } | null>(null)

  const updatePill = useCallback(() => {
    const el = btnRefs.current[value]
    if (el) {
      setPill({ left: el.offsetLeft, width: el.offsetWidth })
    }
  }, [value])

  useLayoutEffect(() => {
    updatePill()
  }, [updatePill, options.length])

  useEffect(() => {
    window.addEventListener('resize', updatePill)
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(updatePill) : null
    if (ro && containerRef.current) ro.observe(containerRef.current)
    return () => {
      window.removeEventListener('resize', updatePill)
      ro?.disconnect()
    }
  }, [updatePill])

  const onKeyDown = (e: KeyboardEvent<HTMLButtonElement>, idx: number) => {
    if (options.length < 2) return
    const nextIdx =
      e.key === 'ArrowRight' || e.key === 'ArrowDown' ? (idx + 1) % options.length
        : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? (idx - 1 + options.length) % options.length
          : e.key === 'Home' ? 0
            : e.key === 'End' ? options.length - 1
              : -1
    if (nextIdx === -1) return
    e.preventDefault()
    const next = options[nextIdx]
    onChange(next.value)
    btnRefs.current[next.value]?.focus()
  }

  return (
    <div
      ref={containerRef}
      role="tablist"
      aria-label={ariaLabel}
      className="relative flex w-full max-w-full items-center gap-0.5 overflow-hidden rounded-full border border-line bg-bg/90 p-1 shadow-inner sm:inline-flex sm:w-auto sm:gap-1"
    >
      {pill && (
        <motion.span
          aria-hidden="true"
          initial={false}
          animate={{ x: pill.left, width: pill.width }}
          transition={{ type: 'spring', stiffness: 400, damping: 36, bounce: 0 }}
          className="pointer-events-none absolute top-1 bottom-1 left-0 rounded-full border border-primary/45 bg-primary-soft shadow-sm shadow-primary/15"
        />
      )}
      {options.map((o, idx) => {
        const active = value === o.value
        return (
          <button
            key={o.value}
            ref={el => {
              btnRefs.current[o.value] = el
            }}
            type="button"
            role="tab"
            tabIndex={active ? 0 : -1}
            aria-selected={active}
            onKeyDown={e => onKeyDown(e, idx)}
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
