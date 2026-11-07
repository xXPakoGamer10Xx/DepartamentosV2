/** Lista vacía estable: `data ?? SIN_DATOS` no crea un arreglo nuevo en cada render (evita recalcular useMemo). */
export const SIN_DATOS: never[] = []
