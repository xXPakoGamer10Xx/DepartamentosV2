import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Download, Eye, FileText, Landmark, Sparkles, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../../components/ui/Badge'
import { Button } from '../../../components/ui/Button'
import { Input } from '../../../components/ui/Input'
import { Modal } from '../../../components/ui/Modal'
import { Select } from '../../../components/ui/Select'
import { Spinner } from '../../../components/ui/Spinner'
import { SuccessCheck } from '../../../components/ui/Celebration'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { abrirPdf, guardarBlob } from '../../../lib/file'
import { fechaHora } from '../../../lib/format'
import type { ContratoVersion, PrepararContrato } from '../../../lib/types'
import { nombreVisible } from '../../../lib/nombre'

const ORIGEN: Record<PrepararContrato['origen'], string> = {
  sistema: 'Plantilla del sistema',
  inquilino: 'Asignada a este inquilino',
  propiedad: 'Asignada a esta propiedad',
  predeterminada: 'Tu plantilla predeterminada',
}
const GUARDA: Record<string, string> = {
  inquilino: 'Se guardará en la ficha del inquilino.',
  propiedad: 'Se guardará en la propiedad.',
  config: 'Se guardará en tus datos de arrendador.',
}

/** Genera el contrato de un inquilino: elige plantilla, completa lo que falta y descarga PDF/Word. */
export function GenerarContratoModal({ inquilinoId, onClose }: { inquilinoId: string | null; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const abierto = !!inquilinoId
  const [plantillaId, setPlantillaId] = useState('')
  const [completar, setCompletar] = useState<Record<string, string>>({})
  const [extra, setExtra] = useState<Record<string, string>>({})
  const [hecho, setHecho] = useState<{ id: string; version: number; aproximado: boolean; plantilla_nombre: string } | null>(null)

  useEffect(() => { if (abierto) { setPlantillaId(''); setCompletar({}); setExtra({}); setHecho(null) } }, [abierto, inquilinoId])

  const prep = useQuery({
    queryKey: ['preparar-contrato', inquilinoId, plantillaId],
    queryFn: async () => (await api.prepararContrato(inquilinoId!, plantillaId || undefined)).data,
    enabled: abierto, staleTime: 0,
  })
  const d = prep.data
  const elegidaId = plantillaId || d?.plantilla_id || ''
  const elegida = d?.plantillas.find(p => p.id === elegidaId)

  const faltantes = useMemo(() => (d?.faltantes ?? []).filter(f => f.destino !== 'cuenta'), [d])
  const sinCuenta = useMemo(() => (d?.faltantes ?? []).filter(f => f.destino === 'cuenta'), [d])

  const generar = useMutation({
    mutationFn: async () => (await api.generarContrato(inquilinoId!, {
      plantilla_id: elegidaId,
      completar: Object.fromEntries(Object.entries(completar).filter(([, v]) => v.trim())),
      valores_extra: extra,
    })).data,
    onSuccess: r => {
      setHecho(r)
      void qc.invalidateQueries({ queryKey: ['contratos-resumen'] })
      void qc.invalidateQueries({ queryKey: ['contratos-historial'] })
      void qc.invalidateQueries({ queryKey: ['inquilino', inquilinoId] })
      void qc.invalidateQueries({ queryKey: ['preparar-contrato', inquilinoId] })
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo generar el contrato', 'error'),
  })

  const bajar = useMutation({
    mutationFn: async ({ id, formato, ver }: { id: string; formato: 'pdf' | 'docx'; ver?: boolean }) => {
      const blob = await api.descargarContrato(id, formato)
      if (ver) abrirPdf(blob, `Contrato – ${d?.inquilino.nombre_completo ?? ''}`)
      else guardarBlob(blob, `contrato_${d?.inquilino.depto_numero ?? ''}_${(d?.inquilino.nombre_completo ?? '').replace(/\s+/g, '_')}.${formato}`)
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo descargar', 'error'),
  })

  const borrar = useMutation({
    mutationFn: (id: string) => api.eliminarContrato(id),
    onSuccess: () => {
      toast('Contrato eliminado', 'success')
      for (const k of ['contratos-resumen', 'contratos-historial', 'preparar-contrato']) void qc.invalidateQueries({ queryKey: [k] })
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo eliminar', 'error'),
  })

  const faltanRequeridos = (elegida?.campos ?? []).some(c => c.requerido && !(extra[c.key] ?? '').trim())

  return (
    <Modal open={abierto} title={hecho ? 'Contrato listo' : 'Generar contrato'} onClose={onClose}>
      {prep.isLoading && <div className="grid place-items-center py-12"><Spinner className="size-7" /></div>}
      {prep.isError && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{prep.error instanceof Error ? prep.error.message : 'No se pudo preparar el contrato'}</p>}

      {d && hecho && (
        <div className="space-y-5">
          <div className="flex flex-col items-center gap-3 py-2 text-center">
            <SuccessCheck />
            <div>
              <p className="font-extrabold">{nombreVisible(d.inquilino.nombre_completo)}</p>
              <p className="text-sm text-fg-2">Depto {d.inquilino.depto_numero} · versión {hecho.version} · {hecho.plantilla_nombre}</p>
            </div>
          </div>
          {hecho.aproximado && <Aviso>El servidor no tiene LibreOffice instalado: este PDF es una aproximación (sin tablas ni imágenes). El Word sí queda completo.</Aviso>}
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
            <Button icon={<Eye className="size-4" />} loading={bajar.isPending && bajar.variables?.ver} onClick={() => bajar.mutate({ id: hecho.id, formato: 'pdf', ver: true })}>Ver PDF</Button>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={() => bajar.mutate({ id: hecho.id, formato: 'pdf' })}>PDF</Button>
            <Button variant="secondary" icon={<Download className="size-4" />} onClick={() => bajar.mutate({ id: hecho.id, formato: 'docx' })}>Word</Button>
          </div>
          <Button variant="ghost" full onClick={onClose}>Cerrar</Button>
        </div>
      )}

      {d && !hecho && (
        <div className="space-y-5">
          <div className="rounded-xl bg-hover px-4 py-3">
            <p className="font-bold">{nombreVisible(d.inquilino.nombre_completo)}</p>
            <p className="text-sm text-fg-2">Departamento {d.inquilino.depto_numero}</p>
          </div>

          <div className="space-y-1.5">
            <Select label="Plantilla" value={elegidaId} onChange={e => setPlantillaId(e.target.value)}>
              {d.plantillas.map(p => <option key={p.id} value={p.id}>{p.nombre}</option>)}
            </Select>
            {elegidaId === d.plantilla_id && <p className="flex items-center gap-1.5 text-xs text-muted"><Sparkles className="size-3.5" />Sugerida: {ORIGEN[d.origen]}</p>}
          </div>

          {faltantes.length > 0 && (
            <section className="space-y-3 rounded-2xl border border-warning/40 bg-warning-soft/40 p-4">
              <div>
                <p className="flex items-center gap-2 text-sm font-bold"><AlertTriangle className="size-4 text-warning" />Faltan datos para esta plantilla</p>
                <p className="text-xs text-fg-2">Complétalos aquí y se guardan para los próximos contratos. Puedes dejarlos vacíos y generar igual.</p>
              </div>
              {faltantes.map(f => (
                <Input key={f.key} label={f.etiqueta} hint={GUARDA[f.destino]} type={f.key.startsWith('fecha_') ? 'date' : 'text'} value={completar[f.key] ?? ''}
                  onChange={e => setCompletar(c => ({ ...c, [f.key]: e.target.value }))} />
              ))}
            </section>
          )}

          {sinCuenta.length > 0 && (
            <Aviso>
              Esta plantilla incluye datos bancarios ({sinCuenta.map(s => s.etiqueta).join(', ')}) y aún no tienes una cuenta registrada.{' '}
              <Link to="/cuentas" onClick={onClose} className="inline-flex items-center gap-1 font-semibold text-primary hover:underline"><Landmark className="size-3.5" />Agregar cuenta bancaria</Link>
            </Aviso>
          )}

          {!!elegida?.campos.length && (
            <section className="space-y-3">
              <p className="text-xs font-bold uppercase tracking-wide text-fg-2">Datos de este contrato</p>
              {elegida.campos.map(c => (
                <Input key={c.key} label={c.etiqueta + (c.requerido ? ' *' : '')} type={c.tipo === 'fecha' ? 'date' : c.tipo === 'numero' ? 'number' : 'text'}
                  value={extra[c.key] ?? ''} onChange={e => setExtra(x => ({ ...x, [c.key]: e.target.value }))} />
              ))}
            </section>
          )}

          {d.motor === 'aproximado' && <Aviso>El servidor no tiene LibreOffice instalado: el PDF saldrá aproximado (sin tablas ni imágenes). El Word sí queda completo.</Aviso>}

          {d.versiones.length > 0 && <Versiones versiones={d.versiones} borrando={borrar.isPending ? borrar.variables : null} onBorrar={id => borrar.mutate(id)} onBajar={(id, formato, ver) => bajar.mutate({ id, formato, ver })} />}

          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button variant="ghost" onClick={onClose}>Cancelar</Button>
            <Button icon={<FileText className="size-4" />} loading={generar.isPending} disabled={faltanRequeridos || prep.isFetching} onClick={() => generar.mutate()}>
              {d.versiones.length ? `Generar versión ${d.versiones[0].version + 1}` : 'Generar contrato'}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  )
}

function Aviso({ children }: { children: React.ReactNode }) {
  return <p className="flex items-start gap-2 rounded-xl bg-warning-soft px-4 py-3 text-xs leading-relaxed text-fg"><AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" /><span>{children}</span></p>
}

function Versiones({ versiones, onBajar, onBorrar, borrando }: {
  versiones: ContratoVersion[]; onBajar: (id: string, f: 'pdf' | 'docx', ver?: boolean) => void; onBorrar: (id: string) => void; borrando: string | null
}) {
  const [confirmar, setConfirmar] = useState<string | null>(null)
  return (
    <details className="rounded-xl border border-line">
      <summary className="cursor-pointer px-4 py-3 text-sm font-semibold">Contratos anteriores ({versiones.length})</summary>
      <ul className="divide-y divide-line border-t border-line">
        {versiones.map(v => (
          <li key={v.id} className="flex items-center justify-between gap-3 px-4 py-2.5 text-sm">
            <span className="min-w-0"><b>Versión {v.version}</b> <Badge>{v.plantilla_nombre ?? 'Plantilla'}</Badge><span className="block text-xs text-muted">{fechaHora(v.fecha_generacion)}</span></span>
            {confirmar === v.id ? (
              <span className="flex shrink-0 items-center gap-1">
                <span className="text-xs font-semibold text-danger">¿Eliminar?</span>
                <button onClick={() => { onBorrar(v.id); setConfirmar(null) }} disabled={borrando === v.id} className="rounded-lg bg-danger px-2.5 py-1.5 text-xs font-semibold text-white">Sí</button>
                <button onClick={() => setConfirmar(null)} className="rounded-lg px-2.5 py-1.5 text-xs font-semibold text-fg-2 hover:bg-hover">No</button>
              </span>
            ) : (
              <span className="flex shrink-0 items-center gap-1">
                <button onClick={() => onBajar(v.id, 'pdf', true)} className="rounded-lg px-2.5 py-1.5 text-xs font-semibold text-primary hover:bg-primary-soft">PDF</button>
                <button onClick={() => onBajar(v.id, 'docx')} className="rounded-lg px-2.5 py-1.5 text-xs font-semibold text-primary hover:bg-primary-soft">Word</button>
                <button onClick={() => setConfirmar(v.id)} aria-label={`Eliminar versión ${v.version}`} title="Eliminar" className="grid size-8 place-items-center rounded-lg text-muted hover:bg-danger-soft hover:text-danger"><Trash2 className="size-4" /></button>
              </span>
            )}
          </li>
        ))}
      </ul>
    </details>
  )
}
