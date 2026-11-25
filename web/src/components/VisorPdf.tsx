import { Download, ExternalLink, Printer, Share2, X } from 'lucide-react'
import { useEffect, useId, useMemo, useRef, useState } from 'react'
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
  const titleId = useId()
  const dialogRef = useRef<HTMLDialogElement>(null)
  const onCloseRef = useRef(onClose)
  const url = useMemo(() => URL.createObjectURL(pdf.blob), [pdf.blob])
  useEffect(() => () => URL.revokeObjectURL(url), [url])

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    if (!dialog.open) dialog.showModal()
    const onNativeClose = () => {
      dialog.returnValue = ''
      onCloseRef.current()
    }
    dialog.addEventListener('close', onNativeClose)
    return () => {
      dialog.removeEventListener('close', onNativeClose)
      if (dialog.open) dialog.close()
    }
  }, [])

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
    <dialog
      ref={dialogRef}
      aria-labelledby={titleId}
      className="native-dialog fixed inset-0 h-dvh w-screen flex-col bg-bg"
    >
      <div className="flex shrink-0 items-center gap-2 border-b border-line bg-card px-4 py-3 pt-[max(0.75rem,env(safe-area-inset-top))]">
        <h1 id={titleId} className="min-w-0 flex-1 truncate text-base font-extrabold">{pdf.titulo}</h1>
        {!tactil && <Button type="button" variant="secondary" className="min-h-9 px-3" icon={<Printer className="size-4" aria-hidden="true" />} onClick={imprimir}>Imprimir</Button>}
        {!tactil && <Button type="button" variant="secondary" className="min-h-9 px-3" icon={<Download className="size-4" aria-hidden="true" />} onClick={() => guardarBlob(pdf.blob, pdf.nombre)}>Descargar</Button>}
        <button
          type="button"
          onClick={() => dialogRef.current?.close()}
          aria-label="Cerrar"
          className="cursor-pointer rounded-lg p-2 text-muted transition hover:bg-hover hover:text-fg active:scale-95"
        >
          <X className="size-5" aria-hidden="true" />
        </button>
      </div>

      {tactil ? (
        <div className="flex flex-1 flex-col items-center justify-center gap-3 overflow-y-auto p-6 pb-[max(1.5rem,env(safe-area-inset-bottom))] text-center">
          <p className="max-w-xs text-sm text-fg-2">El documento está listo. Ábrelo para verlo e imprimirlo, descárgalo o compártelo con otra app.</p>
          <a href={url} target="_blank" rel="noreferrer" className="inline-flex min-h-11 w-full max-w-xs items-center justify-center gap-2 rounded-xl bg-primary px-5 text-sm font-semibold text-white shadow-sm shadow-primary/20 transition hover:bg-primary-dark active:scale-[0.98]">
            <ExternalLink className="size-4" aria-hidden="true" /> Abrir para ver o imprimir
          </a>
          <Button type="button" full variant="secondary" className="max-w-xs" icon={<Download className="size-4" aria-hidden="true" />} onClick={() => guardarBlob(pdf.blob, pdf.nombre)}>Descargar</Button>
          {puedeCompartir && <Button type="button" full variant="secondary" className="max-w-xs" icon={<Share2 className="size-4" aria-hidden="true" />} onClick={compartir}>Compartir o imprimir…</Button>}
        </div>
      ) : (
        <iframe ref={frame} src={url} title={pdf.titulo} className="min-h-0 w-full flex-1 border-0 bg-white" />
      )}
    </dialog>
  )
}
