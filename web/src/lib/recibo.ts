// Recibo simple en HTML para imprimir/compartir con el inquilino. No incluye datos internos del admin
// (datos bancarios, comentarios de rechazo, ids, historial de otros periodos): solo el desglose de lo que debe.
export interface ReciboParams {
  arrendadorNombre: string
  nombreInquilino: string
  deptoNumero: string | number
  periodoLabel: string
  renta: number
  cargosExtra: { concepto: string; monto: number }[]
  abonosRenta: { monto: number; fecha: string }[]
  saldoRentaPendiente: number
  depositoPendiente: number
}

const fmt = (n: number) => `$${Number(n || 0).toLocaleString('es-MX', { minimumFractionDigits: 2 })}`
const fmtFecha = (iso: string) => new Date(iso).toLocaleDateString('es-MX', { day: '2-digit', month: 'short', year: 'numeric' })
const esc = (s: string) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

export function buildReciboHtml(p: ReciboParams): string {
  const totalExtra = p.cargosExtra.reduce((s, c) => s + c.monto, 0)
  const totalGeneral = Math.max(p.saldoRentaPendiente, 0) + Math.max(p.depositoPendiente, 0)
  const hoy = new Date().toLocaleDateString('es-MX', { day: '2-digit', month: 'long', year: 'numeric' })
  const cargos = p.cargosExtra.map(c => `<tr><td class="indent">${esc(c.concepto)}</td><td class="val">+${fmt(c.monto)}</td></tr>`).join('')
  const abonos = p.abonosRenta.map(a => `<tr><td class="indent">Abono · ${fmtFecha(a.fecha)}</td><td class="val neg">-${fmt(a.monto)}</td></tr>`).join('')

  return `<!DOCTYPE html><html lang="es"><head><meta charset="utf-8" /><title>Recibo</title>
<style>
*{box-sizing:border-box}body{font-family:-apple-system,Helvetica,Arial,sans-serif;color:#111;margin:0;padding:32px;max-width:480px}
h1{font-size:18px;margin:0 0 2px;text-align:center}.sub{text-align:center;color:#555;font-size:12px;margin-bottom:20px}
.meta{font-size:13px;margin-bottom:18px}.meta div{display:flex;justify-content:space-between;padding:2px 0}.meta .k{color:#555}.meta .v{font-weight:600}
table{width:100%;border-collapse:collapse;font-size:13px}td{padding:5px 0}
.section-title{font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:#555;padding-top:14px;border-top:1px solid #ddd}
.indent{padding-left:12px;color:#333}.val{text-align:right;font-variant-numeric:tabular-nums}.neg{color:#B91C1C}
.subtotal td{border-top:1px solid #ddd;font-weight:700;padding-top:8px}
.total-box{margin-top:22px;padding:16px;background:#111;color:#fff;border-radius:10px;text-align:center}
.total-box .t-label{font-size:11px;text-transform:uppercase;letter-spacing:.05em;opacity:.75}.total-box .t-val{font-size:26px;font-weight:800;margin-top:4px}
.paid-box{margin-top:22px;padding:16px;background:#ECFDF5;border:1px solid #A7F3D0;border-radius:10px;text-align:center;color:#065F46;font-weight:700}
.footer{margin-top:24px;text-align:center;font-size:10px;color:#999}@media print{body{padding:0}}
</style></head><body>
<h1>Recibo</h1><div class="sub">${esc(p.arrendadorNombre)}</div>
<div class="meta">
<div><span class="k">Inquilino</span><span class="v">${esc(p.nombreInquilino)}</span></div>
<div><span class="k">Departamento</span><span class="v">${esc(String(p.deptoNumero))}</span></div>
<div><span class="k">Periodo</span><span class="v">${esc(p.periodoLabel)}</span></div>
<div><span class="k">Fecha</span><span class="v">${hoy}</span></div></div>
<table><tr><td>Renta</td><td class="val">${fmt(p.renta)}</td></tr>${cargos}
${p.cargosExtra.length ? `<tr class="subtotal"><td>Subtotal</td><td class="val">${fmt(p.renta + totalExtra)}</td></tr>` : ''}
${abonos}
${p.abonosRenta.length ? `<tr class="subtotal"><td>Saldo de renta</td><td class="val">${fmt(p.saldoRentaPendiente)}</td></tr>` : ''}
${p.depositoPendiente > 0 ? `<tr><td class="section-title" colspan="2">Depósito</td></tr><tr><td class="indent">Depósito pendiente</td><td class="val">${fmt(p.depositoPendiente)}</td></tr>` : ''}
</table>
${totalGeneral > 0 ? `<div class="total-box"><div class="t-label">Total que debe</div><div class="t-val">${fmt(totalGeneral)}</div></div>` : '<div class="paid-box">✓ Sin adeudo — al corriente</div>'}
<div class="footer">Generado el ${hoy}</div></body></html>`
}

/** Abre el recibo en una pestaña y lanza el diálogo de impresión. */
export function imprimirHtml(html: string): boolean {
  const url = URL.createObjectURL(new Blob([html], { type: 'text/html' }))
  const win = window.open(url, '_blank')
  if (!win) { URL.revokeObjectURL(url); return false }
  setTimeout(() => { win.print(); URL.revokeObjectURL(url) }, 300)
  return true
}
