import type { Marca } from '../../../../lib/types'

export const NUEVO_CAMPO = '__nuevo__'

export interface MarcaUI extends Marca { texto: string; confianza?: number }
export interface Seleccion { p: string; ini: number; fin: number; texto: string }

export const slug = (s: string) => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 30) || 'dato'
