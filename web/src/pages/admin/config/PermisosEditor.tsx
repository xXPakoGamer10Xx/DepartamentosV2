import { PERMISO_LABELS, PRESETS } from '../../../lib/permisos'
import type { Permiso } from '../../../lib/types'

/** Presets (Cobrador / Contador / Asistente) + casillas por grupo. */
export function PermisosEditor({ value, preset, onChange }: { value: Permiso[]; preset: string; onChange: (permisos: Permiso[], preset: string) => void }) {
  const grupos = [...new Set(PERMISO_LABELS.map(p => p.grupo))]
  const toggle = (k: Permiso) => onChange(value.includes(k) ? value.filter(x => x !== k) : [...value, k], 'custom')
  return (
    <div className="space-y-4">
      <div className="grid gap-2 sm:grid-cols-3">
        {PRESETS.map(p => (
          <button key={p.key} type="button" onClick={() => onChange([...p.permisos], p.key)}
            className={`rounded-xl border p-3 text-left transition ${preset === p.key ? 'border-primary bg-primary-soft' : 'border-line bg-card hover:bg-hover'}`}>
            <span className="block text-sm font-bold">{p.label}</span><span className="text-xs text-fg-2">{p.descripcion}</span>
          </button>
        ))}
      </div>
      {grupos.map(g => (
        <fieldset key={g} className="space-y-1.5">
          <legend className="mb-1 text-[11px] font-bold uppercase tracking-wide text-muted">{g}</legend>
          {PERMISO_LABELS.filter(p => p.grupo === g).map(p => (
            <label key={p.key} className="flex cursor-pointer items-center gap-3 rounded-lg px-2 py-1.5 text-sm hover:bg-hover">
              <input type="checkbox" checked={value.includes(p.key)} onChange={() => toggle(p.key)} className="size-4 accent-[var(--primary)]" />{p.label}
            </label>
          ))}
        </fieldset>
      ))}
    </div>
  )
}
