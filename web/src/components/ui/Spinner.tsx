export function Spinner({ className = 'size-6', label = 'Cargando' }: { className?: string; label?: string }) {
  return <progress aria-label={label} className={`loading-spinner ${className}`} />
}

export function FullPageSpinner() {
  return <div className="grid h-full place-items-center bg-bg"><Spinner className="size-8" /></div>
}
