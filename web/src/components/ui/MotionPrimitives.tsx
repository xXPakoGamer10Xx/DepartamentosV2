import { motion, type HTMLMotionProps } from 'motion/react'
import type { ReactNode } from 'react'

/**
 * Contenedor con entrada escalonada (stagger) para listas y grillas de tarjetas.
 */
export function StaggerList({
  children,
  className,
  stagger = 0.045,
}: {
  children: ReactNode
  className?: string
  stagger?: number
}) {
  return (
    <motion.div
      initial="hidden"
      animate="show"
      variants={{
        hidden: {},
        show: { transition: { staggerChildren: stagger } },
      }}
      className={className}
    >
      {children}
    </motion.div>
  )
}

/**
 * Elemento individual dentro de un StaggerList o con animación de entrada y layout.
 */
export function StaggerItem({
  children,
  className,
  layout = false,
  ...rest
}: HTMLMotionProps<'div'> & { children: ReactNode; className?: string; layout?: boolean }) {
  return (
    <motion.div
      layout={layout}
      variants={{
        hidden: { opacity: 0, y: 12, scale: 0.985 },
        show: {
          opacity: 1,
          y: 0,
          scale: 1,
          transition: { type: 'spring', stiffness: 340, damping: 28 },
        },
      }}
      className={className}
      {...rest}
    >
      {children}
    </motion.div>
  )
}

/**
 * Sacude horizontalmente su contenido cuando aparece o cambia un mensaje de error.
 */
export function ShakeError({ error, children, className }: { error?: string | boolean | null; children: ReactNode; className?: string }) {
  if (!error) return null
  return (
    <motion.div
      key={typeof error === 'string' ? error : 'err'}
      initial={{ opacity: 0, x: -8 }}
      animate={{ opacity: 1, x: [0, -7, 7, -5, 5, 0] }}
      transition={{ duration: 0.36, ease: 'easeInOut' }}
      className={className}
    >
      {children}
    </motion.div>
  )
}
