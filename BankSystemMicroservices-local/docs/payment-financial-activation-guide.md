# Guide de préparation — virements Payment / Account

Ce document décrit les contrôles à effectuer avant une activation coordonnée. Il ne demande pas d'exécuter les migrations ni d'activer les flags. Aucun secret ne doit être ajouté à ce fichier.

## 1. Ordre des évolutions de schéma

Les schémas sont séparés. Vérifier chaque base existante et son historique avant d'appliquer quoi que ce soit ; ne pas supposer qu'un script additif prouve la compatibilité avec une base donnée.

### PaymentDB — Java

Examiner les migrations Flyway dans cet ordre : `V1` à `V7` du dossier `payment-service/src/main/resources/db/migration`.

Elles établissent les tables `payments`, `payment_outbox_events`, `payment_rabbit_outbox_events` et `payment_financial_result_inbox`, puis ajoutent l'empreinte de requête, les identités propriétaire, et les champs d'autorisation de virement. Vérifier notamment `uq_payments_customer_idempotency`, `uq_payments_owner_client_idempotency`, `ix_payments_legacy_idempotency`, `ix_payment_outbox_ready`, `uq_payment_rabbit_outbox_event`, `ix_payment_rabbit_outbox_ready`, `ix_payment_financial_result_inbox_ready` et `uq_payments_transfer_authorization`. Flyway est désactivé dans la configuration actuelle ; Hibernate utilise `ddl-auto: validate`. La version applicative exige donc que le schéma correspondant soit déjà présent.

### AccountDB — .NET

Vérifier d'abord les migrations EF existantes pour le schéma de base et les tables Outbox/Inbox MassTransit. Pour le parcours virement, contrôler ensuite l'ordre des scripts :

1. `202610110001_AddAccountTransactionReceipts` puis `202610110002_AddAccountTransactionReceiptOutcome` : reçu durable, résultat et motif.
2. `20261012_add_account_transfer_authorizations.sql` : table `AccountTransferAuthorizations` et ses clés/index.
3. `20261013_add_account_transfer_authorization_expiration_and_uniqueness.sql` : expiration, contraintes et index uniques complémentaires.

Les objets métier attendus sont `AccountTransactionReceipts` (clé primaire `TransactionId`) et `AccountTransferAuthorizations` (clé primaire `AuthorizationId`). Vérifier les index `UX_AccountTransferAuthorizations_CustomerId_RequestId`, `IX_AccountTransferAuthorizations_CustomerId_BusinessRequestId`, `UX_AccountTransferAuthorizations_ActiveOrConsumedBusinessRequest` et `UX_AccountTransferAuthorizations_ConsumedByTransactionId`, ainsi que les contraintes de résultat et de statut. Ces évolutions sont manuelles. Le démarrage d'Account ne doit pas être utilisé pour les appliquer.

### TransactionDB — .NET

Contrôler le schéma de base et les tables MassTransit Outbox, puis les scripts présents dans `Services/Transaction/database/migrations` :

1. `20261012_add_payment_id_to_transactions.sql`.
2. `20261012_create_payment_transaction_accepted_outbox.sql` et `20261012_create_payment_financial_result_outbox.sql`.
3. `20261013_add_authorization_id_to_transactions.sql`.

Vérifier `Transactions.PaymentId` et l'index partiel unique `IX_Transactions_PaymentId`, `Transactions.AuthorizationId` et son index partiel unique `IX_Transactions_AuthorizationId`, ainsi que les tables `PaymentTransactionAcceptedOutboxMessages` et `PaymentFinancialResultOutboxMessages` avec leurs index `IX_PaymentTransactionAcceptedOutboxMessages_Ready` et `IX_PaymentFinancialResultOutboxMessages_Ready`. `EnsureCreated` ne met pas à niveau un schéma déjà existant ; comparer les scripts au schéma réel avant application.

Les listes ci-dessus décrivent les fichiers et dépendances attendus dans le code, pas l'état d'une base. Une sauvegarde, une comparaison de schéma et une approbation séparée sont nécessaires avant toute application.

## 2. Contrat d'identité JWT

Comparer les paramètres effectifs injectés dans Security, YARP et Payment :

- même issuer et même audience attendus par les trois composants ;
- signature HS256 et validation de signature, expiration, issuer et audience ;
- `sub` correspond à l'utilisateur Security ; `clientId` correspond au client bancaire ;
- le secret de signature est injecté par un canal d'exécution approuvé et n'apparaît ni dans le dépôt ni dans les journaux.

Ne recopier aucune valeur secrète dans les journaux ou ce guide. Une configuration présente dans un fichier n'est pas une preuve de la valeur effectivement injectée au lancement.

## 3. Topologie RabbitMQ attendue

Vhost financier dédié : `/bank-financial`.

| Flux | Exchange | Routing key | Queue consommatrice |
| --- | --- | --- | --- |
| Payment vers Transaction | `banksystem.commands.v2` | `bank.transaction.requested.v2` | `transaction.create-from-payment.v2` |
| Transaction vers Account | `transaction-events` | topology d'événement MassTransit | `account-transaction` |
| Account vers Transaction | `account-events` | topology d'événement MassTransit | `transaction-account` |
| Acceptation Transaction vers Payment | `banksystem.payment.acceptance.v1` | `bank.payment.accepted.v1` | `payment.transaction.accepted.v1` |
| Résultat final vers Payment | `banksystem.payment.results.v1` | `bank.payment.completed.v1` ou `bank.payment.rejected.v1` | `payment.financial-results.v1` |

Confirmer les bindings, types d'exchange, durabilité, confirmations publisher, retours des messages non routables, DLX/DLQ et la topologie réelle dans l'environnement. Ne pas déduire leur existence de la seule configuration du code.

## 4. Identités techniques et ACL minimales

Utiliser des identités techniques distinctes par service, limitées au vhost financier. Accorder uniquement les droits nécessaires :

- Payment : publier les commandes v2 et consommer les files d'acceptation/résultat ;
- Transaction : consommer la commande, publier l'acceptation et les résultats, consommer les événements Account ;
- Account : consommer les demandes Transaction et publier les résultats Account.

Limiter les permissions de configuration des exchanges, files et bindings à l'identité ou à l'opération qui en a besoin. Vérifier TLS, rotation des secrets et droits effectifs avec l'administrateur RabbitMQ. Ne pas modifier les ACL dans le cadre de cette préparation.

## 5. Flags à laisser désactivés

Conserver à `false` jusqu'à l'approbation coordonnée :

- Java : `payment.account.transfer-authorization-enabled`, `payment.account.allow-insecure-http`, `payment.rabbit.command-creation-enabled`, `payment.rabbit.command-v2-enabled`, `payment.rabbit.transport-enabled`, `payment.rabbit.financial-publication-enabled`, `payment.rabbit.financial-integration-enabled` et `payment.rabbit.result-consumption-enabled` ;
- Transaction : `PaymentRabbit:CommandV2ConsumerEnabled` et `PaymentRabbit:ResultPublishingEnabled` ;
- Account : `Account:AuthorizedTransferExecutionEnabled`.

Les defaults du code restent fermés. Ne pas activer les consommateurs de commandes sans les résultats et le chemin Account correspondants.

## 6. Prérequis d'activation coordonnée

Avant activation, les responsables doivent confirmer :

1. les schémas et index présents dans chaque base, avec migrations appliquées séparément et sans perte de données ;
2. la configuration JWT effective et le compte de test autorisé ;
3. le vhost, la topologie, les ACL, TLS et les files DLQ ;
4. la disponibilité des Outbox/Inbox et le suivi de leur retard, de leurs erreurs et des messages non routables ;
5. une procédure de rapprochement des commandes, transactions, reçus Account, autorisations consommées et résultats Payment ;
6. une procédure d'arrêt et de retour arrière approuvée avant l'activation des flags.

L'approbation Fraud ou manuelle n'est pas une confirmation bancaire. Une autorisation expirée ne doit pas être renouvelée silencieusement. Une erreur technique ou un résultat de publication ambigu reste à rapprocher ; il ne devient pas un refus métier.

## 7. Messages historiques et résultats incertains

Ne pas rejouer automatiquement les commandes v1, les messages sans version reconnue, les Outbox `DEAD`, ni les messages dont la publication a expiré sans confirmation certaine. Comparer les identifiants d'origine, les statuts locaux, les reçus Account, les autorisations et les références Rabbit disponibles. Toute reprise doit être limitée à un dossier explicitement approuvé ; si le mouvement peut déjà avoir été appliqué, arrêter et escalader.

Le contrat historique `TransactionCompletedIntegrationEvent` reste consommable pour les messages déjà en circulation. Les nouvelles demandes utilisent `TransactionExecutionRequestedIntegrationEvent` et son champ `RequestedAt`. Une demande d'exécution n'est jamais une date de confirmation ; seule la date du résultat Account confirme l'application.

## 8. Revue manuelle Fraud

Le code Payment observé sait placer un paiement en `MANUAL_REVIEW`, mais aucun parcours de décision opérateur sûr n'a été identifié. Ne pas ajouter ni activer une route de décision tant que l'identité, le rôle et les droits d'opérateur ne sont pas établis et vérifiés dans Payment. Ce point bloque la résolution opérationnelle des paiements en revue.

## 9. Retour arrière sans suppression

En cas de problème, arrêter d'abord la création et la publication de nouvelles commandes, puis laisser les consommateurs de résultats traiter les opérations déjà en cours tant que leur état reste sûr. Ne pas supprimer les messages, lignes Outbox, reçus, autorisations ou écritures financières. Ne pas désappliquer une migration qui contient des données. Désactiver les nouveaux flux seulement après un rapprochement des commandes déjà publiées ; conserver les dossiers ambigus pour une résolution manuelle.

Le retour arrière ne consiste jamais à recréditer ou redébiter automatiquement un compte. Tout mouvement correctif doit suivre une opération bancaire distincte, approuvée et auditée.
