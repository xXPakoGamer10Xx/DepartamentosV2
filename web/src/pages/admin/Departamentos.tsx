import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, Plus, Trash2, X } from 'lucide-react'
import { motion } from 'motion/react'
import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { AnimatedNumber } from '../../components/ui/AnimatedNumber'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EmptyState } from '../../components/ui/EmptyState'
import { Input } from '../../components/ui/Input'
import { Modal } from '../../components/ui/Modal'
import { ShakeError, StaggerItem, StaggerList } from '../../components/ui/MotionPrimitives'
import { PageHeader } from '../../components/ui/PageHeader'
import { Spinner } from '../../components/ui/Spinner'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { money, num } from '../../lib/format'
import type { Departamento } from '../../lib/types'

export const ESTADO_DEPTO = {
  disponible: { label: 'Disponible', tone: 'success', bar: 'bg-success' },
  ocupado: { label: 'Ocupado', tone: 'primary', bar: 'bg-primary' },
  mantenimiento: { label: 'Mantenimiento', tone: 'warning', bar: 'bg-warning' },
} as const

const pisoDe = (n: number) => (n >= 100 ? Math.floor(n / 100) : null)

export default function Departamentos() {
  const { user } = useAuth()
  const qc = useQueryClient()
  const toast = useToast()
  const esAdmin = user?.rol === 'admin'
  const [piso, setPiso] = useState<'todos' | number>('todos')
  const [nuevo, setNuevo] = useState(false)
  const [borrar, setBorrar] = useState<Departamento | null>(null)

  const deptos = useQuery({ queryKey: ['departamentos'], queryFn: async () => (await api.getDepartamentos()).data })
  const stats = useQuery({ queryKey: ['deptos-stats'], queryFn: async () => (await api.getDepartamentosStats()).data })

  const lista = deptos.data ?? []
  const pisos = useMemo(() => [...new Set(lista.map(d => pisoDe(d.numero)).filter((p): p is number => p !== null))].sort((a, b) => a - b), [lista])
  const visibles = lista.filter(d => piso === 'todos' || pisoDe(d.numero) === piso)

  const total = num(stats.data?.total)
  const ocupados = num(stats.data?.ocupados)

  const eliminar = useMutation({
    mutationFn: (n: number) => api.deleteDepartamento(n),
    onSuccess: () => {
      toast('Departamento eliminado', 'success'); setBorrar(null)
      void qc.invalidateQueries({ queryKey: ['departamentos'] }); void qc.invalidateQueries({ queryKey: ['deptos-stats'] })
    },
  })

  return (
    <StaggerList className="space-y-6">
      <PageHeader title="Departamentos" subtitle={`${total} unidad${total !== 1 ? 'es' : ''}`}
        actions={esAdmin && <Button icon={<Plus className="size-4" />} onClick={() => setNuevo(true)}>Nuevo departamento</Button>} />

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <StaggerItem className="h-full"><Kpi label="Total" value={<AnimatedNumber value={total} />} /></StaggerItem>
        <StaggerItem className="h-full"><Kpi label="Ocupados" value={<><AnimatedNumber value={ocupados} />{total ? ` (${Math.round((ocupados / total) * 100)}%)` : ''}</>} tone="text-primary" /></StaggerItem>
        <StaggerItem className="h-full"><Kpi label="Disponibles" value={<AnimatedNumber value={num(stats.data?.disponibles)} />} tone="text-success" /></StaggerItem>
        <StaggerItem className="h-full"><Kpi label="Mantenimiento" value={<AnimatedNumber value={num(stats.data?.mantenimiento)} />} tone="text-warning" /></StaggerItem>
      </div>

      {pisos.length > 1 && (
        <StaggerItem className="flex flex-wrap gap-2">
          {(['todos', ...pisos] as const).map(p => {
            const active = piso === p
            return (
              <button key={p} onClick={() => setPiso(p)} className={`relative rounded-full border px-4 py-1.5 text-sm font-semibold transition-colors ${active ? 'border-primary text-white' : 'border-line bg-card text-fg-2 hover:bg-hover'}`}>
                {active && (
                  <motion.span
                    layoutId="deptos-piso-pill"
                    transition={{ type: 'spring', stiffness: 400, damping: 30 }}
                    className="absolute inset-0 rounded-full bg-primary shadow-sm shadow-primary/25"
                  />
                )}
                <span className="relative z-10">{p === 'todos' ? 'Todos' : `Piso ${p}`}</span>
              </button>
            )
          })}
        </StaggerItem>
      )}

      {deptos.isLoading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}
      {!deptos.isLoading && visibles.length === 0 && (
        <StaggerItem>
          <Card><EmptyState icon={Building2} title="Sin departamentos">{esAdmin ? 'Agrega el primero con “Nuevo departamento”.' : 'Aún no hay departamentos registrados.'}</EmptyState></Card>
        </StaggerItem>
      )}

      {visibles.length > 0 && (
        <StaggerList key={String(piso)} className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {visibles.map(d => {
          const cfg = ESTADO_DEPTO[d.estado]
          const inv = d.inventario_base?.length ?? 0
          return (
            <StaggerItem key={d.numero} layout>
              <Card className="group relative overflow-hidden p-0 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md">
                <span className={`absolute inset-y-0 left-0 w-1.5 transition-all duration-200 group-hover:w-2 ${cfg.bar}`} />
                <Link to={`/departamentos/${d.numero}`} className="block p-5 pl-6">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="text-xs font-bold uppercase tracking-wide text-muted">{pisoDe(d.numero) ? `Piso ${pisoDe(d.numero)}` : 'Unidad'}</p>
                      <p className="text-2xl font-extrabold tracking-tight">Depto {d.numero}</p>
                    </div>
                    <Badge tone={cfg.tone}>{cfg.label}</Badge>
                  </div>
                  {d.descripcion && <p className="mt-1 line-clamp-1 text-sm text-fg-2">{d.descripcion}</p>}
                  <div className="mt-4 border-t border-line pt-3 text-sm">
                    {d.estado === 'ocupado' && d.inquilino_actual
                      ? <div className="flex items-center justify-between gap-2"><span className="truncate font-semibold">{d.inquilino_actual.nombre}</span><span className="font-bold">{money(d.inquilino_actual.renta)}</span></div>
                      : <span className="text-fg-2">{inv > 0 ? `${inv} artículo${inv !== 1 ? 's' : ''} en inventario` : 'Sin inventario'}</span>}
                  </div>
                </Link>
                {esAdmin && d.estado !== 'ocupado' && (
                  <button onClick={() => { eliminar.reset(); setBorrar(d) }} aria-label={`Eliminar departamento ${d.numero}`}
                    className="absolute right-3 top-14 grid size-8 place-items-center rounded-lg text-muted opacity-0 transition hover:bg-danger-soft hover:text-danger focus:opacity-100 group-hover:opacity-100 max-md:opacity-100">
                    <Trash2 className="size-4" />
                  </button>
                )}
              </Card>
            </StaggerItem>
          )
        })}
        </StaggerList>
      )}

      <NuevoDepto open={nuevo} onClose={() => setNuevo(false)} />

      <ConfirmDialog open={!!borrar} title="Eliminar departamento" confirmLabel="Eliminar" loading={eliminar.isPending}
        error={eliminar.error instanceof Error ? eliminar.error.message : null}
        onClose={() => setBorrar(null)} onConfirm={() => borrar && eliminar.mutate(borrar.numero)}>
        <p>¿Seguro que deseas eliminar el <b className="text-fg">Departamento {borrar?.numero}</b>? Esta acción no se puede deshacer.</p>
      </ConfirmDialog>
    </StaggerList>
  )
}

const Kpi = ({ label, value, tone = '' }: { label: string; value: ReactNode; tone?: string }) => (
  <Card className="h-full p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md"><p className="text-[11px] font-bold uppercase tracking-wide text-muted">{label}</p><p className={`mt-1 text-2xl font-extrabold ${tone}`}>{value}</p></Card>
)

/** Editor de lista de textos (inventario): alta, baja y reordenar. */
export function InventarioEditor({ items, onChange, disabled }: { items: string[]; onChange: (v: string[]) => void; disabled?: boolean }) {
  const [txt, setTxt] = useState('')
  const add = () => { const t = txt.trim(); if (t) { onChange([...items, t]); setTxt('') } }
  const move = (i: number, d: -1 | 1) => { const a = [...items]; const j = i + d; if (j < 0 || j >= a.length) return; [a[i], a[j]] = [a[j], a[i]]; onChange(a) }
  return (
    <div className="space-y-3">
      <ul className="space-y-1.5">
        {items.map((it, i) => (
          <li key={`${it}-${i}`} className="flex items-center gap-2 rounded-xl bg-hover px-3 py-2 text-sm">
            <span className="flex-1">{it}</span>
            {!disabled && <>
              <button type="button" onClick={() => move(i, -1)} disabled={i === 0} aria-label="Subir" className="px-1 text-muted hover:text-fg disabled:opacity-30">↑</button>
              <button type="button" onClick={() => move(i, 1)} disabled={i === items.length - 1} aria-label="Bajar" className="px-1 text-muted hover:text-fg disabled:opacity-30">↓</button>
              <button type="button" onClick={() => onChange(items.filter((_, k) => k !== i))} aria-label={`Quitar ${it}`} className="text-muted hover:text-danger"><X className="size-4" /></button>
            </>}
          </li>
        ))}
        {items.length === 0 && <li className="text-sm text-muted">Sin artículos</li>}
      </ul>
      {!disabled && (
        <div className="flex gap-2">
          <input value={txt} onChange={e => setTxt(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); add() } }} placeholder="Ej. Refrigerador"
            className="h-11 flex-1 rounded-xl border border-line bg-card px-4 text-sm placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25" />
          <Button type="button" variant="secondary" onClick={add} className="h-11">Agregar</Button>
        </div>
      )}
    </div>
  )
}

function NuevoDepto({ open, onClose }: { open: boolean; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [numero, setNumero] = useState('')
  const [desc, setDesc] = useState('')
  const [inv, setInv] = useState<string[]>([])
  const [error, setError] = useState('')

  const crear = useMutation({
    mutationFn: () => api.createDepartamento({ numero: Number(numero), descripcion: desc.trim() || null, inventario_base: inv }),
    onSuccess: () => {
      toast(`Departamento ${numero} creado`, 'success')
      setNumero(''); setDesc(''); setInv([]); setError(''); onClose()
      void qc.invalidateQueries({ queryKey: ['departamentos'] }); void qc.invalidateQueries({ queryKey: ['deptos-stats'] })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'No se pudo crear el departamento'),
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!numero || Number.isNaN(Number(numero)) || Number(numero) <= 0) return setError('Ingresa un número de departamento válido')
    setError(''); crear.mutate()
  }
  return (
    <Modal open={open} title="Nuevo departamento" onClose={onClose}>
      <form onSubmit={submit} className="space-y-5">
        <Input label="Número *" inputMode="numeric" placeholder="Ej. 101" value={numero} onChange={e => { setError(''); setNumero(e.target.value.replace(/\D/g, '')) }} />
        <Input label="Descripción (opcional)" placeholder="Ej. Planta baja" value={desc} onChange={e => setDesc(e.target.value)} />
        <div className="space-y-1.5"><span className="block text-xs font-bold uppercase tracking-wide text-fg-2">Inventario base</span><InventarioEditor items={inv} onChange={setInv} /></div>
        <ShakeError error={error}>
          <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>
        </ShakeError>
        <div className="pt-2">
          <Button type="submit" full loading={crear.isPending}>Crear departamento</Button>
        </div>
      </form>
    </Modal>
  )
}

