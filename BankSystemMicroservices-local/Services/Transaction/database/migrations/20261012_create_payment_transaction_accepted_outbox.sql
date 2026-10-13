-- Apply manually after approval and before enabling Payment acceptance publishing.
CREATE TABLE IF NOT EXISTS public."PaymentTransactionAcceptedOutboxMessages" (
    "Id" uuid PRIMARY KEY,
    "PaymentId" uuid NOT NULL UNIQUE,
    "TransactionId" uuid NOT NULL UNIQUE,
    "PayloadJson" text NOT NULL,
    "Status" varchar(20) NOT NULL CHECK ("Status" IN ('Pending', 'Processing', 'Published', 'Dead')),
    "AttemptCount" integer NOT NULL DEFAULT 0,
    "NextAttemptAtUtc" timestamp with time zone NOT NULL,
    "LockUntilUtc" timestamp with time zone NULL,
    "LastError" varchar(1000) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "PublishedAtUtc" timestamp with time zone NULL
);

CREATE INDEX IF NOT EXISTS "IX_PaymentTransactionAcceptedOutboxMessages_Ready"
    ON public."PaymentTransactionAcceptedOutboxMessages" ("Status", "NextAttemptAtUtc", "LockUntilUtc");
