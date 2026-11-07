/** Destino tras iniciar sesión: la ruta interna que se pidió antes (p. ej. el enlace de un QR), o el inicio. */
export function destinoTrasLogin(state: unknown): string {
  const from = (state as { from?: unknown } | null)?.from
  return typeof from === 'string' && from.startsWith('/') && !from.startsWith('//') ? from : '/'
}
