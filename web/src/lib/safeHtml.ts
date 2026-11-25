/** Inserta HTML sanitizado usando la Sanitizer API nativa (`Element.setHTML`) con carga diferida de DOMPurify como respaldo. */
export interface SafeHtmlConfig {
  elements: string[]
  attributes: string[]
}

type ElementWithSetHTML = Element & {
  setHTML(html: string, options?: { sanitizer?: SafeHtmlConfig }): void
}

let domPurifyPromise: Promise<typeof import('dompurify')['default']> | undefined

function loadDOMPurify() {
  domPurifyPromise ??= import('dompurify').then(mod => mod.default ?? (mod as unknown as typeof import('dompurify')['default']))
  return domPurifyPromise
}

export async function safeSetHTML(el: Element, html: string, config: SafeHtmlConfig): Promise<void> {
  if ('setHTML' in Element.prototype) {
    ;(el as ElementWithSetHTML).setHTML(html, { sanitizer: config })
    return
  }

  const DOMPurify = await loadDOMPurify()
  el.innerHTML = DOMPurify.sanitize(html, {
    ALLOWED_TAGS: [...config.elements, '#text'],
    ALLOWED_ATTR: config.attributes,
    // Iguala la semántica de la Sanitizer API nativa: descarta aria-*/data-* no listados y el contenido de etiquetas prohibidas.
    ALLOW_ARIA_ATTR: false,
    ALLOW_DATA_ATTR: false,
    KEEP_CONTENT: false,
  })
}
