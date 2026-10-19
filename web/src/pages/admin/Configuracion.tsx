import { AnimatePresence, motion } from 'motion/react'
import { useSearchParams } from 'react-router-dom'
import { PageHeader } from '../../components/ui/PageHeader'
import { Segmented } from '../../components/ui/Segmented'
import { General } from './config/General'
import { Invitaciones } from './config/Invitaciones'
import { Respaldos } from './config/Respaldos'
import { Usuarios } from './config/Usuarios'

type Tab = 'general' | 'usuarios' | 'invitaciones' | 'respaldos'
const TABS: { value: Tab; label: string }[] = [
  { value: 'general', label: 'General' }, { value: 'usuarios', label: 'Usuarios' },
  { value: 'invitaciones', label: 'Invitaciones' }, { value: 'respaldos', label: 'Respaldos' },
]

export default function Configuracion() {
  const [sp, setSp] = useSearchParams()
  const tab = (TABS.find(t => t.value === sp.get('tab'))?.value ?? 'general') as Tab
  return (
    <div className="min-w-0 space-y-6">
      <PageHeader title="Configuración" subtitle="Tu cuenta, el equipo y el funcionamiento del sistema" />
      <Segmented value={tab} onChange={t => setSp(t === 'general' ? {} : { tab: t }, { replace: true })} options={TABS} />
      <AnimatePresence mode="wait">
        <motion.div
          key={tab} className="min-w-0"
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          exit={{ opacity: 0, y: -6 }}
          transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
        >
          {tab === 'general' && <General />}
          {tab === 'usuarios' && <Usuarios />}
          {tab === 'invitaciones' && <Invitaciones />}
          {tab === 'respaldos' && <Respaldos />}
        </motion.div>
      </AnimatePresence>
    </div>
  )
}

