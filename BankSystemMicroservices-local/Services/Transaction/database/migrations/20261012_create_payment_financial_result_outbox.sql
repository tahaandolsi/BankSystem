-- Apply manually after approval and before enabling Payment result publishing.
CREATE TABLE IF NOT EXISTS public."PaymentFinancialResultOutboxMessages" (
    "Id" uuid PRIMARY KEY,
    "TransactionId" uuid NOT NULL UNIQUE,
    "PaymentId" uuid NOT NULL,
    "EventType" varchar(16) NOT NULL CHECK ("EventType" IN ('Completed', 'Rejected')),
    "RoutingKey" varchar(100) NOT NULL,
    "PayloadJson" text NOT NULL,
    "Status" varchar(20) NOT NULL CHECK ("Status" IN ('Pending', 'Processing', 'Published', 'Dead')),
    "AttemptCount" integer NOT NULL DEFAULT 0,
    "NextAttemptAtUtc" timestamp with time zone NOT NULL,
    "LockUntilUtc" timestamp with time zone NULL,
    "LastError" varchar(1000) NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "PublishedAtUtc" timestamp with time zone NULL
);

CREATE INDEX IF NOT EXISTS "IX_PaymentFinancialResultOutboxMessages_Ready"
    ON public."PaymentFinancialResultOutboxMessages" ("Status", "NextAttemptAtUtc", "LockUntilUtc");
