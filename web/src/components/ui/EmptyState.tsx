import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'

export function EmptyState({ icon: Icon, title, children }: { icon: LucideIcon; title: string; children?: ReactNode }) {
  return (
    <div className="mx-auto max-w-sm py-16 text-center">
      <span className="mx-auto mb-4 grid size-16 place-items-center rounded-2xl bg-hover text-muted"><Icon className="size-8" /></span>
      <p className="font-bold">{title}</p>
      {children && <p className="mt-1 text-sm text-fg-2">{children}</p>}
    </div>
  )
}
