// En el sistema los nombres se capturan "Apellidos Nombre(s)" (formato de listas: PACHECO BUEN DIA JESUS IVAN).
// Para mostrarlos se invierte a "Nombre(s) Apellidos". Es una heurística sobre texto libre: los datos guardados no cambian.
const PARTICULAS = new Set(['de', 'del', 'la', 'las', 'los', 'y', 'san', 'santa', 'da', 'das', 'do', 'dos', 'van', 'von', 'mc'])

export function nombreVisible(completo?: string | null): string {
  const raw = (completo ?? '').trim().replace(/\s+/g, ' ')
  if (!raw) return ''
  const palabras = raw.split(' ')
  // "De La Cruz" cuenta como un solo apellido
  const tokens: string[] = []
  for (let i = 0; i < palabras.length; i++) {
    let t = palabras[i]
    while (PARTICULAS.has(t.toLowerCase()) && i < palabras.length - 1) t += ` ${palabras[++i]}`
    tokens.push(t)
  }
  const n = tokens.length
  if (n < 2) return raw
  const apellidos = n === 2 ? 1 : n <= 4 ? 2 : Math.max(2, n - 2)
  return [...tokens.slice(apellidos), ...tokens.slice(0, apellidos)].join(' ')
}
