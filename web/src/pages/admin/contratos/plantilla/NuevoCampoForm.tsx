import { Plus } from 'lucide-react'
import { useState } from 'react'
import { Button } from '../../../../components/ui/Button'
import { Input } from '../../../../components/ui/Input'
import { Select } from '../../../../components/ui/Select'
import type { CampoPlantilla } from '../../../../lib/types'


export function NuevoCampoForm({ onCrear }: { onCrear: (etiqueta: string, tipo: CampoPlantilla['tipo']) => void }) {
  const [et, setEt] = useState('')
  const [tipo, setTipo] = useState<CampoPlantilla['tipo']>('texto')
  return (
    <form className="space-y-4" onSubmit={e => { e.preventDefault(); if (et.trim()) onCrear(et, tipo) }}>
      <Input label="¿Cómo se llama este dato?" hint="Se te preguntará cada vez que generes un contrato. Ej.: Uso del inmueble, Cajones de estacionamiento." value={et} onChange={e => setEt(e.target.value)} autoFocus />
      <Select label="Tipo" value={tipo} onChange={e => setTipo(e.target.value as CampoPlantilla['tipo'])}>
        <option value="texto">Texto</option><option value="numero">Número</option><option value="fecha">Fecha</option>
      </Select>
      <Button type="submit" full icon={<Plus className="size-4" />} disabled={!et.trim()}>Crear dato</Button>
    </form>
  )
}
