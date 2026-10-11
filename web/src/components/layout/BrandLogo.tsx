import clsx from 'clsx'

export function BrandLogo({
  collapsed = false,
  compact = false,
  variant = 'default',
  className,
}: {
  collapsed?: boolean
  compact?: boolean
  variant?: 'default' | 'hero'
  className?: string
}) {
  const isHero = variant === 'hero'

  return (
    <div
      className={clsx(
        'inline-flex items-center select-none',
        isHero
          ? 'gap-3.5 rounded-2xl border border-white/15 bg-[#0E1321]/70 px-4 py-2.5 shadow-2xl shadow-black/40 backdrop-blur-md'
          : 'gap-3',
        className,
      )}
    >
      <span
        className={clsx(
          'relative grid shrink-0 place-items-center rounded-xl border border-primary/25 bg-gradient-to-br from-primary/20 via-[#0E1321] to-[#10B981]/20 shadow-md shadow-primary/10',
          isHero ? 'size-11 p-1.5' : compact ? 'size-9 p-1.5' : 'size-10 p-1.5',
        )}
      >
        <img
          src="/logo-mark.png"
          alt="NethRent"
          className="size-full object-contain drop-shadow-[0_2px_6px_rgba(37,99,235,0.4)]"
        />
      </span>
      {!collapsed && (
        <div className="min-w-0 leading-none">
          <div
            className={clsx(
              'font-extrabold tracking-tight',
              isHero ? 'text-2xl' : compact ? 'text-lg' : 'text-[21px]',
            )}
          >
            <span className={isHero ? 'text-white' : 'text-fg'}>Neth</span>
            <span className="bg-gradient-to-r from-[#10B981] to-[#34D399] bg-clip-text text-transparent">
              Rent
            </span>
          </div>
          {!compact && (
            <p
              className={clsx(
                'mt-1.5 font-bold uppercase tracking-[0.22em]',
                isHero ? 'text-[10px] text-white/75' : 'text-[9.5px] text-muted',
              )}
            >
              Executive Suite
            </p>
          )}
        </div>
      )}
    </div>
  )
}
