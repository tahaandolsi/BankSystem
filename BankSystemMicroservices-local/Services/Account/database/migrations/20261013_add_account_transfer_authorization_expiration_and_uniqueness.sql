-- Apply manually after the initial authorization table script and an authorized data review.
-- Existing rows are preserved. Resolve any conflicting rows before creating the unique index.
ALTER TABLE public."AccountTransferAuthorizations"
    DROP CONSTRAINT IF EXISTS "CK_AccountTransferAuthorizations_ValidOperation";

ALTER TABLE public."AccountTransferAuthorizations"
    ADD CONSTRAINT "CK_AccountTransferAuthorizations_ValidOperation" CHECK (
        "AuthorizationId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "RequestedByUserId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "CustomerId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "RequestId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "BusinessRequestId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "SourceAccountId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "BeneficiaryAccountId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "SourceAccountId" <> "BeneficiaryAccountId"
        AND "Amount" > 0
        AND btrim("Currency") IN ('USD', 'EUR', 'GBP')
        AND "ExpiresAt" > "CreatedAt"
    );

ALTER TABLE public."AccountTransferAuthorizations"
    DROP CONSTRAINT IF EXISTS "CK_AccountTransferAuthorizations_StatusAndTransaction";

ALTER TABLE public."AccountTransferAuthorizations"
    ADD CONSTRAINT "CK_AccountTransferAuthorizations_StatusAndTransaction" CHECK (
        ("Status" = 'Consumed'
            AND "ConsumedByTransactionId" IS NOT NULL
            AND "ConsumedByTransactionId" <> '00000000-0000-0000-0000-000000000000'::uuid)
        OR ("Status" IN ('Active', 'Revoked', 'Expired')
            AND "ConsumedByTransactionId" IS NULL)
    );

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AccountTransferAuthorizations_ActiveOrConsumedBusinessRequest"
    ON public."AccountTransferAuthorizations" ("CustomerId", "BusinessRequestId")
    WHERE "Status" IN ('Active', 'Consumed');
