import { Pencil, Trash2 } from 'lucide-react'
import { fecha, money } from '../../../lib/format'
import type { Abono } from '../../../lib/types'


export function Abonos({ items, editable, onEdit, onDelete }: { items: Abono[]; editable: boolean; onEdit: (a: Abono) => void; onDelete: (a: Abono) => void }) {
  if (!items.length) return <p className="text-sm text-muted">Sin abonos registrados</p>
  return (
    <ul className="divide-y divide-line">
      {items.map(a => (
        <li key={a.id} className="flex items-center gap-3 py-2.5 text-sm">
          <span className="min-w-0 flex-1">
            <span className="block font-semibold">{fecha(a.fecha)} · {a.metodo === 'transferencia' ? 'Transferencia' : 'Efectivo'}</span>
            <span className="block truncate text-xs text-muted">{[a.nota, a.registrado_por_nombre && `por ${a.registrado_por_nombre}`].filter(Boolean).join(' · ') || '—'}</span>
          </span>
          <b className="text-success">{money(a.monto)}</b>
          {editable && (
            <span className="flex gap-1">
              <button onClick={() => onEdit(a)} aria-label="Editar abono" className="grid size-8 place-items-center rounded-lg text-muted hover:bg-hover hover:text-fg"><Pencil className="size-4" /></button>
              <button onClick={() => onDelete(a)} aria-label="Eliminar abono" className="grid size-8 place-items-center rounded-lg text-muted hover:bg-danger-soft hover:text-danger"><Trash2 className="size-4" /></button>
            </span>
          )}
        </li>
      ))}
    </ul>
  )
}
