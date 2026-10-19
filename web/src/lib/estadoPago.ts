import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { api } from './api'
import { rowState, toSaldo, type EstadoActual, type RowState, type Saldo } from './cobranza'

/** Estado de pago del mes de cada inquilino activo (pagado / en revisión / atrasado / por pagar). Vacío si no hay permiso. */
export function useEstadosPago(habilitado: boolean): Map<string, RowState> | null {
  const estados = useQuery({ queryKey: ['estados-actuales'], queryFn: async () => (await api.getEstadosActuales()).data, enabled: habilitado, refetchInterval: 30_000 })
  const saldos = useQuery({ queryKey: ['saldos'], queryFn: async () => (await api.getSaldos()).data, enabled: habilitado, refetchInterval: 30_000 })
  return useMemo(() => {
    if (!habilitado || !estados.data || !saldos.data) return null
    const est = new Map<string, EstadoActual>(estados.data.map(e => [e.inquilino_id, e]))
    const sal = new Map<string, Saldo>(saldos.data.map(s => [s.inquilino_id, toSaldo(s)]))
    const ids = new Set([...est.keys(), ...sal.keys()])
    return new Map([...ids].map(id => [id, rowState(est.get(id), sal.get(id))]))
  }, [habilitado, estados.data, saldos.data])
}
