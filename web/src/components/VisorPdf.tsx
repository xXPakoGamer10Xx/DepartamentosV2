import { Download, ExternalLink, Printer, Share2, X } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { guardarBlob, registrarVisorPdf, type PdfParaVer } from '../lib/file'
import { Button } from './ui/Button'

/** En pantallas táctiles los navegadores no dibujan PDFs dentro de un iframe: se ofrecen acciones en su lugar. */
const esTactil = () => typeof window !== 'undefined' && window.matchMedia('(pointer: coarse)').matches

/** Visor de PDF a pantalla completa con Imprimir, Descargar, Compartir y Abrir. Montar una sola vez. */
export function VisorPdfHost() {
  const [pdf, setPdf] = useState<PdfParaVer | null>(null)
  useEffect(() => { registrarVisorPdf(setPdf); return () => registrarVisorPdf(null) }, [])
  return pdf ? <Visor key={pdf.nombre + pdf.blob.size} pdf={pdf} onClose={() => setPdf(null)} /> : null
}

function Visor({ pdf, onClose }: { pdf: PdfParaVer; onClose: () => void }) {
  const url = useMemo(() => URL.createObjectURL(pdf.blob), [pdf.blob])
  useEffect(() => () => URL.revokeObjectURL(url), [url])
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const frame = useRef<HTMLIFrameElement>(null)
  const tactil = esTactil()
  const archivo = useMemo(() => new File([pdf.blob], pdf.nombre, { type: 'application/pdf' }), [pdf])
  const puedeCompartir = typeof navigator.canShare === 'function' && navigator.canShare({ files: [archivo] })

  const imprimir = () => {
    try { frame.current?.contentWindow?.focus(); frame.current?.contentWindow?.print() }
    catch { window.open(url, '_blank') } // el visor del sistema trae su propio botón de imprimir
  }
  const compartir = () => { void navigator.share({ files: [archivo], title: pdf.titulo }).catch(() => { /* cancelado */ }) }

  return (
    <div className="fixed inset-0 z-[95] flex flex-col bg-bg" role="dialog" aria-modal="true" aria-label={pdf.titulo}>
      <div className="flex shrink-0 items-center gap-2 border-b border-line bg-card px-4 py-3 pt-[max(0.75rem,env(safe-area-inset-top))]">
        <h2 className="min-w-0 flex-1 truncate text-base font-extrabold">{pdf.titulo}</h2>
        {!tactil && <Button variant="secondary" className="h-9 px-3" icon={<Printer className="size-4" />} onClick={imprimir}>Imprimir</Button>}
        {!tactil && <Button variant="secondary" className="h-9 px-3" icon={<Download className="size-4" />} onClick={() => guardarBlob(pdf.blob, pdf.nombre)}>Descargar</Button>}
        <button onClick={onClose} aria-label="Cerrar" className="cursor-pointer rounded-lg p-2 text-muted transition hover:bg-hover hover:text-fg active:scale-95"><X className="size-5" /></button>
      </div>

      {tactil ? (
        <div className="flex flex-1 flex-col items-center justify-center gap-3 overflow-y-auto p-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] text-center">
          <p className="max-w-xs text-sm text-fg-2">El documento está listo. Ábrelo para verlo e imprimirlo, descárgalo o compártelo con otra app.</p>
          <a href={url} target="_blank" rel="noreferrer" className="w-full max-w-xs"><Button full icon={<ExternalLink className="size-4" />}>Abrir para ver o imprimir</Button></a>
          <Button full variant="secondary" className="max-w-xs" icon={<Download className="size-4" />} onClick={() => guardarBlob(pdf.blob, pdf.nombre)}>Descargar</Button>
          {puedeCompartir && <Button full variant="secondary" className="max-w-xs" icon={<Share2 className="size-4" />} onClick={compartir}>Compartir o imprimir…</Button>}
        </div>
      ) : (
        <iframe ref={frame} src={url} title={pdf.titulo} className="min-h-0 w-full flex-1 border-0 bg-white" />
      )}
    </div>
  )
}
