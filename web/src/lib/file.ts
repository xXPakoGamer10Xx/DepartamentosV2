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

/** Abre un PDF en una pestaña nueva (desde un Blob autenticado), con `titulo` como nombre de la pestaña. */
export function abrirPdf(blob: Blob, titulo = 'Contrato') {
  const url = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }))
  const w = window.open('', '_blank')
  if (!w) { const a = document.createElement('a'); a.href = url; a.target = '_blank'; a.click(); return }
  const t = titulo.replace(/[<>&"]/g, '')
  w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>${t}</title><style>html,body,iframe{margin:0;width:100%;height:100%;border:0}</style></head><body><iframe src="${url}" title="${t}"></iframe></body></html>`)
  w.document.close()
  setTimeout(() => URL.revokeObjectURL(url), 10 * 60_000)
}
