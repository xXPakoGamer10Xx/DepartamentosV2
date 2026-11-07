import type { MarcaUI } from './comun'



/** Envuelve en <mark> cada rango del texto de su párrafo. Los offsets son de texto plano del párrafo. */
export function pintarMarcas(root: HTMLElement, marcas: MarcaUI[], etiqueta: (key: string) => string) {
  marcas.forEach((m, i) => {
    const p = root.querySelector<HTMLElement>(`[data-p="${m.p}"]`)
    if (!p) return
    const walker = document.createTreeWalker(p, NodeFilter.SHOW_TEXT)
    const nodes: { node: Text; start: number }[] = []
    let pos = 0
    while (walker.nextNode()) { const n = walker.currentNode as Text; nodes.push({ node: n, start: pos }); pos += n.length }
    const envolver: { node: Text; a: number; b: number }[] = []
    for (const { node, start } of nodes) {
      const a = Math.max(m.ini, start), b = Math.min(m.fin, start + node.length)
      if (a < b) envolver.push({ node, a: a - start, b: b - start })
    }
    envolver.forEach(({ node, a, b }, k) => {
      const range = document.createRange(); range.setStart(node, a); range.setEnd(node, b)
      const mark = document.createElement('mark')
      mark.dataset.i = String(i)
      if (m.key.startsWith('campo_')) mark.className = 'pers'
      mark.title = etiqueta(m.key)
      if (k === envolver.length - 1) mark.dataset.label = etiqueta(m.key)
      range.surroundContents(mark)
    })
  })
}
