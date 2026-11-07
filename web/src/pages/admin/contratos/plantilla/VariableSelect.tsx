import { ChevronDown } from 'lucide-react'
import type { CampoPlantilla, VariableContrato } from '../../../../lib/types'
import { NUEVO_CAMPO } from './comun'


export function VariableSelect({ valor, variables, campos, onChange, placeholder, full = true }: {
  valor: string; variables: [string, VariableContrato[]][]; campos: CampoPlantilla[]; onChange: (v: string) => void; placeholder?: string; full?: boolean
}) {
  return (
    <div className={full ? 'relative w-full' : 'relative w-full sm:w-64'}>
      <select value={valor} aria-label="Dato que se rellenará" onChange={e => onChange(e.target.value)}
        className="h-10 w-full cursor-pointer appearance-none truncate rounded-xl border border-line bg-card pl-3.5 pr-9 text-sm font-semibold text-fg transition hover:border-line-strong focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/25">
        {placeholder && <option value="">{placeholder}</option>}
        {variables.map(([g, vs]) => <optgroup key={g} label={g}>{vs.map(v => <option key={v.key} value={v.key}>{v.etiqueta}</option>)}</optgroup>)}
        {campos.length > 0 && <optgroup label="Personalizados">{campos.map(c => <option key={c.key} value={c.key}>{c.etiqueta}</option>)}</optgroup>}
        <option value={NUEVO_CAMPO}>➕ Dato personalizado…</option>
      </select>
      <ChevronDown className="pointer-events-none absolute right-3 top-1/2 size-4 -translate-y-1/2 text-fg-2" />
    </div>
  )
}
