import clsx from 'clsx'
import type { HTMLAttributes } from 'react'

export function Card({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div {...rest} className={clsx('rounded-2xl border border-line bg-card p-5 shadow-sm shadow-black/[0.03]', className)} />
}
