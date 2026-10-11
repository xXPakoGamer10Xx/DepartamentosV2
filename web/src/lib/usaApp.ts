import { useQuery } from '@tanstack/react-query'
import { api } from './api'

/**
 * Modo de uso del administrador: con app para inquilinos (QR de pago, comprobantes, tickets) o uso interno.
 * Se guarda en la config como `usa_qr_inquilinos`. Mientras carga se asume "con app" para no ocultar nada por error.
 */
export function useUsaApp(): boolean {
  const { data } = useQuery({ queryKey: ['config'], queryFn: async () => (await api.getConfig()).data, staleTime: 60_000 })
  return data?.usa_qr_inquilinos !== 'false'
}
