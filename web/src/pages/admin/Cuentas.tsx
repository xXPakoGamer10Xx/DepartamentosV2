import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Landmark, Pencil, Plus, Star, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { BankCard3D } from '../../components/ui/BankCard3D'
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
import { money } from '../../lib/format'
import type { CuentaBancaria } from '../../lib/types'

interface Form { id?: string; alias: string; banco_nombre: string; banco_clabe: string; banco_titular: string; activo: boolean }
const VACIO: Form = { alias: '', banco_nombre: '', banco_clabe: '', banco_titular: '', activo: true }

export default function Cuentas() {
  const { user } = useAuth()
  const esAdmin = user?.rol === 'admin'
  const qc = useQueryClient()
  const toast = useToast()
  const [form, setForm] = useState<Form | null>(null)
  const [formError, setFormError] = useState('')
  const [borrar, setBorrar] = useState<CuentaBancaria | null>(null)

  const { data = [], isLoading } = useQuery({ queryKey: ['cuentas'], queryFn: async () => (await api.getCuentas()).data })
  const refrescar = () => { void qc.invalidateQueries({ queryKey: ['cuentas'] }); void qc.invalidateQueries({ queryKey: ['config'] }) }

  const guardar = useMutation({
    mutationFn: (f: Form) => {
      const payload = { alias: f.alias.trim(), banco_nombre: f.banco_nombre.trim(), banco_clabe: f.banco_clabe, banco_titular: f.banco_titular.trim(), activo: f.activo }
      return f.id ? api.updateCuenta(f.id, payload) : api.createCuenta(payload)
    },
    onSuccess: () => { toast('Cuenta guardada', 'success'); setForm(null); refrescar() },
    onError: (e) => setFormError(e instanceof Error ? e.message : 'No se pudo guardar la cuenta'),
  })
  const predeterminada = useMutation({ mutationFn: (id: string) => api.setCuentaDefault(id), onSuccess: () => { toast('Cuenta predeterminada actualizada', 'success'); refrescar() }, onError: (e) => toast(e instanceof Error ? e.message : 'Error', 'error') })
  const eliminar = useMutation({
    mutationFn: (id: string) => api.deleteCuenta(id),
    onSuccess: (res) => {
      toast(res.inquilinos_a_efectivo ? `Cuenta eliminada. ${res.inquilinos_a_efectivo} inquilino(s) pasaron a pago en efectivo.` : 'Cuenta eliminada', 'success')
      setBorrar(null); refrescar(); void qc.invalidateQueries({ queryKey: ['departamentos'] })
    },
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (!form) return
    if (form.banco_clabe.length !== 18) return setFormError('La CLABE debe tener 18 dígitos')
    setFormError(''); guardar.mutate(form)
  }

  return (
    <StaggerList className="space-y-6">
      <PageHeader title="Cuentas bancarias" subtitle="Cuentas donde tus inquilinos depositan por transferencia"
        actions={esAdmin && <Button icon={<Plus className="size-4" />} onClick={() => { setFormError(''); setForm(VACIO) }}>Nueva cuenta</Button>} />

      {isLoading && <div className="grid place-items-center py-20"><Spinner className="size-8" /></div>}
      {!isLoading && data.length === 0 && (
        <StaggerItem>
          <Card><EmptyState icon={Landmark} title="Sin cuentas bancarias">{esAdmin ? 'Agrega una cuenta para recibir transferencias.' : 'El administrador aún no ha registrado cuentas.'}</EmptyState></Card>
        </StaggerItem>
      )}

      {data.length > 0 && (
        <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
          {data.map(c => (
            <StaggerItem key={c.id} layout>
              <Card className={`space-y-4 p-0 transition duration-200 hover:border-line-strong ${c.activo ? '' : 'opacity-60'}`}>
                <BankCard3D
                  bancoNombre={c.banco_nombre}
                  clabe={c.banco_clabe}
                  titular={c.banco_titular}
                  alias={c.alias || 'Cuenta'}
                  rounded="rounded-t-2xl"
                  badges={
                    <>
                      {c.es_predeterminada && <span className="rounded-full bg-white/20 px-2.5 py-0.5 text-[11px] font-bold">★ Predeterminada</span>}
                      {!c.activo && <span className="rounded-full bg-black/30 px-2.5 py-0.5 text-[11px] font-bold">Inactiva</span>}
                    </>
                  }
                />
                <div className="space-y-3 px-5 pb-5">
                  <div className="flex items-center justify-between text-sm"><span className="text-fg-2">Renta mensual que recibe</span><b>{money(c.renta_mensual)}</b></div>
                  <div className="flex flex-wrap items-center gap-1.5 text-sm">
                    <span className="text-fg-2">Departamentos:</span>
                    {c.departamentos?.length ? c.departamentos.map(d => <Link key={d} to={`/departamentos/${d}`}><Badge tone="primary">{d}</Badge></Link>) : <span className="text-muted">{c.es_predeterminada ? 'todos los que no tengan otra asignada' : 'ninguno'}</span>}
                  </div>
                  {esAdmin && (
                    <div className="flex flex-wrap gap-2 border-t border-line pt-3">
                      <Button variant="secondary" className="h-9 px-3" icon={<Pencil className="size-4" />} onClick={() => { setFormError(''); setForm({ id: c.id, alias: c.alias ?? '', banco_nombre: c.banco_nombre ?? '', banco_clabe: c.banco_clabe ?? '', banco_titular: c.banco_titular ?? '', activo: c.activo }) }}>Editar</Button>
                      {!c.es_predeterminada && <Button variant="secondary" className="h-9 px-3" icon={<Star className="size-4" />} loading={predeterminada.isPending && predeterminada.variables === c.id} onClick={() => predeterminada.mutate(c.id)}>Predeterminada</Button>}
                      <Button variant="ghost" className="h-9 px-3 text-danger" icon={<Trash2 className="size-4" />} onClick={() => { eliminar.reset(); setBorrar(c) }}>Eliminar</Button>
                    </div>
                  )}
                </div>
              </Card>
            </StaggerItem>
          ))}
        </div>
      )}

      <Modal open={!!form} title={form?.id ? 'Editar cuenta' : 'Nueva cuenta'} onClose={() => setForm(null)}>
        {form && (
          <form onSubmit={submit} className="space-y-5">
            <Input label="Alias (opcional)" placeholder="Ej. Cuenta BBVA Francisco" maxLength={60} value={form.alias} onChange={e => setForm({ ...form, alias: e.target.value })} />
            <Input label="Banco" placeholder="Ej. BBVA" maxLength={60} value={form.banco_nombre} onChange={e => setForm({ ...form, banco_nombre: e.target.value })} />
            <Input label="CLABE (18 dígitos) *" inputMode="numeric" placeholder="18 dígitos" maxLength={18} value={form.banco_clabe} onChange={e => { setFormError(''); setForm({ ...form, banco_clabe: e.target.value.replace(/\D/g, '') }) }} />
            <Input label="Titular" placeholder="Nombre de quien recibe" maxLength={120} value={form.banco_titular} onChange={e => setForm({ ...form, banco_titular: e.target.value })} />
            {form.id && (
              <label className="flex cursor-pointer items-center gap-2 text-sm text-fg-2">
                <input type="checkbox" checked={form.activo} onChange={e => setForm({ ...form, activo: e.target.checked })} className="size-4 accent-[var(--primary)]" /> Cuenta activa
              </label>
            )}
            <ShakeError error={formError}>
              <p role="alert" className="rounded-xl bg-danger-soft px-4 py-3 text-sm text-danger">{formError}</p>
            </ShakeError>
            <div className="pt-2">
              <Button type="submit" full loading={guardar.isPending}>Guardar cuenta</Button>
            </div>
          </form>
        )}
      </Modal>

      <ConfirmDialog open={!!borrar} title="Eliminar cuenta" confirmLabel="Eliminar" loading={eliminar.isPending}
        error={eliminar.error instanceof Error ? eliminar.error.message : null} onClose={() => setBorrar(null)} onConfirm={() => borrar && eliminar.mutate(borrar.id)}>
        <p>¿Eliminar la cuenta <b className="text-fg">{borrar?.alias || borrar?.banco_nombre}</b>? Los departamentos que la usaban quedarán sin cuenta asignada.</p>
        {data.length === 1 && <p className="mt-2 font-semibold text-warning">Es tu única cuenta: los inquilinos con transferencia pasarán a pago en efectivo.</p>}
      </ConfirmDialog>
    </StaggerList>
  )
}

