import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Camera, CalendarDays, FileText, Phone, ScanLine, User, Users, Wallet, Banknote } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Input } from '../../components/ui/Input'
import { PageHeader } from '../../components/ui/PageHeader'
import { Segmented } from '../../components/ui/Segmented'
import { Select } from '../../components/ui/Select'
import { Spinner } from '../../components/ui/Spinner'
import { Textarea } from '../../components/ui/Textarea'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import {
  addOneYear, buildFechaPago, esNotaAutomatica, normalizeFechaPago, notaDeposito, pagosMensuales, quincenaFechas, toISO, type DepositoTipo,
} from '../../lib/deposito'
import { fecha } from '../../lib/format'
import { compressImage, pickImage } from '../../lib/image'
import { numberToWords } from '../../lib/numberToWords'
import type { Inquilino } from '../../lib/types'

type Metodo = 'efectivo' | 'transferencia' | 'ambos'
interface FormState {
  nombre: string; depto: string; tel: string
  renta: string; rentaLetra: string; deposito: string; metodo: Metodo; diaPago: string
  inicio: string; termino: string
  fiador: string; telFiador: string; observaciones: string
  depTipo: DepositoTipo; depDia: string; depPagos: number; depFechas: string[]
}

const digits = (s: string) => s.replace(/\D/g, '')
const iso10 = (s?: string | null) => (s ? String(s).substring(0, 10) : '')

function blank(): FormState {
  const hoy = new Date()
  return {
    nombre: '', depto: '', tel: '', renta: '', rentaLetra: '', deposito: '', metodo: 'efectivo', diaPago: String(hoy.getDate()),
    inicio: toISO(hoy), termino: toISO(addOneYear(hoy)), fiador: '', telFiador: '', observaciones: '',
    depTipo: 'ninguno', depDia: '', depPagos: 2, depFechas: [],
  }
}

/** Datos personales y condiciones de un inquilino existente (editar, o base de un contrato nuevo). */
function fromInquilino(d: Inquilino, mode: 'edit' | 'copy'): FormState {
  const base = blank()
  const common = {
    nombre: d.nombre_completo || '', tel: d.tel_arrendatario || '', renta: String(Math.round(Number(d.renta) || 0) || ''),
    rentaLetra: d.renta_letra || '', fiador: d.fiador_nombre || '', telFiador: d.fiador_telefono || '', metodo: (d.metodo_pago || 'efectivo') as Metodo,
  }
  if (mode === 'copy') return { ...base, ...common }
  const fechas = (d.deposito_fechas ?? []).map(iso10).filter(Boolean)
  const tipo = (d.deposito_tipo || 'ninguno') as DepositoTipo
  return {
    ...base, ...common,
    depto: String(d.depto_numero ?? ''), deposito: String(Math.round(Number(d.deposito) || 0)),
    diaPago: String(d.fecha_pago?.match(/\d{1,2}/)?.[0] ?? base.diaPago),
    inicio: iso10(d.fecha_inicio), termino: iso10(d.fecha_termino), observaciones: d.observaciones || '',
    depTipo: tipo, depFechas: fechas,
    depDia: tipo === 'personalizado' && fechas[0] ? String(new Date(`${fechas[0]}T12:00:00`).getDate()) : '',
    depPagos: tipo === 'personalizado' && fechas.length ? fechas.length : 2,
  }
}

export default function InquilinoForm() {
  const { id } = useParams()
  const [sp] = useSearchParams()
  const desde = sp.get('desde')
  const sourceId = id ?? desde
  const src = useQuery({ queryKey: ['inquilino', sourceId], queryFn: async () => (await api.getInquilino(sourceId!)).data, enabled: !!sourceId })

  if (sourceId && src.isLoading) return <div className="grid place-items-center py-24"><Spinner className="size-8" /></div>
  const initial = src.data ? fromInquilino(src.data, id ? 'edit' : 'copy') : blank()
  // `key` reinicia el formulario si cambia el origen (editar otro inquilino, etc.)
  return <Formulario key={`${id ?? 'new'}-${desde ?? ''}`} editId={id} initial={initial} editando={!!id} desde={!!desde} initialObsEdited={!!id} />
}

function Formulario({ editId, initial, editando, desde, initialObsEdited }: { editId?: string; initial: FormState; editando: boolean; desde: boolean; initialObsEdited: boolean }) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()
  const [f, setF] = useState<FormState>(initial)
  // Al editar, depósito y día de corte ya vienen fijados: no se recalculan solos.
  const [depositoManual, setDepositoManual] = useState(initialObsEdited)
  const [diaManual, setDiaManual] = useState(initialObsEdited)
  const [error, setError] = useState('')
  const [leyendoIne, setLeyendoIne] = useState(false)
  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => { setError(''); setF(s => ({ ...s, [k]: v })) }

  const deptos = useQuery({ queryKey: ['departamentos'], queryFn: async () => (await api.getDepartamentos()).data })
  const opciones = useMemo(() => (deptos.data ?? []).filter(d => editando || d.estado === 'disponible'), [deptos.data, editando])

  // ── Reglas automáticas (iguales a la app anterior) ───────────────────────────
  const setRenta = (raw: string) => {
    const c = digits(raw).slice(0, 6)
    const n = parseInt(c, 10)
    setError('')
    setF(s => ({ ...s, renta: c, rentaLetra: Number.isNaN(n) ? '' : numberToWords(n), deposito: depositoManual ? s.deposito : c }))
  }

  /** Reescribe las notas del depósito sin pisar notas que el admin haya escrito a mano. */
  const conNotaDeposito = (s: FormState, tipo: DepositoTipo, fechas: string[]): FormState =>
    esNotaAutomatica(s.observaciones) ? { ...s, observaciones: notaDeposito(tipo, fechas) } : s

  const recalcDeposito = (s: FormState, tipo: DepositoTipo, inicio: string, dia: string, pagos: number): FormState => {
    const fechas = tipo === 'quincenas' ? quincenaFechas(inicio) : tipo === 'personalizado' ? pagosMensuales(parseInt(dia, 10), pagos, inicio) : []
    return conNotaDeposito({ ...s, depTipo: tipo, depDia: tipo === 'personalizado' ? dia : '', depPagos: pagos, depFechas: fechas }, tipo, fechas)
  }

  const setInicio = (v: string) => {
    if (!v) return
    const d = new Date(`${v}T12:00:00`)
    setError('')
    setF(s => {
      const next = { ...s, inicio: v, termino: toISO(addOneYear(d)), diaPago: diaManual ? s.diaPago : String(d.getDate()) }
      return s.depTipo === 'ninguno' ? next : recalcDeposito(next, s.depTipo, v, s.depDia || String(d.getDate()), s.depPagos)
    })
  }

  const setDepTipo = (tipo: DepositoTipo) => {
    setError('')
    setF(s => recalcDeposito(s, tipo, s.inicio, tipo === 'personalizado' ? (s.diaPago || '1') : '', 2))
  }

  const escanearIne = async () => {
    const file = await pickImage({ camera: true })
    if (!file) return
    setLeyendoIne(true)
    try {
      const img = await compressImage(file, 1800, 0.85)
      const d = (await api.extraerIne(img)).data
      setF(s => ({
        ...s,
        nombre: d.nombre_completo || s.nombre,
        observaciones: s.observaciones || [d.domicilio, d.colonia, d.municipio, d.estado, d.cp].filter(Boolean).join(', '),
      }))
      toast('Datos extraídos del INE. Revisa y edita si es necesario.', 'success')
    } catch (e) {
      toast(e instanceof Error ? e.message : 'No se pudo leer el INE. Intenta con una imagen más clara.', 'error')
    } finally { setLeyendoIne(false) }
  }

  const guardar = useMutation({
    mutationFn: async () => {
      const payload = {
        nombre_completo: f.nombre.trim(), depto_numero: Number(f.depto), tel_arrendatario: f.tel,
        renta: Number(f.renta), renta_letra: f.rentaLetra, deposito: Number(f.deposito),
        fecha_pago: normalizeFechaPago(buildFechaPago(f.diaPago || '1')), fecha_inicio: f.inicio, fecha_termino: f.termino,
        fiador_nombre: f.fiador.trim(), fiador_telefono: f.telFiador, observaciones: f.observaciones,
        metodo_pago: f.metodo, deposito_tipo: f.depTipo, deposito_fechas: f.depFechas,
      }
      return editId ? api.updateInquilino(editId, payload) : api.createInquilino(payload)
    },
    onSuccess: (res) => {
      toast(editando ? 'Cambios guardados' : 'Inquilino registrado', 'success')
      void qc.invalidateQueries({ queryKey: ['inquilinos'] })
      void qc.invalidateQueries({ queryKey: ['inquilino'] })
      void qc.invalidateQueries({ queryKey: ['departamentos'] })
      navigate(`/inquilinos/${res.data.id}`, { replace: true })
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'No se pudo guardar el inquilino'),
  })

  const submit = () => {
    if (!f.nombre.trim() || !f.depto || !f.tel || !f.renta || !f.deposito || !f.inicio || !f.termino) return setError('Completa todos los campos obligatorios (*).')
    if (f.tel.length !== 10) return setError('El teléfono del arrendatario debe tener 10 dígitos.')
    if (f.telFiador && f.telFiador.length !== 10) return setError('El teléfono del fiador debe tener 10 dígitos.')
    if (f.termino <= f.inicio) return setError('La fecha de término debe ser posterior a la de inicio.')
    const dia = parseInt(f.diaPago, 10)
    if (!dia || dia < 1 || dia > 31) return setError('El día de pago debe estar entre 1 y 31.')
    setError(''); guardar.mutate()
  }

  return (
    <div className="space-y-6 pb-4">
      <PageHeader back={editId ? `/inquilinos/${editId}` : '/inquilinos'}
        title={editando ? 'Editar inquilino' : desde ? 'Nuevo contrato' : 'Registro de inquilino'}
        subtitle="Asigna departamento, datos del arrendatario y condiciones del contrato." />

      {!editando && (
        <Card className="flex flex-wrap items-center gap-4 border-primary/25 bg-primary-soft">
          <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-primary/15 text-primary"><ScanLine className="size-5" /></span>
          <div className="min-w-0 flex-1">
            <p className="font-bold">Autocompletar con INE (IA)</p>
            <p className="text-sm text-fg-2">Fotografía la credencial para rellenar los datos del arrendatario.</p>
          </div>
          <Button variant="secondary" loading={leyendoIne} onClick={escanearIne} icon={<Camera className="size-4" />}>{leyendoIne ? 'Analizando…' : 'Escanear INE'}</Button>
        </Card>
      )}

      <div className="grid gap-5 lg:grid-cols-2">
        <div className="space-y-5">
          <Seccion titulo="Datos del arrendatario" icon={<User className="size-4" />}>
            <Input label="Nombre completo *" icon={<User />} placeholder="Ej. Juan Pérez García" value={f.nombre} onChange={e => set('nombre', e.target.value)} />
            <Select label="Departamento asignado *" value={f.depto} onChange={e => set('depto', e.target.value)}>
              <option value="">{deptos.isLoading ? 'Cargando…' : opciones.length ? 'Selecciona un departamento…' : 'No hay departamentos disponibles'}</option>
              {opciones.map(d => <option key={d.numero} value={d.numero}>Departamento {d.numero}{d.estado !== 'disponible' ? ` (${d.estado})` : ''}{d.descripcion ? ` — ${d.descripcion}` : ''}</option>)}
            </Select>
            <Input label="Teléfono *" icon={<Phone />} inputMode="tel" placeholder="10 dígitos" maxLength={10} value={f.tel} onChange={e => set('tel', digits(e.target.value))} />
          </Seccion>

          <Seccion titulo="Obligado solidario / fiador (opcional)" icon={<Users className="size-4" />}>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Nombre del fiador" placeholder="Ej. María López" value={f.fiador} onChange={e => set('fiador', e.target.value)} />
              <Input label="Teléfono del fiador" inputMode="tel" placeholder="10 dígitos" maxLength={10} value={f.telFiador} onChange={e => set('telFiador', digits(e.target.value))} />
            </div>
          </Seccion>

          <Seccion titulo="Observaciones y notas" icon={<FileText className="size-4" />}>
            <Textarea placeholder="Notas adicionales sobre el contrato o el inquilino…" value={f.observaciones} onChange={e => set('observaciones', e.target.value)} />
          </Seccion>
        </div>

        <div className="space-y-5">
          <Seccion titulo="Condiciones financieras" icon={<Wallet className="size-4" />}>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Renta mensual *" inputMode="numeric" placeholder="3500" value={f.renta} onChange={e => setRenta(e.target.value)} />
              <Input label="Depósito *" inputMode="numeric" placeholder="3500" value={f.deposito} onChange={e => { setDepositoManual(true); set('deposito', digits(e.target.value)) }} />
            </div>
            <Input label="Renta en letra" placeholder="Se completa sola al escribir la renta" value={f.rentaLetra} onChange={e => set('rentaLetra', e.target.value)} />
            <div className="space-y-1.5">
              <span className="block text-xs font-bold uppercase tracking-wide text-fg-2">Método de pago</span>
              <Segmented<Metodo> value={f.metodo} onChange={v => set('metodo', v)} options={[{ value: 'efectivo', label: 'Efectivo' }, { value: 'transferencia', label: 'Transferencia' }, { value: 'ambos', label: 'Ambos' }]} />
            </div>
            <Input label="Día de pago (de cada mes) *" type="number" min={1} max={31} inputMode="numeric" value={f.diaPago}
              onChange={e => { setDiaManual(true); set('diaPago', e.target.value.slice(0, 2)) }} />
          </Seccion>

          <Seccion titulo="Vigencia del contrato" icon={<CalendarDays className="size-4" />}>
            <div className="grid gap-4 sm:grid-cols-2">
              <Input label="Fecha de inicio *" type="date" value={f.inicio} onChange={e => setInicio(e.target.value)} />
              <Input label="Fecha de término *" type="date" value={f.termino} onChange={e => set('termino', e.target.value)} />
            </div>
          </Seccion>

          <Seccion titulo="Depósito diferido" icon={<Banknote className="size-4" />}>
            <Select label="Modalidad de pago del depósito" value={f.depTipo} onChange={e => setDepTipo(e.target.value as DepositoTipo)}>
              <option value="ninguno">Completo al inicio del contrato</option>
              <option value="quincenas">En dos quincenas (15 y fin de mes)</option>
              <option value="personalizado">En pagos mensuales</option>
            </Select>
            {f.depTipo === 'personalizado' && (
              <div className="grid gap-4 sm:grid-cols-2">
                <Input label="Día del mes" type="number" min={1} max={31} value={f.depDia}
                  onChange={e => setF(s => recalcDeposito(s, 'personalizado', s.inicio, e.target.value.slice(0, 2), s.depPagos))} />
                <Select label="Número de pagos" value={f.depPagos} onChange={e => setF(s => recalcDeposito(s, 'personalizado', s.inicio, s.depDia, Number(e.target.value)))}>
                  {[2, 3, 4, 5, 6].map(n => <option key={n} value={n}>{n} pagos</option>)}
                </Select>
              </div>
            )}
            {f.depFechas.length > 0 && (
              <ul className="flex flex-wrap gap-2">{f.depFechas.map(x => <li key={x} className="rounded-lg bg-hover px-3 py-1 text-sm font-semibold">{fecha(x)}</li>)}</ul>
            )}
          </Seccion>
        </div>
      </div>

      {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}

      <div className="sticky bottom-20 z-30 flex justify-end gap-3 rounded-2xl border border-line bg-card/95 p-3 shadow-lg backdrop-blur lg:bottom-4">
        <Button variant="secondary" onClick={() => navigate(-1)} disabled={guardar.isPending}>Descartar</Button>
        <Button onClick={submit} loading={guardar.isPending}>{editando ? 'Guardar cambios' : 'Guardar inquilino'}</Button>
      </div>
    </div>
  )
}

function Seccion({ titulo, icon, children }: { titulo: string; icon: ReactNode; children: ReactNode }) {
  return (
    <Card className="space-y-4">
      <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2">{icon}{titulo}</h2>
      {children}
    </Card>
  )
}
