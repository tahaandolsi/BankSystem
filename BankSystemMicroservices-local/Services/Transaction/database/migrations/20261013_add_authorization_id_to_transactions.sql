-- Apply manually after confirming the deployed Transaction schema.
ALTER TABLE public."Transactions"
ADD COLUMN IF NOT EXISTS "AuthorizationId" uuid NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Transactions_AuthorizationId"
ON public."Transactions" ("AuthorizationId")
WHERE "AuthorizationId" IS NOT NULL;
