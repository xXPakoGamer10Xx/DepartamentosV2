-- Monto semanal pactado cuando el depósito se paga en modalidad semanal (Opción 4, por defecto $1,000.00).
ALTER TABLE inquilinos ADD COLUMN IF NOT EXISTS deposito_monto_semanal NUMERIC(10,2);
