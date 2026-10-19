import { motion } from 'motion/react'
import type { ReactNode } from 'react'
import { BrandLogo } from '../components/layout/BrandLogo'

/** Marco común de login/registro: imagen de marca a la izquierda en escritorio, formulario a la derecha. */
export function AuthLayout({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-1 min-h-full lg:grid-cols-2">
      <div className="relative hidden overflow-hidden lg:block">
        <img src="/auth-bg.jpg" alt="" className="animate-ken-burns absolute inset-0 size-full object-cover" />
        <div className="absolute inset-0 bg-gradient-to-br from-[#0E1321]/85 via-[#0E1321]/60 to-primary/40" />
        <div className="relative flex h-full flex-col justify-between p-12 text-white">
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.45 }}
            className="self-start"
          >
            <BrandLogo variant="hero" />
          </motion.div>
          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.55, delay: 0.1, ease: [0.22, 1, 0.36, 1] }}
          >
            <h2 className="max-w-md text-4xl font-extrabold leading-tight tracking-tight">Tus departamentos, rentas y contratos en un solo lugar.</h2>
            <p className="mt-3 max-w-md text-white/70">Cobranza, contratos, tickets y notificaciones en tiempo real.</p>
          </motion.div>
        </div>
      </div>

      <div className="safe-top safe-bottom flex items-center justify-center bg-bg px-5 py-10">
        <motion.div
          initial={{ opacity: 0, y: 14 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.38, ease: [0.22, 1, 0.36, 1] }}
          className="w-full max-w-md"
        >
          <BrandLogo className="mb-8 lg:hidden" />
          <h1 className="text-3xl font-extrabold tracking-tight">{title}</h1>
          <p className="mb-7 mt-1.5 text-sm text-fg-2">{subtitle}</p>
          {children}
        </motion.div>
      </div>
    </div>
  )
}
