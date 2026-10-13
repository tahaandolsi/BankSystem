# Payment–Transaction RabbitMQ result channel

This configuration is prepared for a dedicated RabbitMQ vhost. The vhost is `/bank-financial`; an administrator must create it and provision its users and topology. The applications do not create users or change broker ACLs.

## Topology

- Results exchange: durable topic exchange `banksystem.payment.results.v1`.
- Completion routing key: `bank.payment.completed.v1`.
- Rejection routing key: `bank.payment.rejected.v1`.
- Durable Payment queues to provision:
  - `payment.transaction.completed.v1`, bound to the completion key.
  - `payment.transaction.rejected.v1`, bound to the rejection key.
- Each queue should dead-letter rejected messages to an operator-managed DLQ. The Payment listener implementation is currently blocked pending security review; do not enable result consumption until it is present and its retry/DLQ behavior is reviewed.

The Transaction result publisher uses raw JSON with the versioned shared contract body. RabbitMQ message headers carry a stable message id and the Payment id as correlation id. The event body does not contain a user JWT.

## Technical identities and least permissions

The intended identities are `payment-financial-service` and `transaction-financial-service`. They are names for provisioning only; no credentials are stored here.

With topology provisioned by an administrator:

- `transaction-financial-service` needs `write` on `banksystem.payment.results.v1`.
- `payment-financial-service` needs `read` on the two Payment result queues.
- `payment-financial-service` will need `write` on `banksystem.commands.v1` if Payment command publication is enabled later.
- `transaction-financial-service` will need `read` on the Payment command queue only after a secured business consumer is implemented and explicitly enabled.
- Neither application needs `configure` permissions when the administrator creates the exchanges, queues, bindings, and dead-letter topology.

Do not grant these identities access to the existing `/` vhost or to unrelated exchanges and queues. Use TLS and runtime secret injection in shared or production environments.

## Runtime configuration

Transaction result publishing is disabled unless `PaymentRabbit__ResultPublishingEnabled=true`. When enabled, provide `PaymentRabbit__Host`, `PaymentRabbit__Username`, and `PaymentRabbit__Password` through the runtime environment. `PaymentRabbit__VirtualHost` defaults to `/bank-financial` and may be set explicitly. Set `PaymentRabbit__UseSsl=true` for a TLS connection in shared or production environments. The default Transaction–Account bus continues to use its existing configuration and connection.

The additive table script `Services/Transaction/database/migrations/20261012_create_payment_financial_result_outbox.sql` must be reviewed and applied manually before enabling result publishing on an existing Transaction database. It has not been applied by this development step.

## Activation blockers

- The Payment result listener was not added because automatic review rejected the proposed change: it considered the then-proposed no-requeue listener unsafe for transient errors and treated the dedicated vhost change as a restricted security-boundary change. Do not enable `PAYMENT_RABBIT_RESULT_CONSUMPTION_ENABLED` until an approved bounded retry and DLQ implementation is available.
- The Payment command producer is not currently creating Rabbit outbox rows, and Transaction's intentionally failing command consumer remains unregistered. No Payment-to-Transaction financial command is active.
- Transaction's result outbox can publish more than once if the process stops after broker acceptance but before recording `Published`. The Payment consumer must keep the same-transaction/status handling idempotent.
- An administrator must provision the result queues before enabling the Transaction relay. Publisher confirmation alone does not prove that a topic-exchange message had a matching queue.
- The result outbox table is additive and manually managed. Existing Transaction startup schema creation does not update an already-created database with this table.
