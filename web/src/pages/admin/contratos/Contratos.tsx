import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, Download, Eye, FilePlus2, FileText, Pencil, Search, Star, Trash2, Upload, Users, X } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Card } from '../../../components/ui/Card'
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog'
import { EmptyState } from '../../../components/ui/EmptyState'
import { StaggerItem, StaggerList } from '../../../components/ui/MotionPrimitives'
import { PageHeader } from '../../../components/ui/PageHeader'
import { Segmented } from '../../../components/ui/Segmented'
import { Spinner } from '../../../components/ui/Spinner'
import { Stat } from '../../../components/ui/Stat'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { useAuth } from '../../../lib/auth'
import { abrirPdf, guardarBlob } from '../../../lib/file'
import { fecha, iniciales, money } from '../../../lib/format'
import type { Plantilla, ResumenContrato } from '../../../lib/types'
import { vencimiento } from '../Inquilinos'
import { AsignarPlantillaModal } from './AsignarPlantillaModal'
import { GenerarContratoModal } from './GenerarContratoModal'
import { nombreVisible } from '../../../lib/nombre'

type Tab = 'contratos' | 'plantillas'
type Filtro = 'todos' | 'vigentes' | 'porvencer' | 'vencidos'

function estadoVigencia(i: Pick<ResumenContrato, 'fecha_termino'>): Exclude<Filtro, 'todos'> {
  const v = vencimiento(i.fecha_termino)
  if (v?.expired) return 'vencidos'
  return v?.tone === 'warning' ? 'porvencer' : 'vigentes'
}

export default function Contratos() {
  const [params, setParams] = useSearchParams()
  const tab: Tab = params.get('tab') === 'plantillas' ? 'plantillas' : 'contratos'
  const { user } = useAuth()
  const esAdmin = user?.rol === 'admin'
  const navigate = useNavigate()

  return (
    <StaggerList className="space-y-6">
      <PageHeader title="Contratos"
        subtitle={tab === 'contratos' ? 'Genera el contrato de cada inquilino con sus datos del sistema' : 'Usa la plantilla del sistema o sube tu propio contrato en Word'}
        actions={tab === 'plantillas' && esAdmin && <Button icon={<Upload className="size-4" />} onClick={() => navigate('/contratos/plantillas/nueva')}>Subir mi contrato</Button>} />
      <StaggerItem>
        <Segmented value={tab} onChange={t => setParams(t === 'contratos' ? {} : { tab: t }, { replace: true })}
          options={[{ value: 'contratos', label: 'Contratos' }, { value: 'plantillas', label: 'Plantillas' }]} />
      </StaggerItem>
      {tab === 'contratos' ? <ListaContratos /> : <ListaPlantillas esAdmin={esAdmin} />}
    </StaggerList>
  )
}

// ── Contratos por inquilino ─────────────────────────────────────────────────────────────────
function ListaContratos() {
  const toast = useToast()
  const [filtro, setFiltro] = useState<Filtro>('todos')
  const [search, setSearch] = useState('')
  const [generar, setGenerar] = useState<string | null>(null)
  const qc = useQueryClient()
  const todos = useMutation({
    mutationFn: async () => (await api.generarPendientes()).data,
    onSuccess: r => {
      void qc.invalidateQueries({ queryKey: ['contratos-resumen'] })
      toast(r.fallos.length ? `${r.generados} generados, ${r.fallos.length} con error: ${r.fallos.map(f => f.nombre).join(', ')}` : `${r.generados} contratos generados`, r.fallos.length ? 'error' : 'success')
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudieron generar', 'error'),
  })
  const { data = [], isLoading } = useQuery({ queryKey: ['contratos-resumen'], queryFn: async () => (await api.getResumenContratos()).data })

  const conteo = useMemo(() => {
    const c = { vigentes: 0, porvencer: 0, vencidos: 0, sinContrato: 0 }
    for (const i of data) { c[estadoVigencia(i)]++; if (!i.contrato_id) c.sinContrato++ }
    return c
  }, [data])

  const filtrados = useMemo(() => {
    const s = search.trim().toLowerCase()
    return data.filter(i => (filtro === 'todos' || estadoVigencia(i) === filtro)
      && (!s || i.nombre_completo.toLowerCase().includes(s) || String(i.depto_numero).includes(s)))
  }, [data, filtro, search])

  const bajar = useMutation({
    mutationFn: async ({ i, formato, ver }: { i: ResumenContrato; formato: 'pdf' | 'docx'; ver?: boolean }) => {
      const blob = await api.descargarContrato(i.contrato_id!, formato)
      if (ver) abrirPdf(blob, `Contrato – ${nombreVisible(i.nombre_completo)}`)
      else guardarBlob(blob, `contrato_${i.depto_numero}_${i.nombre_completo.replace(/\s+/g, '_')}.${formato}`)
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo descargar', 'error'),
  })

  const chips: { v: Filtro; label: string; n: number }[] = [
    { v: 'todos', label: 'Todos', n: data.length }, { v: 'vigentes', label: 'Vigentes', n: conteo.vigentes },
    { v: 'porvencer', label: 'Por vencer', n: conteo.porvencer }, { v: 'vencidos', label: 'Vencidos', n: conteo.vencidos },
  ]

  return (
    <>
      {conteo.sinContrato > 0 && (
        <div className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-line bg-card p-4">
          <p className="min-w-0 text-sm text-fg-2"><b className="text-fg">{conteo.sinContrato}</b> inquilino(s) sin contrato. Se generan con la fecha en que entraron.</p>
          <Button loading={todos.isPending} icon={<FilePlus2 className="size-4" />} onClick={() => todos.mutate()}>Generar todos los pendientes</Button>
        </div>
      )}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <StaggerItem><Stat label="Contratos activos" numericValue={data.length} icon={<FileText />} /></StaggerItem>
        <StaggerItem><Stat label="Por vencer (30 días)" numericValue={conteo.porvencer} tone={conteo.porvencer ? 'text-warning' : ''} /></StaggerItem>
        <StaggerItem><Stat label="Vencidos" numericValue={conteo.vencidos} tone={conteo.vencidos ? 'text-danger' : ''} /></StaggerItem>
        <StaggerItem><Stat label="Sin contrato generado" numericValue={conteo.sinContrato} tone={conteo.sinContrato ? 'text-primary' : ''} sub="Aún no tienen el documento" /></StaggerItem>
      </div>

      <StaggerItem className="flex flex-wrap items-center gap-3">
        <Segmented value={filtro} onChange={setFiltro} options={chips.map(c => ({ value: c.v, label: `${c.label} ${c.n}` }))} />
        <div className="relative min-w-60 flex-1">
          <Search className="pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2 text-muted" />
          <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Buscar por nombre o depto…" aria-label="Buscar contratos"
            className="h-11 w-full rounded-xl border border-line bg-card pl-10 pr-9 text-sm placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25" />
          {search && <button onClick={() => setSearch('')} aria-label="Limpiar búsqueda" className="absolute right-3 top-1/2 -translate-y-1/2 text-muted hover:text-fg"><X className="size-4" /></button>}
        </div>
      </StaggerItem>

      {isLoading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}
      {!isLoading && filtrados.length === 0 && (
        <Card><EmptyState icon={FileText} title={data.length ? 'Sin resultados' : 'Aún no hay contratos'}>
          {data.length ? 'Prueba con otro filtro o búsqueda' : 'Cuando tengas inquilinos activos podrás generar sus contratos aquí.'}
        </EmptyState></Card>
      )}

      {filtrados.length > 0 && (
        <Card className="hidden overflow-hidden p-0 md:block">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-line bg-hover/50 text-[11px] font-bold uppercase tracking-wide text-muted">
              <tr><th className="px-5 py-3">Inquilino</th><th className="px-3 py-3">Vigencia</th><th className="px-3 py-3">Renta</th><th className="px-3 py-3">Contrato</th><th className="px-5 py-3 text-right">Acciones</th></tr>
            </thead>
            <tbody className="divide-y divide-line">
              {filtrados.map(i => {
                const v = vencimiento(i.fecha_termino)
                return (
                  <tr key={i.id} className="transition hover:bg-hover/40">
                    <td className="px-5 py-3">
                      <Link to={`/inquilinos/${i.id}`} className="flex items-center gap-3">
                        <span className="grid size-10 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-bold text-primary">{iniciales(i.nombre_completo)}</span>
                        <span className="min-w-0"><span className="block truncate font-semibold">{nombreVisible(i.nombre_completo)}</span><span className="text-xs text-muted">Depto {i.depto_numero}</span></span>
                      </Link>
                    </td>
                    <td className="px-3 py-3"><span className="block">{fecha(i.fecha_inicio)} – {fecha(i.fecha_termino)}</span>{v && <Badge tone={v.tone}>{v.label}</Badge>}</td>
                    <td className="px-3 py-3 font-bold">{money(i.renta)}</td>
                    <td className="px-3 py-3">{i.contrato_id
                      ? <span>{i.desactualizado ? <Badge tone="warning">Plantilla actualizada</Badge> : <Badge tone="success">Versión {i.version}</Badge>}<span className="mt-1 block text-xs text-muted">{i.plantilla_nombre} · {fecha(i.fecha_generacion)}</span></span>
                      : <Badge>Sin generar</Badge>}</td>
                    <td className="px-5 py-3">
                      <div className="flex items-center justify-end gap-1">
                        {i.contrato_id && <button onClick={() => bajar.mutate({ i, formato: 'docx' })} className="grid size-9 place-items-center rounded-lg text-fg-2 hover:bg-hover" aria-label="Descargar Word" title="Descargar Word"><Download className="size-[17px]" /></button>}
                        {i.contrato_id && !i.desactualizado && <button onClick={() => setGenerar(i.id)} className="grid size-9 place-items-center rounded-lg text-fg-2 hover:bg-hover" aria-label="Generar de nuevo" title="Generar de nuevo"><FilePlus2 className="size-[17px]" /></button>}
                        {i.contrato_id && i.desactualizado && <button onClick={() => bajar.mutate({ i, formato: 'pdf', ver: true })} className="grid size-9 place-items-center rounded-lg text-fg-2 hover:bg-hover" aria-label="Ver contrato anterior" title="Ver contrato anterior"><Eye className="size-[17px]" /></button>}
                        {i.contrato_id && !i.desactualizado
                          ? <Button className="h-9 px-3.5" icon={<Eye className="size-4" />} onClick={() => bajar.mutate({ i, formato: 'pdf', ver: true })}>Ver contrato</Button>
                          : <Button className="h-9 px-3.5" icon={<FilePlus2 className="size-4" />} onClick={() => setGenerar(i.id)}>Generar contrato</Button>}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </Card>
      )}

      <StaggerList className="space-y-3 md:hidden">
        {filtrados.map(i => {
          const v = vencimiento(i.fecha_termino)
          return (
            <StaggerItem key={i.id}>
              <Card className="space-y-3 p-4">
                <Link to={`/inquilinos/${i.id}`} className="flex items-center gap-3">
                  <span className="grid size-11 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-bold text-primary">{iniciales(i.nombre_completo)}</span>
                  <span className="min-w-0 flex-1"><span className="block truncate font-bold">{nombreVisible(i.nombre_completo)}</span>
                    <span className="text-xs text-muted">Depto {i.depto_numero} · {money(i.renta)} · hasta {fecha(i.fecha_termino)}</span></span>
                </Link>
                <div className="flex flex-wrap items-center gap-2">
                  {v && <Badge tone={v.tone}>{v.label}</Badge>}
                  {i.contrato_id ? (i.desactualizado ? <Badge tone="warning">Plantilla actualizada</Badge> : <Badge tone="success">Contrato v{i.version}</Badge>) : <Badge>Sin generar</Badge>}
                </div>
                <div className="flex gap-2">
                  {i.contrato_id && !i.desactualizado ? (
                    <>
                      <Button className="h-10 min-w-0 flex-1 px-3" icon={<Eye className="size-4" />} onClick={() => bajar.mutate({ i, formato: 'pdf', ver: true })}>Ver contrato</Button>
                      <Button variant="secondary" className="size-10 shrink-0 px-0" aria-label="Descargar Word" title="Descargar Word" onClick={() => bajar.mutate({ i, formato: 'docx' })}><Download className="size-4" /></Button>
                      <Button variant="secondary" className="size-10 shrink-0 px-0" aria-label="Generar de nuevo" title="Generar de nuevo" onClick={() => setGenerar(i.id)}><FilePlus2 className="size-4" /></Button>
                    </>
                  ) : (
                    <>
                      {i.contrato_id && <Button variant="secondary" className="h-10 flex-1 px-3" icon={<Eye className="size-4" />} onClick={() => bajar.mutate({ i, formato: 'pdf', ver: true })}>Anterior</Button>}
                      <Button className="h-10 flex-[2] px-3" icon={<FilePlus2 className="size-4" />} onClick={() => setGenerar(i.id)}>Generar contrato</Button>
                    </>
                  )}
                </div>
              </Card>
            </StaggerItem>
          )
        })}
      </StaggerList>

      <GenerarContratoModal inquilinoId={generar} onClose={() => setGenerar(null)} />
    </>
  )
}

// ── Plantillas ──────────────────────────────────────────────────────────────────────────────
function ListaPlantillas({ esAdmin }: { esAdmin: boolean }) {
  const qc = useQueryClient()
  const toast = useToast()
  const navigate = useNavigate()
  const [asignar, setAsignar] = useState<Plantilla | null>(null)
  const [borrar, setBorrar] = useState<Plantilla | null>(null)
  const { data = [], isLoading } = useQuery({ queryKey: ['plantillas'], queryFn: async () => (await api.getPlantillas()).data })
  const refrescar = () => void qc.invalidateQueries({ queryKey: ['plantillas'] })
  const error = (e: unknown) => toast(e instanceof Error ? e.message : 'No se pudo completar la acción', 'error')

  const predeterminar = useMutation({ mutationFn: (id: string) => api.hacerPredeterminada(id), onSuccess: () => { toast('Plantilla predeterminada actualizada', 'success'); refrescar() }, onError: error })
  const eliminar = useMutation({ mutationFn: (id: string) => api.eliminarPlantilla(id), onSuccess: () => { toast('Plantilla eliminada', 'success'); setBorrar(null); refrescar() }, onError: error })
  const preview = useMutation({ mutationFn: async (id: string) => abrirPdf(await api.previewPlantilla(id), `Vista previa – ${data.find(p => p.id === id)?.nombre ?? 'Plantilla'}`), onError: error })
  const docx = useMutation({
    mutationFn: async (p: Plantilla) => guardarBlob(await api.descargarPlantilla(p.id), `${p.nombre}.docx`),
    onError: error,
  })

  return (
    <>
      {esAdmin && (
        <StaggerItem>
          <Card className="flex flex-col gap-4 border-primary/25 bg-primary-soft/40 sm:flex-row sm:items-center">
            <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-primary text-white shadow-lg shadow-primary/30"><Upload className="size-6" /></span>
            <div className="flex-1">
              <p className="font-extrabold">¿Ya tienes tu propio contrato?</p>
              <p className="text-sm text-fg-2">Súbelo en Word: el sistema detecta dónde van los nombres, montos y fechas, y lo usa para generar los contratos de tus inquilinos con el mismo formato.</p>
            </div>
            <Button icon={<Upload className="size-4" />} onClick={() => navigate('/contratos/plantillas/nueva')}>Subir mi contrato</Button>
          </Card>
        </StaggerItem>
      )}

      {isLoading && <div className="grid place-items-center py-16"><Spinner className="size-8" /></div>}
      <StaggerList className="grid grid-cols-1 gap-4 lg:grid-cols-2 2xl:grid-cols-3">
        {data.map(p => (
          <StaggerItem key={p.id}>
            <Card className="flex h-full flex-col gap-4">
              <div className="flex items-start gap-3">
                <span className={`grid size-11 shrink-0 place-items-center rounded-xl ${p.predeterminada ? 'bg-primary text-white' : 'bg-hover text-fg-2'}`}>{p.predeterminada ? <Star className="size-5" /> : <FileText className="size-5" />}</span>
                <div className="min-w-0 flex-1">
                  <h3 className="truncate font-extrabold">{p.nombre}</h3>
                  <div className="mt-1 flex flex-wrap gap-1.5">
                    {p.sistema && <Badge tone="primary">Del sistema</Badge>}
                    {p.predeterminada && <Badge tone="success">Predeterminada</Badge>}
                    {p.estado === 'borrador' && <Badge tone="warning">Borrador</Badge>}
                    {p.propiedades > 0 && <Badge><Building2 className="mr-1 size-3" />{p.propiedades} {p.propiedades === 1 ? 'propiedad' : 'propiedades'}</Badge>}
                    {p.inquilinos > 0 && <Badge><Users className="mr-1 size-3" />{p.inquilinos}</Badge>}
                  </div>
                </div>
              </div>
              {p.descripcion && <p className="text-sm text-fg-2">{p.descripcion}</p>}
              <div className="mt-auto flex flex-wrap gap-2 border-t border-line pt-3">
                <Button variant="secondary" className="h-9 px-3" icon={<Eye className="size-4" />} loading={preview.isPending && preview.variables === p.id} onClick={() => preview.mutate(p.id)}>Vista previa</Button>
                <Button variant="secondary" className="h-9 px-3" icon={<Download className="size-4" />} onClick={() => docx.mutate(p)}>Word</Button>
                {esAdmin && !p.sistema && <Link to={`/contratos/plantillas/${p.id}`}><Button variant="secondary" className="h-9 px-3" icon={<Pencil className="size-4" />}>Editar datos</Button></Link>}
                {esAdmin && !p.sistema && p.estado === 'lista' && <Button variant="secondary" className="h-9 px-3" icon={<Building2 className="size-4" />} onClick={() => setAsignar(p)}>Propiedades</Button>}
                {esAdmin && !p.predeterminada && p.estado === 'lista' && <Button variant="ghost" className="h-9 px-3" icon={<Star className="size-4" />} loading={predeterminar.isPending && predeterminar.variables === p.id} onClick={() => predeterminar.mutate(p.id)}>Usar por defecto</Button>}
                {esAdmin && !p.sistema && <Button variant="ghost" className="h-9 px-3 text-danger" icon={<Trash2 className="size-4" />} onClick={() => setBorrar(p)} aria-label="Eliminar plantilla" />}
              </div>
            </Card>
          </StaggerItem>
        ))}
      </StaggerList>

      <AsignarPlantillaModal plantilla={asignar} onClose={() => setAsignar(null)} />
      <ConfirmDialog open={!!borrar} title="Eliminar plantilla" confirmLabel="Eliminar" loading={eliminar.isPending}
        onClose={() => setBorrar(null)} onConfirm={() => borrar && eliminar.mutate(borrar.id)}>
        <p>¿Eliminar <b className="text-fg">{borrar?.nombre}</b>? Los contratos ya generados se conservan; las propiedades que la usaban volverán a la plantilla predeterminada.</p>
      </ConfirmDialog>
    </>
  )
}
