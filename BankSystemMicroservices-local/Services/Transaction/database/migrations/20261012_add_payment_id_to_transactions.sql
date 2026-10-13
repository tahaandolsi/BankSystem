-- Apply manually after confirming the deployed Transaction schema.
ALTER TABLE public."Transactions"
ADD COLUMN IF NOT EXISTS "PaymentId" uuid NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Transactions_PaymentId"
ON public."Transactions" ("PaymentId")
WHERE "PaymentId" IS NOT NULL;
