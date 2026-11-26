import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Banknote, CalendarDays, Camera, CheckCircle2, FileText, Info, Phone, ScanLine, Sparkles, User, Users, Wallet } from 'lucide-react'
import { useId, useMemo, useState, type ReactNode } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
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
  addOneYear, buildFechaPago, cuotaMensualDeposito, esNotaAutomatica, estimarPlanSemanal,
  fechasQuincenalIntercalado, MAX_PAGOS_DEPOSITO, mesSiguienteISO, MONTO_SEMANAL_DEFAULT,
  normalizarFechas, normalizeFechaPago, notaDeposito, pagosMensuales, PLAZOS_DIFERIDO,
  toISO, type DepositoTipo,
} from '../../lib/deposito'
import { diaYDiasDelMes, enDiasISO } from '../../lib/fecha'
import { fecha, money } from '../../lib/format'
import { compressImage, pickImage } from '../../lib/image'
import { numberToWords } from '../../lib/numberToWords'
import type { Inquilino } from '../../lib/types'

type Metodo = 'efectivo' | 'transferencia' | 'ambos'
type OpcionDeposito = 'diferido_mensual' | 'quincenal' | 'contado_beneficio' | 'semanal' | 'fechas'

interface FormState {
  nombre: string; depto: string; tel: string
  renta: string; rentaLetra: string; deposito: string; metodo: Metodo; diaPago: string
  inicio: string; termino: string
  fiador: string; telFiador: string; observaciones: string
  depTipo: OpcionDeposito; depDia: string; depPagos: number; depFechas: string[]
  depMontoSemanal: string; depPagadoInicio: boolean
}

const digits = (s: string) => s.replace(/\D/g, '')
const iso10 = (s?: string | null) => (s ? String(s).substring(0, 10) : '')
const isPlazoOficial = (n: number): n is (typeof PLAZOS_DIFERIDO)[number] =>
  (PLAZOS_DIFERIDO as readonly number[]).includes(n)

function blank(): FormState {
  const inicio = enDiasISO(0)
  const { dia } = diaYDiasDelMes()
  return {
    nombre: '', depto: '', tel: '', renta: '', rentaLetra: '', deposito: '', metodo: 'efectivo', diaPago: String(dia),
    inicio, termino: toISO(addOneYear(new Date(`${inicio}T12:00:00`))), fiador: '', telFiador: '', observaciones: '',
    depTipo: 'contado_beneficio', depDia: String(dia), depPagos: 3, depFechas: [],
    depMontoSemanal: String(MONTO_SEMANAL_DEFAULT), depPagadoInicio: true,
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
  const rawTipo = (d.deposito_tipo || 'ninguno') as DepositoTipo
  const diaPago = String(d.fecha_pago?.match(/\d{1,2}/)?.[0] ?? base.diaPago)

  let depTipo: OpcionDeposito = 'contado_beneficio'
  if (rawTipo === 'diferido_mensual' || rawTipo === 'personalizado') depTipo = 'diferido_mensual'
  else if (rawTipo === 'quincenal' || rawTipo === 'quincenas') depTipo = 'quincenal'
  else if (rawTipo === 'semanal') depTipo = 'semanal'
  else if (rawTipo === 'fechas') depTipo = 'fechas'
  else depTipo = 'contado_beneficio'

  const primerDia = fechas[0] ? String(Number(fechas[0].slice(8, 10)) || diaPago) : diaPago
  const depPagos = depTipo === 'diferido_mensual' && fechas.length > 0 ? fechas.length : 3
  const depFechas = depTipo === 'fechas' ? (fechas.length ? fechas : ['']) : fechas
  const montoSem = d.deposito_monto_semanal && Number(d.deposito_monto_semanal) > 0
    ? String(Math.round(Number(d.deposito_monto_semanal)))
    : String(MONTO_SEMANAL_DEFAULT)

  return {
    ...base, ...common,
    depto: String(d.depto_numero ?? ''), deposito: String(Math.round(Number(d.deposito) || 0)),
    diaPago,
    inicio: iso10(d.fecha_inicio), termino: iso10(d.fecha_termino), observaciones: d.observaciones || '',
    depTipo,
    depDia: depTipo === 'diferido_mensual' ? primerDia : diaPago,
    depPagos,
    depFechas,
    depMontoSemanal: montoSem,
    depPagadoInicio: false,
  }
}

export default function InquilinoForm() {
  const { id } = useParams()
  const [sp] = useSearchParams()
  const desde = sp.get('desde')
  const sourceId = id ?? desde
  const src = useQuery({ queryKey: ['inquilino', sourceId], queryFn: async () => (await api.getInquilino(sourceId!)).data, enabled: !!sourceId })

  if (sourceId && src.isLoading) return <div className="grid place-items-center py-24"><Spinner className="size-8" /></div>
  // `key` reinicia el formulario si cambia el origen (editar otro inquilino, etc.)
  return (
    <Formulario
      key={`${id ?? 'new'}-${desde ?? ''}-${src.data?.id ?? ''}`}
      editId={id}
      source={src.data}
      editando={!!id}
      desde={!!desde}
      initialObsEdited={!!id}
    />
  )
}

function Formulario({
  editId, source, editando, desde, initialObsEdited,
}: {
  editId?: string; source?: Inquilino; editando: boolean; desde: boolean; initialObsEdited: boolean
}) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const toast = useToast()
  const radioName = useId()
  const [f, setF] = useState<FormState>(() => (source ? fromInquilino(source, editando ? 'edit' : 'copy') : blank()))
  // Al editar, depósito y día de corte ya vienen fijados: no se recalculan solos.
  const [depositoManual, setDepositoManual] = useState(initialObsEdited)
  const [diaManual, setDiaManual] = useState(initialObsEdited)
  const [error, setError] = useState('')
  const [leyendoIne, setLeyendoIne] = useState(false)
  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => { setError(''); setF(s => ({ ...s, [k]: v })) }

  const deptos = useQuery({ queryKey: ['departamentos'], queryFn: async () => (await api.getDepartamentos()).data })
  const opciones = useMemo(() => (deptos.data ?? []).filter(d => editando || d.estado === 'disponible'), [deptos.data, editando])

  /** Reescribe las notas del depósito sin pisar notas que el admin haya escrito a mano. */
  const conNotaDeposito = (s: FormState, tipo: OpcionDeposito, fechas: string[]): FormState =>
    esNotaAutomatica(s.observaciones)
      ? {
          ...s,
          observaciones: notaDeposito(tipo, fechas, {
            deposito: Number(s.deposito) || 0,
            montoSemanal: Number(s.depMontoSemanal) || MONTO_SEMANAL_DEFAULT,
          }),
        }
      : s

  const recalcDeposito = (
    s: FormState,
    tipo: OpcionDeposito,
    inicio: string,
    dia: string,
    pagos: number,
    montoSemanalStr: string = s.depMontoSemanal,
  ): FormState => {
    const rentaNum = Number(s.renta) || 0
    const depNum = Number(s.deposito) || 0
    const semNum = Number(montoSemanalStr) || 0
    let fechas: string[] = []
    if (tipo === 'diferido_mensual') {
      fechas = pagosMensuales(parseInt(dia, 10), pagos, inicio)
    } else if (tipo === 'quincenal') {
      fechas = fechasQuincenalIntercalado(inicio)
    } else if (tipo === 'semanal') {
      fechas = estimarPlanSemanal(rentaNum, depNum, semNum, inicio).fechas
    } else if (tipo === 'fechas') {
      fechas = s.depTipo === 'fechas' && s.depFechas.length ? s.depFechas : ['']
    }
    const next: FormState = {
      ...s,
      depTipo: tipo,
      depDia: tipo === 'diferido_mensual' ? dia : s.depDia,
      depPagos: pagos,
      depFechas: fechas,
      depMontoSemanal: montoSemanalStr,
    }
    return conNotaDeposito(next, tipo, fechas.filter(Boolean))
  }

  // ── Reglas automáticas ─────────────────────────────────────────────────────
  const setRenta = (raw: string) => {
    const c = digits(raw).slice(0, 6)
    const n = parseInt(c, 10)
    setError('')
    setF(s => {
      const next: FormState = {
        ...s,
        renta: c,
        rentaLetra: Number.isNaN(n) ? '' : numberToWords(n),
        deposito: depositoManual ? s.deposito : c,
      }
      if (next.depTipo === 'semanal') return recalcDeposito(next, 'semanal', next.inicio, next.depDia, next.depPagos)
      if (!depositoManual && (next.depTipo === 'diferido_mensual' || next.depTipo === 'quincenal')) {
        return conNotaDeposito(next, next.depTipo, next.depFechas.filter(Boolean))
      }
      return next
    })
  }

  const setDeposito = (raw: string) => {
    const c = digits(raw).slice(0, 6)
    setDepositoManual(true)
    setError('')
    setF(s => {
      const next: FormState = { ...s, deposito: c }
      if (next.depTipo === 'semanal') return recalcDeposito(next, 'semanal', next.inicio, next.depDia, next.depPagos)
      if (next.depTipo === 'diferido_mensual' || next.depTipo === 'quincenal') {
        return conNotaDeposito(next, next.depTipo, next.depFechas.filter(Boolean))
      }
      return next
    })
  }

  const setInicio = (v: string) => {
    if (!v) return
    const d = new Date(`${v}T12:00:00`)
    setError('')
    setF(s => {
      const next = { ...s, inicio: v, termino: toISO(addOneYear(d)), diaPago: diaManual ? s.diaPago : String(d.getDate()) }
      if (s.depTipo === 'contado_beneficio' || s.depTipo === 'fechas') return next
      return recalcDeposito(next, s.depTipo, v, s.depDia || String(d.getDate()), s.depPagos)
    })
  }

  /** Modalidad "fechas libres": cada renglón es una fecha de pago escogida en el calendario. */
  const setFechasDep = (fn: (prev: string[]) => string[]) => {
    setError('')
    setF(s => {
      const fechas = fn(s.depFechas)
      return conNotaDeposito({ ...s, depFechas: fechas }, 'fechas', fechas.filter(Boolean))
    })
  }

  const setDepTipo = (tipo: OpcionDeposito) => {
    setError('')
    setF(s => {
      const dia = tipo === 'diferido_mensual' ? (s.depDia || s.diaPago || '1') : s.depDia
      const pagos = tipo === 'diferido_mensual' ? (isPlazoOficial(s.depPagos) ? s.depPagos : 3) : s.depPagos
      return recalcDeposito(s, tipo, s.inicio, dia, pagos)
    })
  }

  const setMontoSemanal = (raw: string) => {
    const c = digits(raw).slice(0, 6)
    setError('')
    setF(s => recalcDeposito(s, 'semanal', s.inicio, s.depDia, s.depPagos, c))
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

  const rentaNum = Number(f.renta) || 0
  const depositoNum = Number(f.deposito) || 0
  const montoSemanalNum = Number(f.depMontoSemanal) || 0
  const planSemanal = useMemo(
    () => estimarPlanSemanal(rentaNum, depositoNum, montoSemanalNum, f.inicio),
    [rentaNum, depositoNum, montoSemanalNum, f.inicio],
  )
  const fechasQuincenales = useMemo(
    () => (f.depTipo === 'quincenal' && f.depFechas.length === 2 ? f.depFechas : fechasQuincenalIntercalado(f.inicio)),
    [f.depTipo, f.depFechas, f.inicio],
  )
  const cuotaDiferido = cuotaMensualDeposito(depositoNum, f.depPagos)
  const mitadDeposito = cuotaMensualDeposito(depositoNum, 2)

  const guardar = useMutation({
    mutationFn: async () => {
      const depFechasPayload =
        f.depTipo === 'fechas'
          ? normalizarFechas(f.depFechas).fechas
          : f.depTipo === 'semanal'
            ? planSemanal.fechas
            : f.depTipo === 'contado_beneficio'
              ? []
              : f.depFechas

      const payload = {
        nombre_completo: f.nombre.trim(), depto_numero: Number(f.depto), tel_arrendatario: f.tel,
        renta: Number(f.renta), renta_letra: f.rentaLetra, deposito: Number(f.deposito),
        fecha_pago: normalizeFechaPago(buildFechaPago(f.diaPago || '1')), fecha_inicio: f.inicio, fecha_termino: f.termino,
        fiador_nombre: f.fiador.trim(), fiador_telefono: f.telFiador, observaciones: f.observaciones,
        metodo_pago: f.metodo,
        deposito_tipo: f.depTipo,
        deposito_fechas: depFechasPayload,
        deposito_monto_semanal: f.depTipo === 'semanal' ? Number(f.depMontoSemanal) : null,
        ...(!editando && f.depTipo === 'contado_beneficio' ? { deposito_pagado_inicio: f.depPagadoInicio } : {}),
      }
      return editId ? api.updateInquilino(editId, payload) : api.createInquilino(payload)
    },
    onSuccess: (res) => {
      toast(editando ? 'Cambios guardados' : 'Inquilino registrado', 'success')
      void qc.invalidateQueries({ queryKey: ['inquilinos'] })
      void qc.invalidateQueries({ queryKey: ['inquilino'] })
      void qc.invalidateQueries({ queryKey: ['departamentos'] })
      void qc.invalidateQueries({ queryKey: ['deposito-saldo'] })
      void qc.invalidateQueries({ queryKey: ['deposito-abonos'] })
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
    if (f.depTipo === 'diferido_mensual') {
      const depDia = parseInt(f.depDia, 10)
      if (!depDia || depDia < 1 || depDia > 31) return setError('El día de pago del depósito debe estar entre 1 y 31.')
    }
    if (f.depTipo === 'semanal' && !(Number(f.depMontoSemanal) > 0)) {
      return setError('El abono semanal debe ser mayor a cero.')
    }
    if (f.depTipo === 'fechas' && normalizarFechas(f.depFechas).error) return setError(normalizarFechas(f.depFechas).error!)
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

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
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
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
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
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Input label="Renta mensual *" inputMode="numeric" placeholder="3500" className="tabular-nums" value={f.renta} onChange={e => setRenta(e.target.value)} />
              <Input label="Depósito *" inputMode="numeric" placeholder="3500" className="tabular-nums" value={f.deposito} onChange={e => setDeposito(e.target.value)} />
            </div>
            <Input label="Renta en letra" placeholder="Se completa sola al escribir la renta" value={f.rentaLetra} onChange={e => set('rentaLetra', e.target.value)} />
            <div className="space-y-1.5">
              <span className="block text-xs font-bold uppercase tracking-wide text-fg-2">Método de pago</span>
              <Segmented<Metodo> value={f.metodo} onChange={v => set('metodo', v)} options={[{ value: 'efectivo', label: 'Efectivo' }, { value: 'transferencia', label: 'Transferencia' }, { value: 'ambos', label: 'Ambos' }]} />
            </div>
            <Input label="Día de pago (de cada mes) *" type="number" min={1} max={31} inputMode="numeric" className="tabular-nums" value={f.diaPago}
              onChange={e => { setDiaManual(true); set('diaPago', e.target.value.slice(0, 2)) }} />
          </Seccion>

          <Seccion titulo="Vigencia del contrato" icon={<CalendarDays className="size-4" />}>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <Input label="Fecha de inicio *" type="date" value={f.inicio} onChange={e => setInicio(e.target.value)} />
              <Input label="Fecha de término *" type="date" value={f.termino} onChange={e => set('termino', e.target.value)} />
            </div>
          </Seccion>

          <Seccion titulo="Modalidad de pago del depósito" icon={<Banknote className="size-4" />}>
            <fieldset className="space-y-2.5">
              <legend className="sr-only">Elige la opción de pago del depósito</legend>

              {/* Opción 1 · Pago Diferido */}
              <OpcionCard
                name={radioName}
                value="diferido_mensual"
                checked={f.depTipo === 'diferido_mensual'}
                onSelect={() => setDepTipo('diferido_mensual')}
                titulo="Opción 1 · Pago Diferido"
                descripcion="Renta + depósito dividido en mensualidades fijas."
              >
                <div className="space-y-3 border-t border-line pt-3">
                  <div className="space-y-1.5">
                    <span className="block text-xs font-bold uppercase tracking-wide text-fg-2">Plazo en meses</span>
                    <Segmented<string>
                      ariaLabel="Plazo del depósito diferido"
                      value={isPlazoOficial(f.depPagos) ? String(f.depPagos) : ''}
                      onChange={v => setF(s => recalcDeposito(s, 'diferido_mensual', s.inicio, s.depDia || s.diaPago || '1', Number(v)))}
                      options={PLAZOS_DIFERIDO.map(n => ({ value: String(n), label: `${n} meses` }))}
                    />
                    {!isPlazoOficial(f.depPagos) && (
                      <p className="text-xs text-muted">Plan previo de {f.depPagos} pagos conservado; elige 3, 6, 9 o 12 meses si deseas cambiarlo.</p>
                    )}
                  </div>
                  <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                    <Input
                      label="Día del mes"
                      type="number"
                      min={1}
                      max={31}
                      inputMode="numeric"
                      className="tabular-nums"
                      value={f.depDia}
                      onChange={e => setF(s => recalcDeposito(s, 'diferido_mensual', s.inicio, e.target.value.slice(0, 2), s.depPagos))}
                    />
                    <div className="flex flex-col justify-center rounded-xl border border-line bg-bg/70 px-3.5 py-2.5 text-sm tabular-nums">
                      <span className="text-xs font-semibold text-fg-2">Cuota mensual de depósito</span>
                      <span className="text-base font-extrabold text-primary">{depositoNum > 0 ? `${money(cuotaDiferido)} / mes` : '—'}</span>
                      {rentaNum > 0 && depositoNum > 0 && (
                        <span className="text-xs text-muted">Renta + depósito: <b className="text-fg">{money(rentaNum + cuotaDiferido)}</b></span>
                      )}
                    </div>
                  </div>
                  {f.depFechas.length > 0 && (
                    <ul className="flex flex-wrap gap-1.5">
                      {f.depFechas.map((x, idx) => (
                        <li key={`${x}-${idx}`} className="rounded-lg bg-hover px-2.5 py-1 text-xs font-semibold tabular-nums">{fecha(x)}</li>
                      ))}
                    </ul>
                  )}
                </div>
              </OpcionCard>

              {/* Opción 2 · Pago Quincenal (Poco a poco) */}
              <OpcionCard
                name={radioName}
                value="quincenal"
                checked={f.depTipo === 'quincenal'}
                onSelect={() => setDepTipo('quincenal')}
                titulo="Opción 2 · Pago Quincenal (Poco a poco)"
                descripcion="Intercala la renta con 2 exhibiciones del 50% del depósito (+15 y +45 días)."
              >
                <div className="space-y-2 border-t border-line pt-3">
                  <ol className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                    {[
                      { paso: '1 · Día 1', concepto: 'Renta mes 1', fISO: f.inicio, monto: rentaNum, esDep: false },
                      { paso: '2 · +15 días', concepto: '50% depósito', fISO: fechasQuincenales[0], monto: mitadDeposito, esDep: true },
                      { paso: '3 · Mes 2', concepto: 'Renta mes 2', fISO: mesSiguienteISO(f.inicio), monto: rentaNum, esDep: false },
                      { paso: '4 · +45 días', concepto: '50% depósito', fISO: fechasQuincenales[1], monto: mitadDeposito, esDep: true },
                    ].map(hito => (
                      <li
                        key={hito.paso}
                        className={`rounded-xl border px-3 py-2 text-xs tabular-nums ${
                          hito.esDep ? 'border-primary/30 bg-primary-soft' : 'border-line bg-bg/70'
                        }`}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-bold text-fg-2">{hito.paso}</span>
                          <span className="font-semibold text-muted">{fecha(hito.fISO)}</span>
                        </div>
                        <div className="mt-1 flex items-center justify-between gap-2 text-sm">
                          <span className={hito.esDep ? 'font-bold text-primary' : 'font-semibold text-fg'}>{hito.concepto}</span>
                          <b className="text-fg">{hito.monto > 0 ? money(hito.monto) : '—'}</b>
                        </div>
                      </li>
                    ))}
                  </ol>
                </div>
              </OpcionCard>

              {/* Opción 3 · Contado y Beneficio */}
              <OpcionCard
                name={radioName}
                value="contado_beneficio"
                checked={f.depTipo === 'contado_beneficio'}
                onSelect={() => setDepTipo('contado_beneficio')}
                titulo="Opción 3 · Contado y Beneficio"
                descripcion="Pago completo al inicio del contrato."
                badge={<Badge tone="success">Beneficio Mes 13</Badge>}
              >
                <div className="space-y-3 border-t border-line pt-3">
                  <div className="flex gap-2.5 rounded-xl border border-success/30 bg-success-soft p-3 text-xs">
                    <Sparkles className="mt-0.5 size-4 shrink-0 text-success" aria-hidden="true" />
                    <div className="space-y-0.5">
                      <p className="font-bold text-success">¡El mes 13 se paga con tu depósito!</p>
                      <p className="text-fg-2">Al cumplir el año de contrato y avisar con 15 días de anticipación, el mes 13 se cubre con el depósito sin desembolsar efectivo.</p>
                    </div>
                  </div>
                  {!editando && (
                    <label className="flex min-h-11 cursor-pointer items-center gap-3 rounded-xl border border-line bg-bg/70 px-3.5 py-2 text-sm font-medium text-fg">
                      <input
                        type="checkbox"
                        checked={f.depPagadoInicio}
                        onChange={e => set('depPagadoInicio', e.target.checked)}
                        className="size-4 shrink-0 accent-primary"
                      />
                      <span>Registrar depósito como ya pagado al guardar</span>
                    </label>
                  )}
                </div>
              </OpcionCard>

              {/* Opción 4 · Pago Semanal */}
              <OpcionCard
                name={radioName}
                value="semanal"
                checked={f.depTipo === 'semanal'}
                onSelect={() => setDepTipo('semanal')}
                titulo="Opción 4 · Pago Semanal"
                descripcion="Cada abono semanal cubre primero la renta y el excedente abona al depósito."
              >
                <div className="space-y-3 border-t border-line pt-3">
                  <Input
                    label="Abono semanal ($)"
                    type="number"
                    min={1}
                    inputMode="numeric"
                    placeholder={String(MONTO_SEMANAL_DEFAULT)}
                    className="tabular-nums"
                    value={f.depMontoSemanal}
                    onChange={e => setMontoSemanal(e.target.value)}
                  />
                  <div className="grid grid-cols-1 gap-2 rounded-xl border border-line bg-bg/70 p-3 text-xs tabular-nums sm:grid-cols-3">
                    <div>
                      <p className="text-fg-2">Equiv. mensual</p>
                      <p className="text-sm font-extrabold text-fg">{money(planSemanal.equivMensual)}</p>
                    </div>
                    <div>
                      <p className="text-fg-2">Excedente/mes</p>
                      <p className={`text-sm font-extrabold ${planSemanal.excedenteMensual > 0 ? 'text-success' : 'text-danger'}`}>
                        {money(planSemanal.excedenteMensual)}
                      </p>
                    </div>
                    <div>
                      <p className="text-fg-2">Depósito completo</p>
                      <p className="text-sm font-extrabold text-primary">
                        {planSemanal.semanas > 0 ? `~${planSemanal.semanas} sem` : 'Sin avance'}
                      </p>
                      {planSemanal.fechas.length > 0 && (
                        <p className="text-[11px] text-muted">Est. {fecha(planSemanal.fechas[planSemanal.fechas.length - 1])}</p>
                      )}
                    </div>
                  </div>
                  {planSemanal.insuficiente && (
                    <div role="alert" className="flex flex-wrap items-start gap-2.5 rounded-xl border border-warning/40 bg-warning-soft p-3 text-xs text-fg">
                      <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden="true" />
                      <div className="min-w-0 flex-1 space-y-1">
                        <p className="font-bold text-warning">Abono semanal insuficiente para cubrir la renta mensual</p>
                        <p className="text-fg-2">
                          Con <b className="tabular-nums text-fg">{money(montoSemanalNum)}/sem</b> ({money(planSemanal.equivMensual)}/mes) no queda suficiente margen sobre la renta de <b className="tabular-nums text-fg">{money(rentaNum)}</b>. Sugerido: al menos <b className="tabular-nums text-fg">{money(planSemanal.minimoSugerido)}/sem</b>.
                        </p>
                        <button
                          type="button"
                          onClick={() => setMontoSemanal(String(planSemanal.minimoSugerido))}
                          className="mt-1 inline-flex min-h-9 cursor-pointer items-center gap-1 rounded-lg bg-card px-2.5 py-1 text-xs font-bold text-primary shadow-xs ring-1 ring-line transition hover:bg-hover"
                        >
                          <CheckCircle2 className="size-3.5" aria-hidden="true" />
                          Usar {money(planSemanal.minimoSugerido)}/sem
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              </OpcionCard>

              {/* Fechas libres */}
              <OpcionCard
                name={radioName}
                value="fechas"
                checked={f.depTipo === 'fechas'}
                onSelect={() => setDepTipo('fechas')}
                titulo="Fechas libres"
                descripcion="Elige fecha por fecha en el calendario para acuerdos especiales."
              >
                <div className="space-y-3 border-t border-line pt-3">
                  {f.depFechas.map((x, i) => (
                    <div key={i} className="flex items-end gap-2">
                      <div className="flex-1">
                        <Input label={`Pago ${i + 1}`} type="date" value={x} min={f.inicio}
                          onChange={e => setFechasDep(prev => prev.map((v, j) => (j === i ? e.target.value : v)))} />
                      </div>
                      {f.depFechas.length > 1 && (
                        <Button type="button" variant="secondary" aria-label={`Quitar pago ${i + 1}`} onClick={() => setFechasDep(prev => prev.filter((_, j) => j !== i))}>Quitar</Button>
                      )}
                    </div>
                  ))}
                  {f.depFechas.length < MAX_PAGOS_DEPOSITO && (
                    <Button type="button" variant="secondary" onClick={() => setFechasDep(prev => [...prev, ''])}>+ Agregar fecha</Button>
                  )}
                </div>
              </OpcionCard>
            </fieldset>

            <p className="flex items-start gap-2 rounded-xl border border-line bg-hover px-3.5 py-2.5 text-xs leading-relaxed text-fg-2">
              <Info className="mt-0.5 size-4 shrink-0 text-muted" aria-hidden="true" />
              <span><b className="text-fg">Importante:</b> El depósito no es reembolsable bajo ninguna circunstancia ni hay devolución de dinero ni de abonos.</span>
            </p>
          </Seccion>
        </div>
      </div>

      {error && <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{error}</p>}

      <div className="sticky bottom-20 z-30 flex justify-end gap-3 rounded-2xl border border-line bg-card/95 p-3 shadow-lg backdrop-blur lg:bottom-4">
        <Button type="button" variant="secondary" onClick={() => navigate(-1)} disabled={guardar.isPending}>Descartar</Button>
        <Button type="button" onClick={submit} loading={guardar.isPending}>{editando ? 'Guardar cambios' : 'Guardar inquilino'}</Button>
      </div>
    </div>
  )
}

function OpcionCard({
  name, value, checked, onSelect, titulo, descripcion, badge, children,
}: {
  name: string; value: OpcionDeposito; checked: boolean; onSelect: () => void
  titulo: string; descripcion: string; badge?: ReactNode; children: ReactNode
}) {
  const id = useId()
  return (
    <div
      className={`rounded-2xl border p-3.5 transition has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-primary/35 ${
        checked ? 'border-primary bg-primary-soft/45 shadow-xs' : 'border-line bg-card hover:border-line-strong'
      }`}
    >
      <label
        htmlFor={id}
        className="flex min-h-11 cursor-pointer items-start gap-3 rounded-xl focus-within:outline-none"
      >
        <input
          id={id}
          type="radio"
          name={name}
          value={value}
          checked={checked}
          onChange={onSelect}
          className="mt-1 size-4 shrink-0 cursor-pointer accent-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
        />
        <span className="min-w-0 flex-1">
          <span className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-bold text-fg">{titulo}</span>
            {badge}
          </span>
          <span className="mt-0.5 block text-xs text-fg-2">{descripcion}</span>
        </span>
      </label>
      {checked && <div className="mt-3">{children}</div>}
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
