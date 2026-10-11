import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, Hammer, Package, Trash2, UserRound, Wallet } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { EmptyState } from '../../components/ui/EmptyState'
import { PageHeader } from '../../components/ui/PageHeader'
import { Select } from '../../components/ui/Select'
import { Spinner } from '../../components/ui/Spinner'
import { useToast } from '../../components/ui/Toast'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { fecha, money } from '../../lib/format'
import { ESTADO_DEPTO, InventarioEditor } from './Departamentos'

export default function DepartamentoDetalle() {
  const { numero = '' } = useParams()
  const n = Number(numero)
  const { user, can } = useAuth()
  const esAdmin = user?.rol === 'admin'
  const qc = useQueryClient()
  const navigate = useNavigate()
  const toast = useToast()
  const [borrar, setBorrar] = useState(false)
  const [inv, setInv] = useState<string[] | null>(null)

  const q = useQuery({ queryKey: ['departamento', n], queryFn: async () => (await api.getDepartamento(n)).data, refetchInterval: 30_000 })
  const cuentas = useQuery({ queryKey: ['cuentas'], queryFn: async () => (await api.getCuentas()).data, enabled: esAdmin })
  const deuda = useQuery({ queryKey: ['resumen-deuda'], queryFn: async () => (await api.getResumenDeuda()).data, enabled: can('pagos') })

  const depto = q.data
  // El inventario se edita en local; se sincroniza con el servidor mientras el admin no haya cambiado nada.
  useEffect(() => { if (depto && inv === null) setInv(depto.inventario_base ?? []) }, [depto, inv])
  const inventarioSucio = depto && inv !== null && JSON.stringify(inv) !== JSON.stringify(depto.inventario_base ?? [])

  const refrescar = () => { void qc.invalidateQueries({ queryKey: ['departamento', n] }); void qc.invalidateQueries({ queryKey: ['departamentos'] }); void qc.invalidateQueries({ queryKey: ['deptos-stats'] }) }
  const err = (m: string) => (e: unknown) => toast(e instanceof Error ? e.message : m, 'error')

  const guardarInv = useMutation({ mutationFn: () => api.updateDepartamento(n, { inventario_base: inv }), onSuccess: () => { toast('Inventario actualizado', 'success'); refrescar() }, onError: err('No se pudo guardar') })
  const mantenimiento = useMutation({
    mutationFn: () => api.updateDepartamento(n, { estado: depto!.estado === 'mantenimiento' ? 'disponible' : 'mantenimiento' }),
    onSuccess: refrescar, onError: err('No se pudo cambiar el estado'),
  })
  const asignarCuenta = useMutation({
    mutationFn: (id: string | null) => api.asignarCuentaDepto(n, id),
    onSuccess: () => { toast('Cuenta asignada', 'success'); refrescar(); void qc.invalidateQueries({ queryKey: ['cuentas'] }) }, onError: err('No se pudo asignar la cuenta'),
  })
  const eliminar = useMutation({
    mutationFn: () => api.deleteDepartamento(n),
    onSuccess: () => { toast('Departamento eliminado', 'success'); refrescar(); navigate('/departamentos', { replace: true }) },
  })

  if (q.isLoading) return <div className="grid place-items-center py-24"><Spinner className="size-8" /></div>
  if (q.isError || !depto) return <Card><EmptyState icon={Building2} title="Departamento no encontrado"><Link to="/departamentos" className="font-semibold text-primary">Volver</Link></EmptyState></Card>

  const cfg = ESTADO_DEPTO[depto.estado]
  const actual = depto.historial_inquilinos?.find(h => h.estado === 'activo')
  const deudaDepto = deuda.data?.por_departamento.find(d => d.depto_numero === n)
  const libre = depto.estado !== 'ocupado'

  return (
    <div className="space-y-6">
      <PageHeader title={`Departamento ${depto.numero}`} back="/departamentos" subtitle={depto.descripcion || depto.ubicacion}
        actions={<>
          <Badge tone={cfg.tone}>{cfg.label}</Badge>
          {esAdmin && libre && (
            <Button variant="secondary" className="h-10 px-4" icon={<Hammer className="size-4" />} loading={mantenimiento.isPending} onClick={() => mantenimiento.mutate()}>
              {depto.estado === 'mantenimiento' ? 'Marcar disponible' : 'Poner en mantenimiento'}
            </Button>
          )}
          {esAdmin && libre && <Button variant="danger" className="h-10 px-4" icon={<Trash2 className="size-4" />} onClick={() => { eliminar.reset(); setBorrar(true) }}>Eliminar</Button>}
        </>} />

      <div className="grid gap-5 lg:grid-cols-2">
        <Card className="space-y-3">
          <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2"><UserRound className="size-4" /> Inquilino actual</h2>
          {actual ? (
            <Link to={`/inquilinos/${actual.id}`} className="flex items-center justify-between gap-3 rounded-xl bg-hover p-4 transition hover:bg-line">
              <span><span className="block font-bold">{actual.nombre}</span><span className="text-xs text-fg-2">{fecha(actual.fecha_inicio)} → {fecha(actual.fecha_termino)}</span></span>
              <span className="font-extrabold">{money(actual.renta)}</span>
            </Link>
          ) : <p className="text-sm text-muted">Sin inquilino activo</p>}
          {deudaDepto && (
            <p className="flex items-center justify-between rounded-xl bg-danger-soft px-4 py-3 text-sm"><span className="font-semibold text-danger">Adeudo vencido</span><b className="text-danger">{money(deudaDepto.deuda_vencida)}</b></p>
          )}
        </Card>

        {esAdmin && (
          <Card className="space-y-3">
            <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2"><Wallet className="size-4" /> Cuenta para transferencias</h2>
            <Select aria-label="Cuenta bancaria" value={depto.cuenta_bancaria_id ?? ''} disabled={asignarCuenta.isPending || cuentas.isLoading} onChange={e => asignarCuenta.mutate(e.target.value || null)}>
              <option value="">Usar la cuenta predeterminada</option>
              {cuentas.data?.filter(c => c.activo).map(c => <option key={c.id} value={c.id}>{c.alias || c.banco_nombre || 'Cuenta'}{c.es_predeterminada ? ' (predeterminada)' : ''} — ····{c.banco_clabe?.slice(-4)}</option>)}
            </Select>
            <p className="text-xs text-muted">Es la cuenta que verá el inquilino de este departamento.</p>
          </Card>
        )}

        <Card className="space-y-3 lg:col-span-2">
          <div className="flex items-center justify-between">
            <h2 className="flex items-center gap-2 text-xs font-bold uppercase tracking-wide text-fg-2"><Package className="size-4" /> Inventario base</h2>
            {esAdmin && inventarioSucio && <Button className="h-9 px-4" loading={guardarInv.isPending} onClick={() => guardarInv.mutate()}>Guardar inventario</Button>}
          </div>
          <InventarioEditor items={inv ?? []} onChange={setInv} disabled={!esAdmin} />
        </Card>

        <Card className="lg:col-span-2">
          <h2 className="mb-3 text-xs font-bold uppercase tracking-wide text-fg-2">Historial de inquilinos</h2>
          {depto.historial_inquilinos?.length ? (
            <ul className="divide-y divide-line">
              {depto.historial_inquilinos.map(h => (
                <li key={h.id}>
                  <Link to={`/inquilinos/${h.id}`} className="flex items-center justify-between gap-3 py-3 text-sm hover:text-primary">
                    <span><b>{h.nombre}</b><span className="block text-xs text-muted">{fecha(h.fecha_inicio)} → {fecha(h.fecha_termino)}</span></span>
                    <span className="flex items-center gap-3"><b>{money(h.renta)}</b><Badge tone={h.estado === 'activo' ? 'success' : 'neutral'}>{h.estado === 'activo' ? 'Activo' : 'Archivado'}</Badge></span>
                  </Link>
                </li>
              ))}
            </ul>
          ) : <p className="text-sm text-muted">Aún no ha tenido inquilinos</p>}
        </Card>
      </div>

      <ConfirmDialog open={borrar} title="Eliminar departamento" confirmLabel="Eliminar" loading={eliminar.isPending}
        error={eliminar.error instanceof Error ? eliminar.error.message : null} onClose={() => setBorrar(false)} onConfirm={() => eliminar.mutate()}>
        <p>¿Seguro que deseas eliminar el <b className="text-fg">Departamento {depto.numero}</b>? Esta acción no se puede deshacer.</p>
      </ConfirmDialog>
    </div>
  )
}
