import { ArrowLeft } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

export function PageHeader({ title, subtitle, back, actions }: { title: string; subtitle?: ReactNode; back?: string; actions?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      {back && <Link to={back} aria-label="Volver" className="grid size-10 place-items-center rounded-xl text-fg-2 hover:bg-hover"><ArrowLeft className="size-5" /></Link>}
      <div className="min-w-0 flex-1">
        <h1 className="truncate text-2xl font-extrabold tracking-tight">{title}</h1>
        {subtitle && <p className="text-sm text-fg-2">{subtitle}</p>}
      </div>
      {actions && <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto">{actions}</div>}
    </div>
  )
}
