/** Abre el selector de archivos del sistema para un contrato (.docx o .pdf) y lo devuelve como data URL (base64). */
export function pickDocx(): Promise<{ nombre: string; dataUrl: string } | null> {
  return new Promise((resolve, reject) => {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = '.docx,.pdf,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document'
    input.style.display = 'none'
    // En el DOM mientras el selector está abierto: un input suelto puede recolectarse antes de elegir el archivo.
    document.body.appendChild(input)
    const fin = () => input.remove()
    input.onchange = () => {
      const file = input.files?.[0]
      fin()
      if (!file) return resolve(null)
      leerDocx(file).then(resolve, reject)
    }
    input.oncancel = () => { fin(); resolve(null) }
    input.click()
  })
}

export const MAX_DOCX = 5 * 1024 * 1024

export function leerDocx(file: File): Promise<{ nombre: string; dataUrl: string } | null> {
  return new Promise((resolve, reject) => {
    if (!/\.(docx|pdf)$/i.test(file.name)) return reject(new Error('El archivo debe ser un Word (.docx) o un PDF. Si es .doc, guárdalo como .docx desde Word.'))
    if (file.size > MAX_DOCX) return reject(new Error('El archivo pesa más de 5 MB'))
    const r = new FileReader()
    r.onload = () => resolve({ nombre: file.name.replace(/\.(docx|pdf)$/i, ''), dataUrl: String(r.result) })
    r.onerror = () => reject(new Error('No se pudo leer el archivo'))
    r.readAsDataURL(file)
  })
}

/** Guarda un Blob como archivo descargado. */
export function guardarBlob(blob: Blob, nombre: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a'); a.href = url; a.download = nombre; a.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export interface PdfParaVer { blob: Blob; titulo: string; nombre: string }
let visor: ((pdf: PdfParaVer) => void) | null = null
/** Lo registra <VisorPdfHost/>: el visor propio de la app (con Imprimir, Descargar y Compartir). */
export const registrarVisorPdf = (fn: ((pdf: PdfParaVer) => void) | null) => { visor = fn }

/**
 * Muestra un PDF (Blob autenticado) en el visor de la app. Una pestaña con un iframe no ofrece imprimir ni
 * descargar en la PWA instalada, y en el celular los iframes de PDF ni se dibujan.
 */
export function abrirPdf(blob: Blob, titulo = 'Contrato') {
  const pdf = new Blob([blob], { type: 'application/pdf' })
  const nombre = `${titulo.replace(/[\\/:*?"<>|]+/g, '').replace(/\s+/g, '_').slice(0, 80) || 'documento'}.pdf`
  if (visor) return visor({ blob: pdf, titulo, nombre })
  const url = URL.createObjectURL(pdf) // sin visor montado: el PDF directo en una pestaña
  window.open(url, '_blank')
  setTimeout(() => URL.revokeObjectURL(url), 10 * 60_000)
}
