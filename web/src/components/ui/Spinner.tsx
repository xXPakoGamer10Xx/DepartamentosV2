import { Loader2 } from 'lucide-react'

export function Spinner({ className = 'size-6' }: { className?: string }) {
  return <Loader2 className={`animate-spin text-primary ${className}`} aria-label="Cargando" />
}

export function FullPageSpinner() {
  return <div className="grid h-full place-items-center bg-bg"><Spinner className="size-8" /></div>
}
