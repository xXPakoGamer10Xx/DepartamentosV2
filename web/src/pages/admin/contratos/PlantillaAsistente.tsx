import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Check, CheckCircle2, ChevronDown, FileUp, Loader2, Plus, Sparkles, X } from 'lucide-react'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Button } from '../../../components/ui/Button'
import { Card } from '../../../components/ui/Card'
import { Input } from '../../../components/ui/Input'
import { Modal } from '../../../components/ui/Modal'
import { PageHeader } from '../../../components/ui/PageHeader'
import { Select } from '../../../components/ui/Select'
import { Spinner } from '../../../components/ui/Spinner'
import { useToast } from '../../../components/ui/Toast'
import { api } from '../../../lib/api'
import { leerDocx, pickDocx } from '../../../lib/file'
import type { CampoPlantilla, Marca, VariableContrato } from '../../../lib/types'

type Paso = 'subir' | 'analizando' | 'revisar' | 'probar'
const PASOS: { id: Paso; label: string }[] = [
  { id: 'subir', label: 'Subir' }, { id: 'analizando', label: 'Analizar' }, { id: 'revisar', label: 'Revisar' }, { id: 'probar', label: 'Probar y guardar' },
]
const NUEVO_CAMPO = '__nuevo__'

interface MarcaUI extends Marca { texto: string; confianza?: number }
interface Seleccion { p: string; ini: number; fin: number; texto: string }

const slug = (s: string) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 30) || 'dato'

/** Envuelve en <mark> cada rango del texto de su párrafo. Los offsets son de texto plano del párrafo. */
function pintarMarcas(root: HTMLElement, marcas: MarcaUI[], etiqueta: (key: string) => string) {
  marcas.forEach((m, i) => {
    const p = root.querySelector<HTMLElement>(`[data-p="${m.p}"]`)
    if (!p) return
    const walker = document.createTreeWalker(p, NodeFilter.SHOW_TEXT)
    const nodes: { node: Text; start: number }[] = []
    let pos = 0
    while (walker.nextNode()) { const n = walker.currentNode as Text; nodes.push({ node: n, start: pos }); pos += n.length }
    const envolver: { node: Text; a: number; b: number }[] = []
    for (const { node, start } of nodes) {
      const a = Math.max(m.ini, start), b = Math.min(m.fin, start + node.length)
      if (a < b) envolver.push({ node, a: a - start, b: b - start })
    }
    envolver.forEach(({ node, a, b }, k) => {
      const range = document.createRange(); range.setStart(node, a); range.setEnd(node, b)
      const mark = document.createElement('mark')
      mark.dataset.i = String(i)
      if (m.key.startsWith('campo_')) mark.className = 'pers'
      mark.title = etiqueta(m.key)
      if (k === envolver.length - 1) mark.dataset.label = etiqueta(m.key)
      range.surroundContents(mark)
    })
  })
}

export default function PlantillaAsistente() {
  const { id: idParam } = useParams()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()
  const editando = !!idParam

  const [paso, setPaso] = useState<Paso>(editando ? 'analizando' : 'subir')
  const [nombre, setNombre] = useState('')
  const [id, setId] = useState<string | null>(idParam ?? null)
  const [html, setHtml] = useState('')
  const [marcas, setMarcas] = useState<MarcaUI[]>([])
  const [campos, setCampos] = useState<CampoPlantilla[]>([])
  const [presentes, setPresentes] = useState<string[]>([])
  const [iaError, setIaError] = useState<string | null>(null)
  const [avisoPdf, setAvisoPdf] = useState<{ pdf: boolean; texto: string | null }>({ pdf: false, texto: null })
  const [sel, setSel] = useState<Seleccion | null>(null)
  const [activa, setActiva] = useState<number | null>(null)
  const [nuevoCampo, setNuevoCampo] = useState<{ aplicar: (key: string) => void } | null>(null)
  const [drag, setDrag] = useState(false)
  const [preview, setPreview] = useState<string | null>(null)
  const [inquilinoId, setInquilinoId] = useState('')
  const [predeterminada, setPredeterminada] = useState(false)
  const docRef = useRef<HTMLDivElement | null>(null)

  const { data: cat } = useQuery({ queryKey: ['variables-contrato'], queryFn: async () => (await api.getVariablesContrato()).data, staleTime: 5 * 60_000 })
  const variables = cat?.variables ?? []
  const grupos = useMemo(() => {
    const g = new Map<string, VariableContrato[]>()
    for (const v of variables) g.set(v.grupo, [...(g.get(v.grupo) ?? []), v])
    return [...g.entries()]
  }, [variables])
  const etiqueta = useCallback((key: string) =>
    variables.find(v => v.key === key)?.etiqueta ?? campos.find(c => c.key === key)?.etiqueta ?? key, [variables, campos])

  // Edición de una plantilla existente
  useEffect(() => {
    if (!idParam) return
    void api.getPlantilla(idParam).then(r => {
      const d = r.data
      if (d.sistema) { navigate('/contratos?tab=plantillas', { replace: true }); return }
      setNombre(d.nombre); setHtml(d.html); setCampos(d.campos); setPresentes(d.variables_presentes ?? [])
      setMarcas(d.marcas.map(m => ({ ...m, texto: m.texto ?? '' })))
      setPredeterminada(!!d.predeterminada)
      setPaso('revisar')
    }).catch(e => { toast(e instanceof Error ? e.message : 'No se encontró la plantilla', 'error'); navigate('/contratos?tab=plantillas', { replace: true }) })
  }, [idParam, navigate, toast])

  // ── Subir ──
  const subir = useMutation({
    mutationFn: async (f: { nombre: string; dataUrl: string }) => (await api.subirPlantilla(nombre.trim() || f.nombre, f.dataUrl)).data,
    onMutate: () => setPaso('analizando'),
    onSuccess: d => {
      setId(d.id); setNombre(d.nombre); setHtml(d.html); setPresentes(d.variables_presentes); setIaError(d.ia_error)
      setAvisoPdf({ pdf: d.desde_pdf, texto: d.aviso_conversion })
      setMarcas(d.sugerencias.map(s => ({ p: s.p, ini: s.ini, fin: s.fin, key: s.key, texto: s.texto, confianza: s.confianza, borrar: s.borrar })))
      const nuevos: CampoPlantilla[] = []
      for (const s of d.sugerencias as (typeof d.sugerencias[number] & { campo?: CampoPlantilla | null })[]) if (s.campo) nuevos.push(s.campo)
      setCampos(nuevos)
      setPaso('revisar')
      void qc.invalidateQueries({ queryKey: ['plantillas'] })
    },
    onError: e => { toast(e instanceof Error ? e.message : 'No se pudo subir el archivo', 'error'); setPaso('subir') },
  })
  const elegirArchivo = async (file?: File) => {
    try {
      const f = file ? await leerDocx(file) : await pickDocx()
      if (f) subir.mutate(f)
    } catch (e) { toast(e instanceof Error ? e.message : 'No se pudo leer el archivo', 'error') }
  }

  // ── Documento: pintar marcas y reaccionar a la selección ──
  useLayoutEffect(() => {
    const root = docRef.current
    if (!root || paso !== 'revisar') return
    root.innerHTML = html
    root.querySelectorAll<HTMLElement>('mark.auto').forEach(m => { m.dataset.label = etiqueta(m.dataset.var ?? ''); m.title = 'Este dato ya viene en tu Word' })
    pintarMarcas(root, marcas, etiqueta)
    // Párrafos que desaparecen porque otra marca los reemplaza (resto de una lista de inventario)
    marcas.forEach(m => m.borrar?.forEach(id => root.querySelector<HTMLElement>(`[data-p="${id}"]`)?.classList.add('borrado')))
    if (activa !== null) root.querySelectorAll(`mark[data-i="${activa}"]`).forEach(m => m.classList.add('activa'))
  }, [html, marcas, paso, etiqueta, activa])

  useEffect(() => {
    if (paso !== 'revisar') return
    const onSel = () => {
      const root = docRef.current; const s = window.getSelection()
      if (!root || !s || s.rangeCount === 0 || s.isCollapsed) { setSel(null); return }
      const r = s.getRangeAt(0)
      if (!root.contains(r.commonAncestorContainer)) { setSel(null); return }
      const pa = (r.startContainer.nodeType === 3 ? r.startContainer.parentElement : r.startContainer as HTMLElement)?.closest<HTMLElement>('[data-p]')
      const pb = (r.endContainer.nodeType === 3 ? r.endContainer.parentElement : r.endContainer as HTMLElement)?.closest<HTMLElement>('[data-p]')
      if (!pa || pa !== pb) { setSel(null); return }
      if ([...pa.querySelectorAll('mark')].some(m => r.intersectsNode(m))) { setSel(null); return }
      const pre = document.createRange(); pre.selectNodeContents(pa); pre.setEnd(r.startContainer, r.startOffset)
      const texto = r.toString()
      const lead = texto.length - texto.trimStart().length
      const t = texto.trim()
      if (!t) { setSel(null); return }
      const ini = pre.toString().length + lead
      setSel({ p: pa.dataset.p!, ini, fin: ini + t.length, texto: t })
    }
    document.addEventListener('selectionchange', onSel)
    return () => document.removeEventListener('selectionchange', onSel)
  }, [paso])

  const onDocClick = (e: React.MouseEvent) => {
    const m = (e.target as HTMLElement).closest<HTMLElement>('mark[data-i]')
    if (m) { setActiva(Number(m.dataset.i)); document.getElementById(`marca-${m.dataset.i}`)?.scrollIntoView({ block: 'center', behavior: 'smooth' }) }
  }

  const agregarMarca = (key: string, s: Seleccion) => {
    setMarcas(ms => [...ms, { p: s.p, ini: s.ini, fin: s.fin, key, texto: s.texto }])
    window.getSelection()?.removeAllRanges(); setSel(null)
  }
  const pedirVariable = (valor: string, aplicar: (key: string) => void) => {
    if (valor === NUEVO_CAMPO) setNuevoCampo({ aplicar })
    else if (valor) aplicar(valor)
  }
  const crearCampo = (et: string, tipo: CampoPlantilla['tipo']) => {
    let key = `campo_${slug(et)}`; let n = 2
    while (campos.some(c => c.key === key)) key = `campo_${slug(et)}_${n++}`
    setCampos(cs => [...cs, { key, etiqueta: et.trim(), tipo, requerido: false }])
    nuevoCampo?.aplicar(key); setNuevoCampo(null)
  }
  const cambiarMarca = (i: number, key: string) => setMarcas(ms => ms.map((m, k) => (k === i ? { ...m, key } : m)))
  const quitarMarca = (i: number) => { setMarcas(ms => ms.filter((_, k) => k !== i)); setActiva(null) }

  // Los campos que ninguna marca usa se descartan al guardar
  const camposUsados = useMemo(() => campos.filter(c => marcas.some(m => m.key === c.key)), [campos, marcas])

  const guardar = useMutation({
    mutationFn: (d: { estado: 'borrador' | 'lista'; predeterminada?: boolean }) =>
      api.guardarPlantilla(id!, { nombre: nombre.trim() || 'Mi contrato', estado: d.estado, campos: camposUsados, marcas: marcas.map(({ p, ini, fin, key, borrar }) => ({ p, ini, fin, key, borrar: borrar ?? undefined })), predeterminada: d.predeterminada }),
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo guardar', 'error'),
  })

  const inquilinos = useQuery({ queryKey: ['inquilinos', 'activo'], queryFn: async () => (await api.getInquilinos({ estado: 'activo' })).data, enabled: paso === 'probar' })
  const probar = useMutation({
    mutationFn: async () => {
      const blob = await api.previewPlantilla(id!, inquilinoId ? { inquilino_id: inquilinoId } : {})
      setPreview(prev => { if (prev) URL.revokeObjectURL(prev); return URL.createObjectURL(new Blob([blob], { type: 'application/pdf' })) })
      return blob
    },
    onError: e => toast(e instanceof Error ? e.message : 'No se pudo generar la vista previa', 'error'),
  })

  const irAProbar = async () => {
    await guardar.mutateAsync({ estado: 'borrador' })
    setPaso('probar'); probar.mutate()
  }
  const terminar = async () => {
    await guardar.mutateAsync({ estado: 'lista', predeterminada: predeterminada || undefined })
    void qc.invalidateQueries({ queryKey: ['plantillas'] })
    toast('Plantilla guardada', 'success')
    navigate('/contratos?tab=plantillas', { replace: true })
  }

  const pasoIdx = PASOS.findIndex(p => p.id === paso)

  return (
    <div className="space-y-6">
      <PageHeader title={editando ? 'Editar datos de la plantilla' : 'Subir mi contrato'} back="/contratos?tab=plantillas"
        subtitle={editando ? nombre : 'Sube tu Word y el sistema detecta dónde van los datos'} />

      <ol className="flex flex-wrap items-center gap-2 text-xs font-bold" aria-label="Progreso">
        {PASOS.map((p, i) => (
          <li key={p.id} className={`flex items-center gap-2 rounded-full px-3 py-1.5 ${i === pasoIdx ? 'bg-primary text-white' : i < pasoIdx ? 'bg-success-soft text-success' : 'bg-hover text-muted'}`}>
            {i < pasoIdx ? <Check className="size-3.5" /> : <span>{i + 1}</span>}{p.label}
          </li>
        ))}
      </ol>

      {/* 1. Subir */}
      {paso === 'subir' && (
        <Card className="mx-auto max-w-2xl space-y-5">
          <Input label="Nombre de la plantilla" hint="Para reconocerla al generar contratos. Ej.: Contrato departamento amueblado." value={nombre} onChange={e => setNombre(e.target.value)} placeholder="Mi contrato" />
          <div
            onDragOver={e => { e.preventDefault(); setDrag(true) }} onDragLeave={() => setDrag(false)}
            onDrop={e => { e.preventDefault(); setDrag(false); void elegirArchivo(e.dataTransfer.files[0]) }}
            className={`grid place-items-center gap-3 rounded-2xl border-2 border-dashed px-6 py-12 text-center transition ${drag ? 'border-primary bg-primary-soft' : 'border-line-strong'}`}>
            <span className="grid size-14 place-items-center rounded-2xl bg-primary-soft text-primary"><FileUp className="size-7" /></span>
            <div>
              <p className="font-extrabold">Arrastra tu contrato aquí</p>
              <p className="text-sm text-fg-2">Word (.docx) o PDF de hasta 5 MB</p>
            </div>
            <Button onClick={() => void elegirArchivo()} icon={<FileUp className="size-4" />}>Elegir archivo</Button>
          </div>
          <ul className="space-y-1.5 text-sm text-fg-2">
            <li className="flex gap-2"><Sparkles className="mt-0.5 size-4 shrink-0 text-primary" />No cambiamos tu texto: solo ubicamos nombres, montos, fechas y espacios en blanco.</li>
            <li className="flex gap-2"><Sparkles className="mt-0.5 size-4 shrink-0 text-primary" />Después revisas lo detectado y corriges lo que haga falta.</li>
            <li className="flex gap-2"><Sparkles className="mt-0.5 size-4 shrink-0 text-primary" />¿Solo tienes el PDF? También lo aceptamos: lo convertimos a Word con IA y te avisamos si algo no coincide. Con un Word el formato es idéntico.</li>
            <li className="flex gap-2"><Sparkles className="mt-0.5 size-4 shrink-0 text-primary" />¿Tu Word ya tiene <code className="rounded bg-hover px-1">{'{{nombre_completo}}'}</code>? Se detecta solo, sin IA.</li>
          </ul>
        </Card>
      )}

      {/* 2. Analizando */}
      {paso === 'analizando' && (
        <Card className="mx-auto grid max-w-2xl place-items-center gap-3 py-16 text-center">
          <Loader2 className="size-10 animate-spin text-primary" />
          <p className="font-extrabold">{editando ? 'Cargando tu plantilla…' : 'Analizando tu contrato…'}</p>
          {!editando && <p className="text-sm text-fg-2">Estamos buscando los datos que cambian de un contrato a otro. Si subiste un PDF, primero lo convertimos a Word: puede tardar hasta un minuto.</p>}
        </Card>
      )}

      {/* 3. Revisar */}
      {paso === 'revisar' && (
        <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_380px]">
          <Card className="space-y-3 p-0">
            <div className="sticky top-0 z-10 rounded-t-2xl border-b border-line bg-card/95 px-5 py-3 backdrop-blur">
              {sel ? (
                <div className="flex flex-wrap items-center gap-3">
                  <p className="min-w-0 flex-1 text-sm"><span className="text-fg-2">Seleccionaste: </span><b className="break-words">“{sel.texto.length > 60 ? sel.texto.slice(0, 60) + '…' : sel.texto}”</b></p>
                  <VariableSelect full={false} valor="" variables={grupos} campos={campos} placeholder="¿Qué dato es?" onChange={v => pedirVariable(v, k => agregarMarca(k, sel))} />
                </div>
              ) : (
                <p className="text-sm text-fg-2"><b className="text-fg">Revisa tu contrato.</b> Lo resaltado es un dato que se rellenará solo. ¿Falta alguno? <b className="text-fg">Selecciona el texto</b> y elige qué dato es.</p>
              )}
            </div>
            <div ref={docRef} onClick={onDocClick} className="docrev px-6 pb-8" />
          </Card>

          <aside className="space-y-4 lg:sticky lg:top-4">
            {avisoPdf.pdf && <p className="flex gap-2 rounded-xl bg-primary-soft px-4 py-3 text-xs"><Sparkles className="mt-0.5 size-4 shrink-0 text-primary" />Convertimos tu PDF a Word. Compara el texto con tu original antes de continuar.</p>}
            {avisoPdf.texto && <p className="flex gap-2 rounded-xl bg-warning-soft px-4 py-3 text-xs"><AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" />{avisoPdf.texto}</p>}
            {iaError && <p className="flex gap-2 rounded-xl bg-warning-soft px-4 py-3 text-xs"><AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" />{iaError}</p>}
            <Card className="space-y-3 p-4">
              <h2 className="flex items-center justify-between text-xs font-bold uppercase tracking-wide text-fg-2">Datos detectados <span className="rounded-full bg-primary-soft px-2 py-0.5 text-primary">{marcas.length}</span></h2>
              {marcas.length === 0 && <p className="text-sm text-muted">Aún no hay datos marcados. Selecciona texto en el documento para empezar.</p>}
              <ul className="custom-scrollbar max-h-[52vh] space-y-2.5 overflow-y-auto pr-1">
                {marcas.map((m, i) => (
                  <li id={`marca-${i}`} key={`${m.p}-${m.ini}`} onClick={() => setActiva(i)}
                    className={`space-y-2 rounded-xl border p-3.5 transition ${activa === i ? 'border-primary bg-primary-soft/50' : 'border-line hover:border-line-strong'}`}>
                    <div className="flex items-center justify-between gap-2">
                      <p className="min-w-0 flex-1 truncate text-sm font-semibold text-fg" title={m.texto}>“{m.texto}”</p>
                      <button onClick={e => { e.stopPropagation(); quitarMarca(i) }} aria-label="Quitar" className="shrink-0 rounded-lg p-1 text-muted transition hover:bg-hover hover:text-danger"><X className="size-4" /></button>
                    </div>
                    <VariableSelect valor={m.key} variables={grupos} campos={campos} onChange={v => pedirVariable(v, k => cambiarMarca(i, k))} />
                    {!!m.borrar?.length && <p className="text-[11px] text-muted">Reemplaza también las {m.borrar.length} líneas siguientes (la lista completa se imprime aquí).</p>}
                    {m.confianza !== undefined && m.confianza < 0.75 && <p className="flex items-center gap-1 text-[11px] font-semibold text-warning"><AlertTriangle className="size-3" />La IA no está segura: revísalo</p>}
                  </li>
                ))}
              </ul>
              {presentes.length > 0 && (
                <div className="border-t border-line pt-3">
                  <p className="mb-1.5 flex items-center gap-1.5 text-xs font-bold text-success"><CheckCircle2 className="size-3.5" />Ya vienen en tu Word</p>
                  <div className="flex flex-wrap gap-1.5">{presentes.map(k => <span key={k} className="rounded-full bg-success-soft px-2 py-0.5 text-[11px] font-semibold text-success">{etiqueta(k)}</span>)}</div>
                </div>
              )}
            </Card>

            {camposUsados.length > 0 && (
              <Card className="space-y-3 p-4">
                <h2 className="text-xs font-bold uppercase tracking-wide text-fg-2">Datos personalizados</h2>
                <p className="text-xs text-muted">Se te preguntarán al generar cada contrato.</p>
                {camposUsados.map(c => (
                  <div key={c.key} className="space-y-2 rounded-xl border border-line p-3.5">
                    <input value={c.etiqueta} aria-label="Nombre del dato" onChange={e => setCampos(cs => cs.map(x => x.key === c.key ? { ...x, etiqueta: e.target.value } : x))}
                      className="h-10 w-full rounded-xl border border-line bg-card px-3.5 text-sm font-semibold text-fg transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25" />
                    <div className="flex items-center gap-3">
                      <div className="relative flex-1">
                        <select value={c.tipo} aria-label="Tipo de dato" onChange={e => setCampos(cs => cs.map(x => x.key === c.key ? { ...x, tipo: e.target.value as CampoPlantilla['tipo'] } : x))}
                          className="h-10 w-full cursor-pointer appearance-none rounded-xl border border-line bg-card pl-3.5 pr-9 text-sm font-semibold text-fg transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25">
                          <option value="texto">Texto</option><option value="numero">Número</option><option value="fecha">Fecha</option>
                        </select>
                        <ChevronDown className="pointer-events-none absolute right-3 top-1/2 size-4 -translate-y-1/2 text-fg-2" />
                      </div>
                      <label className="flex shrink-0 cursor-pointer items-center gap-1.5 text-xs font-medium text-fg-2"><input type="checkbox" checked={c.requerido} className="size-4 accent-[var(--primary)]"
                        onChange={e => setCampos(cs => cs.map(x => x.key === c.key ? { ...x, requerido: e.target.checked } : x))} />Obligatorio</label>
                    </div>
                  </div>
                ))}
              </Card>
            )}

            <div className="flex gap-2">
              <Button variant="ghost" className="flex-1" onClick={() => navigate('/contratos?tab=plantillas')}>Cancelar</Button>
              <Button className="flex-[2]" loading={guardar.isPending} onClick={() => void irAProbar()}>Continuar</Button>
            </div>
          </aside>
        </div>
      )}

      {/* 4. Probar y guardar */}
      {paso === 'probar' && (
        <div className="grid items-start gap-5 lg:grid-cols-[360px_minmax(0,1fr)]">
          <Card className="space-y-4">
            <Input label="Nombre" value={nombre} onChange={e => setNombre(e.target.value)} />
            <Select label="Probar con los datos de" value={inquilinoId} onChange={e => setInquilinoId(e.target.value)}
              hint="Elige un inquilino real para ver el contrato tal como saldrá.">
              <option value="">Datos de ejemplo</option>
              {(inquilinos.data ?? []).map(i => <option key={i.id} value={i.id}>{i.nombre_completo} — Depto {i.depto_numero}</option>)}
            </Select>
            <Button variant="secondary" full loading={probar.isPending} onClick={() => probar.mutate()}>Actualizar vista previa</Button>
            <label className="flex cursor-pointer items-start gap-3 rounded-xl border border-line p-3 has-[:checked]:border-primary has-[:checked]:bg-primary-soft/50">
              <input type="checkbox" checked={predeterminada} onChange={e => setPredeterminada(e.target.checked)} className="mt-0.5 size-4 accent-[var(--primary)]" />
              <span><span className="block text-sm font-bold">Usar como mi plantilla predeterminada</span><span className="text-xs text-muted">Se aplicará a los contratos nuevos, salvo las propiedades que tengan otra asignada.</span></span>
            </label>
            <div className="flex flex-col-reverse gap-2 sm:flex-row">
              <Button variant="ghost" onClick={() => setPaso('revisar')}>Volver a revisar</Button>
              <Button className="flex-1" icon={<Check className="size-4" />} loading={guardar.isPending} onClick={() => void terminar()}>Guardar plantilla</Button>
            </div>
          </Card>
          <Card className="overflow-hidden p-0">
            {probar.isPending && !preview && <div className="grid h-[70vh] place-items-center"><Spinner className="size-8" /></div>}
            {preview && (
              <>
                <iframe title="Vista previa del contrato" src={preview} className="hidden h-[78vh] w-full md:block" />
                <div className="grid place-items-center gap-3 p-8 text-center md:hidden">
                  <p className="text-sm text-fg-2">La vista previa se abre como PDF.</p>
                  <a href={preview} target="_blank" rel="noreferrer"><Button>Abrir vista previa</Button></a>
                </div>
              </>
            )}
          </Card>
        </div>
      )}

      <Modal open={!!nuevoCampo} title="Dato personalizado" onClose={() => setNuevoCampo(null)}>
        <NuevoCampoForm onCrear={crearCampo} />
      </Modal>
    </div>
  )
}

function VariableSelect({ valor, variables, campos, onChange, placeholder, full = true }: {
  valor: string; variables: [string, VariableContrato[]][]; campos: CampoPlantilla[]; onChange: (v: string) => void; placeholder?: string; full?: boolean
}) {
  return (
    <div className={full ? 'relative w-full' : 'relative w-full sm:w-64'}>
      <select value={valor} aria-label="Dato que se rellenará" onChange={e => onChange(e.target.value)}
        className="h-10 w-full cursor-pointer appearance-none truncate rounded-xl border border-line bg-card pl-3.5 pr-9 text-sm font-semibold text-fg transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25">
        {placeholder && <option value="">{placeholder}</option>}
        {variables.map(([g, vs]) => <optgroup key={g} label={g}>{vs.map(v => <option key={v.key} value={v.key}>{v.etiqueta}</option>)}</optgroup>)}
        {campos.length > 0 && <optgroup label="Personalizados">{campos.map(c => <option key={c.key} value={c.key}>{c.etiqueta}</option>)}</optgroup>}
        <option value={NUEVO_CAMPO}>➕ Dato personalizado…</option>
      </select>
      <ChevronDown className="pointer-events-none absolute right-3 top-1/2 size-4 -translate-y-1/2 text-fg-2" />
    </div>
  )
}

function NuevoCampoForm({ onCrear }: { onCrear: (etiqueta: string, tipo: CampoPlantilla['tipo']) => void }) {
  const [et, setEt] = useState('')
  const [tipo, setTipo] = useState<CampoPlantilla['tipo']>('texto')
  return (
    <form className="space-y-4" onSubmit={e => { e.preventDefault(); if (et.trim()) onCrear(et, tipo) }}>
      <Input label="¿Cómo se llama este dato?" hint="Se te preguntará cada vez que generes un contrato. Ej.: Uso del inmueble, Cajones de estacionamiento." value={et} onChange={e => setEt(e.target.value)} autoFocus />
      <Select label="Tipo" value={tipo} onChange={e => setTipo(e.target.value as CampoPlantilla['tipo'])}>
        <option value="texto">Texto</option><option value="numero">Número</option><option value="fecha">Fecha</option>
      </Select>
      <Button type="submit" full icon={<Plus className="size-4" />} disabled={!et.trim()}>Crear dato</Button>
    </form>
  )
}
