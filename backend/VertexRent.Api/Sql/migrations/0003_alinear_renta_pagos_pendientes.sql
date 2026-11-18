-- Corrige pagos que quedaron con la renta anterior: el mes en curso (y siguientes) sin confirmar debe cobrar
-- la renta vigente del inquilino. Los meses pasados conservan el monto con el que se crearon.
UPDATE pagos p
SET monto = i.renta
FROM inquilinos i
WHERE i.id = p.inquilino_id
  AND i.estado = 'activo'
  AND p.confirmado = false
  AND p.periodo >= to_char(CURRENT_DATE, 'YYYY-MM')
  AND p.monto <> i.renta;

-- Si lo ya abonado cubre la renta nueva, el mes queda pagado.
UPDATE pagos p
SET confirmado = true, confirmado_en = NOW()
WHERE p.confirmado = false
  AND p.periodo >= to_char(CURRENT_DATE, 'YYYY-MM')
  AND p.monto > 0
  AND COALESCE((SELECT SUM(a.monto) FROM abonos_pago a WHERE a.pago_id = p.id), 0) >= p.monto - 0.01;
