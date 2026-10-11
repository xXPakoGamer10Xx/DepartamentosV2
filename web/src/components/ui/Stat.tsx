import type { ReactNode } from 'react'
import { AnimatedNumber } from './AnimatedNumber'
import { Card } from './Card'

export function Stat({
  label,
  value,
  numericValue,
  format,
  sub,
  tone = '',
  icon,
}: {
  label: string
  value?: ReactNode
  numericValue?: number
  format?: (n: number) => string
  sub?: ReactNode
  tone?: string
  icon?: ReactNode
}) {
  return (
    <Card className="flex h-full flex-col justify-between p-4 transition duration-200 hover:-translate-y-0.5 hover:border-line-strong hover:shadow-md">
      <div>
        <div className="flex items-start justify-between gap-2">
          <p className="text-[11px] font-bold uppercase tracking-wide text-muted">{label}</p>
          {icon && <span className="text-muted [&>svg]:size-4">{icon}</span>}
        </div>
        <p className={`mt-1 text-2xl font-extrabold tracking-tight ${tone}`}>
          {typeof numericValue === 'number' ? <AnimatedNumber value={numericValue} format={format} /> : value}
        </p>
      </div>
      {sub && <p className="mt-1 text-xs text-fg-2">{sub}</p>}
    </Card>
  )
}

