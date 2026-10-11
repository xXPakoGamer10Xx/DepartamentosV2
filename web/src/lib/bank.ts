// Colores de marca aproximados por banco para pintar la tarjeta de "Datos para transferencia".
// No reproduce logos oficiales (marcas registradas): solo su paleta.
export interface BankBrand { gradient: [string, string]; chip: string; accent: string }

const DEFAULT: BankBrand = { gradient: ['#1a56c4', '#0a3d8a'], chip: 'rgba(255,200,0,0.85)', accent: 'rgba(255,255,255,0.55)' }

const BRANDS: { match: RegExp; brand: BankBrand }[] = [
  { match: /bbva|bancomer/i, brand: { gradient: ['#004C93', '#001B3A'], chip: 'rgba(0,169,224,0.9)', accent: '#5FD4FF' } },
  { match: /santander/i, brand: { gradient: ['#EC0000', '#600000'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /banorte/i, brand: { gradient: ['#D9272E', '#4A0A0D'], chip: 'rgba(255,209,0,0.9)', accent: '#FFD100' } },
  { match: /hsbc/i, brand: { gradient: ['#DB0011', '#161616'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /banamex|citibanamex/i, brand: { gradient: ['#00285E', '#00112B'], chip: 'rgba(230,0,38,0.9)', accent: '#FF6A7A' } },
  { match: /scotiabank/i, brand: { gradient: ['#EC111A', '#4A0000'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /azteca/i, brand: { gradient: ['#0F8A5F', '#053626'], chip: 'rgba(255,255,255,0.9)', accent: '#8DC63F' } },
  { match: /inbursa/i, brand: { gradient: ['#0B3C5D', '#031624'], chip: 'rgba(247,148,30,0.9)', accent: '#F7941E' } },
  { match: /banregio/i, brand: { gradient: ['#F5811F', '#5C2E00'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /afirme/i, brand: { gradient: ['#8C1D2B', '#2B0509'], chip: 'rgba(212,175,55,0.9)', accent: '#D4AF37' } },
  { match: /nu\b|nubank/i, brand: { gradient: ['#8A05BE', '#2E0140'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /klar/i, brand: { gradient: ['#1A1A1A', '#000000'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /ual[aá]/i, brand: { gradient: ['#6C3EF4', '#20104D'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /bienestar/i, brand: { gradient: ['#7A1FA2', '#260834'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
  { match: /mercado\s*pago/i, brand: { gradient: ['#00AEEF', '#005B85'], chip: 'rgba(255,255,255,0.9)', accent: '#FFFFFF' } },
]

export const getBankBrand = (nombre?: string | null): BankBrand =>
  (nombre && BRANDS.find(b => b.match.test(nombre))?.brand) || DEFAULT

export function detectBankInfo(numero: string): { tipo: string; formato: string } {
  if (!numero) return { tipo: 'CLABE', formato: '•••• •••• •••• ••••' }
  const c = numero.replace(/\D/g, '')
  if (c.length === 18) return { tipo: 'CLABE', formato: `${c.slice(0, 4)} ${c.slice(4, 8)} ${c.slice(8, 12)} ${c.slice(12, 16)} ${c.slice(16)}` }
  if (c.length === 16) return { tipo: 'No. de tarjeta', formato: `${c.slice(0, 4)} ${c.slice(4, 8)} ${c.slice(8, 12)} ${c.slice(12)}` }
  if (c.length >= 10) return { tipo: 'No. de cuenta', formato: c }
  return { tipo: 'Cuenta', formato: numero }
}
