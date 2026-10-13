-- Apply manually after an explicit deployment approval. This script is additive.
CREATE TABLE IF NOT EXISTS public."AccountTransferAuthorizations" (
    "AuthorizationId" uuid NOT NULL,
    "RequestedByUserId" uuid NOT NULL,
    "CustomerId" uuid NOT NULL,
    "RequestId" uuid NOT NULL,
    "BusinessRequestId" uuid NOT NULL,
    "SourceAccountId" uuid NOT NULL,
    "BeneficiaryAccountId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" character(3) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "Status" character varying(16) NOT NULL,
    "ConsumedByTransactionId" uuid NULL,
    CONSTRAINT "PK_AccountTransferAuthorizations" PRIMARY KEY ("AuthorizationId"),
    CONSTRAINT "FK_AccountTransferAuthorizations_Accounts_SourceAccountId"
        FOREIGN KEY ("SourceAccountId") REFERENCES public."Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AccountTransferAuthorizations_Accounts_BeneficiaryAccountId"
        FOREIGN KEY ("BeneficiaryAccountId") REFERENCES public."Accounts" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "CK_AccountTransferAuthorizations_ValidOperation" CHECK (
        "CustomerId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "RequestId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "BusinessRequestId" <> '00000000-0000-0000-0000-000000000000'::uuid
        AND "SourceAccountId" <> "BeneficiaryAccountId"
        AND "Amount" > 0
        AND btrim("Currency") IN ('USD', 'EUR', 'GBP')
        AND "ExpiresAt" > "CreatedAt"
    ),
    CONSTRAINT "CK_AccountTransferAuthorizations_StatusAndTransaction" CHECK (
        ("Status" = 'Consumed' AND "ConsumedByTransactionId" IS NOT NULL)
        OR ("Status" IN ('Active', 'Revoked') AND "ConsumedByTransactionId" IS NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AccountTransferAuthorizations_CustomerId_RequestId"
    ON public."AccountTransferAuthorizations" ("CustomerId", "RequestId");

CREATE INDEX IF NOT EXISTS "IX_AccountTransferAuthorizations_CustomerId_BusinessRequestId"
    ON public."AccountTransferAuthorizations" ("CustomerId", "BusinessRequestId");

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AccountTransferAuthorizations_ConsumedBusinessRequest"
    ON public."AccountTransferAuthorizations" ("CustomerId", "BusinessRequestId")
    WHERE "Status" = 'Consumed';

CREATE UNIQUE INDEX IF NOT EXISTS "UX_AccountTransferAuthorizations_ConsumedByTransactionId"
    ON public."AccountTransferAuthorizations" ("ConsumedByTransactionId")
    WHERE "ConsumedByTransactionId" IS NOT NULL;
