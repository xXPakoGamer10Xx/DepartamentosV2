/** Convierte un entero a letras en español (pesos mexicanos): 3500 → "tres mil quinientos pesos". */
export function numberToWords(num: number): string {
  const unidades = ['', 'un', 'dos', 'tres', 'cuatro', 'cinco', 'seis', 'siete', 'ocho', 'nueve']
  const decenas = ['diez', 'veinte', 'treinta', 'cuarenta', 'cincuenta', 'sesenta', 'setenta', 'ochenta', 'noventa']
  const especiales = ['once', 'doce', 'trece', 'catorce', 'quince', 'dieciséis', 'diecisiete', 'dieciocho', 'diecinueve']
  const centenas = ['', 'ciento', 'doscientos', 'trescientos', 'cuatrocientos', 'quinientos', 'seiscientos', 'setecientos', 'ochocientos', 'novecientos']

  const group = (n: number): string => {
    let out = ''
    if (n === 100) return 'cien'
    if (n > 100) { out += centenas[Math.floor(n / 100)] + ' '; n %= 100 }
    if (n >= 10 && n <= 19) out += (n === 10 ? 'diez' : especiales[n - 11]) + ' '
    else if (n >= 20) {
      if (n === 20) out += 'veinte '
      else if (n < 30) out += 'veinti' + unidades[n - 20] + ' '
      else out += decenas[Math.floor(n / 10) - 1] + (n % 10 !== 0 ? ' y ' + unidades[n % 10] : '') + ' '
    } else if (n > 0) out += unidades[n] + ' '
    return out
  }

  if (num === 0) return 'cero pesos'
  let r = ''
  const millones = Math.floor(num / 1_000_000)
  const miles = Math.floor((num % 1_000_000) / 1000)
  const resto = Math.floor(num % 1000)
  if (millones > 0) r += millones === 1 ? 'un millón ' : group(millones) + 'millones '
  if (miles > 0) r += miles === 1 ? 'mil ' : group(miles) + 'mil '
  if (resto > 0) r += group(resto)
  return r.trim().toLowerCase() + ' pesos'
}
