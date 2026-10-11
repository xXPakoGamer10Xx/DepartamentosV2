/**
 * Reduce una foto a un JPEG razonable antes de subirla como comprobante (data URL).
 * Una foto de celular pesa varios MB; así viaja en ~150-300 KB y respeta el límite de 10 MB del API.
 */
export async function compressImage(file: File, maxSide = 1600, quality = 0.72): Promise<string> {
  if (!file.type.startsWith('image/')) throw new Error('El archivo debe ser una imagen')
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.round(bitmap.width * scale)
  canvas.height = Math.round(bitmap.height * scale)
  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('No se pudo procesar la imagen')
  ctx.fillStyle = '#fff'
  ctx.fillRect(0, 0, canvas.width, canvas.height)
  ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  bitmap.close()
  return canvas.toDataURL('image/jpeg', quality)
}

/** Abre el selector de archivos del sistema (en el celular ofrece cámara o galería). */
export function pickImage(opts: { camera?: boolean } = {}): Promise<File | null> {
  return new Promise(resolve => {
    const input = document.createElement('input')
    input.type = 'file'
    input.accept = 'image/*'
    if (opts.camera) input.setAttribute('capture', 'environment') // en el celular abre la cámara trasera
    input.onchange = () => resolve(input.files?.[0] ?? null)
    input.oncancel = () => resolve(null)
    input.click()
  })
}
