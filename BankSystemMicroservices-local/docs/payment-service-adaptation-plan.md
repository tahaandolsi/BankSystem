# Plan d’adaptation du Payment Service

## 1. Objectif et décisions

Ce document prend en compte `docs/fraud-integration-analysis.md`, `docs/fraud-integration-contracts.md` et la demande d’architecture jointe. Il définit une cible et une migration future ; aucun code, service, dépôt, workflow existant ou algorithme n’est modifié ici.

Le Payment Service existant doit être conservé et adapté. Il devient l’unique propriétaire du cycle de vie des **Payments**. Transaction Service demeure propriétaire/orchestrateur des opérations bancaires internes (dépôt, retrait, transfert). Account Service demeure l’unique propriétaire des soldes et seul service pouvant les modifier. Il n’y a ni deuxième Payment Service, ni accès direct à la base Account, ni appel Payment→Account pour appliquer les fonds.

Payment crée et suit une intention de paiement, orchestre l’évaluation fraude, puis après autorisation fraude demande à Transaction Service l’opération bancaire correspondante. Transaction valide et orchestre l’application financière. Account applique débit/crédit et publie son résultat. Un statut Payment APPROVED signifie « autorisé par la politique fraude à poursuivre », pas « argent débité » ni « paiement terminé ».

```text
Angular → YARP Gateway → Payment Service
                              │ FraudEvaluationRequested
                              ▼
                    Fraud Detection → Rules / Redis / ML
                              ├─ APPROVED ───────────────┐
                              ├─ REVIEW → Fraud Case ────┤ décision humaine
                              └─ BLOCKED                 │
                                                        ▼ APPROVED seulement
                                              Transaction Service
                                                        │ FundsApplicationRequested
                                                        ▼
                                                Account Service
                                                        │ FundsApplied / Rejected
                                                        ▼
                                TransactionCompleted / TransactionFailed
                                                        │
                                                        ▼
                                              Payment Service → état final

Account publie aussi les faits métier confirmés à Movement, Notification et Reporting.
```

## 2. Analyse du Payment Service existant

Le prototype est Spring Boot 3.2.5 / Java 17, JPA, PostgreSQL, Kafka. Il expose `POST /api/v1/transactions` et `GET /api/v1/transactions/{transactionId}`. Il persiste une entité `Transaction` à `PENDING`, publie l’entité à Kafka puis répond 202. `VerdictListener` consomme le topic `approved` (identifiant texte) et `fraud-alerts` (JSON `FraudAlert`) et met le statut à `APPROVED`, `REVIEW` ou `BLOCKED`.

Le service ne traite actuellement ni compte bancaire, ni montant/devise bancaire validé, ni solde, ni transaction interne. Il représente une demande simulée et son statut, mais son modèle est nommé `Transaction`, ce qui chevauche Transaction Service. `TransactionProducer` publie un objet Java du domaine directement ; aucun contrat stable versionné, correlation/causation ID ou clé d’idempotence métier n’est présent. `KafkaProducerConfig` active `acks=all` et idempotence producteur, mais le code n’attend pas le résultat de `send`. Persistance PostgreSQL puis publication Kafka ne sont pas atomiques. `VerdictListener` ne valide pas les transitions et peut écraser un état. `TransactionRepository` ne fournit que les opérations JPA standard.

`application.yml` pointe vers `frauddb` avec les identifiants communs au service fraude et `ddl-auto: update`; la DB et l’identité doivent être dédiées. Le compose fraude ne contient ni image ni Dockerfile pour Payment : le README le lance localement via Maven. Aucun test Java/Python n’a été repéré, bien que le POM contienne `spring-boot-starter-test`.

## 3. Évaluation des composants existants

| Élément | Réutilisable | À adapter/remplacer | Reste dans Payment / se déplace |
|---|---|---|---|
| `PaymentController` | API REST asynchrone 202 et statut | `/transactions` devient API Payments; DTO versionné, auth, idempotency key, erreurs et ownership | Payment crée/consulte son Payment; aucune logique de fraude |
| `Transaction` | UUID, montant précis, timestamp, état persistant | Agrégat conceptuel `Payment`, comptes/type/devise, état, version optimiste, idempotency/correlation, historique | Statut métier Payment reste; score, raisons et versions fraude sont Fraud-owned |
| `TransactionRequest` | Validation Bean Validation | DTO Payment; identité non fournie par client comme preuve; comptes/merchant/montant/devise validés | Données métier utiles restent; features sensibles minimisées vers Fraud |
| `TransactionRepository` | Adaptateur JPA local | DB Payment, migrations, contraintes uniques/idempotence, audit d’états | Persistance Payment seulement |
| `TransactionProducer` | Rôle d’adaptateur de publication | Remplacer objet Java/Kafka ad hoc par outbox et contrat RabbitMQ JSON | Payment orchestre la demande d’évaluation et l’ordre Transaction |
| `VerdictListener` | Réception asynchrone | Consumer `FraudEvaluationCompleted`/`FraudReviewResolved`, Inbox et validation transition; APPROVED déclenche Transaction, pas le solde | Payment garde une projection minimale et ses références; décision source reste Fraud |
| `KafkaProducerConfig` | Fiabilité comme exigence | Retrait Kafka après bascule; configuration AMQP et interop MassTransit à tester | Aucun scoring ici |
| `application.yml` | Profils/config externalisée | DB dédiée, secrets externes, Rabbit, health, migrations | Ressources propres Payment |
| `pom.xml` | Spring Web, Validation, JPA, PostgreSQL, tests | Migrations, tracing/metrics, AMQP; garder Kafka jusqu’à fin de migration | Runtime Payment |
| Docker | Déploiement conteneur futur | Dockerfile/compose bancaire, readiness, secrets | Image de l’unique service Payment |

Ne pas promouvoir le schéma `transactions` prototype tel quel comme vérité définitive : conserver les lignes en archive ou migrer vers `payments`, avec mapping stable `legacyTransactionId → paymentId`.

## 4. Responsabilités futures et exclusions

**Payment Service :** API authentifiée de création/consultation, agrégat et états Payment, historique métier, idempotence/expiration/annulation définie, orchestration Payment→Fraud→revue→Transaction, références `fraudEvaluationId` et `bankTransactionId`. Il ne possède pas le solde, ne lit pas Account DB, n’écrit aucun mouvement, n’exécute pas débit/crédit, et ne contient ni règles fraude, ni score ML, ni audit détaillé fraude.

Retirer conceptuellement du Payment les notions d’« opération bancaire » actuellement nommée Transaction, les payloads d’entité JPA comme contrats, les topics ad hoc `approved`/`fraud-alerts` et toute logique d’approbation par défaut. Fraud Detection possède règles, score, Redis/features, modèle, décision et audit. Fraud Case Service porte le workflow humain si réellement séparé. Transaction possède l’opération bancaire, son idempotence et orchestre Account. Account possède soldes et mutations. Movement/Notification/Reporting sont des projections.

## 5. Emplacement et technologie

Payment est un service de premier niveau derrière YARP, aux côtés de Security et Account. Gateway route les APIs et ne porte pas d’état métier. Après fraude, Payment appelle/commande Transaction; seul Transaction demande l’application des fonds à Account.

| Option | Avantages | Coûts/risques | Avis |
|---|---|---|---|
| Java/Spring Boot conservé | Préserve service et données; migration moins intrusive; vraie séparation de déploiement | Stack mixte; contrats interlangages; interop Spring AMQP/MassTransit à prouver | **Recommandé d’abord**, si support Java et contract tests AMQP sont validés |
| Réimplémenter en .NET | Alignement avec conventions, MassTransit/outbox et observabilité bancaires | Réécriture, perte possible de comportements/données, maintien temporaire de deux implémentations | Option si stack unique exigée; migration de l’unique service/source, jamais deuxième Payment |
| Autre runtime | Aucun bénéfice distinct constaté | Coût d’exploitation sans besoin identifié | Non recommandé |

Choix proposé : conserver le repository et runtime Java/Spring Boot, contrats JSON versionnés sur RabbitMQ et outbox fiable. MassTransit est .NET; Java n’a pas à utiliser sa bibliothèque si l’interop protocolaire est testée : topology exchange/queue/routing, content type, headers, sérialisation, retries et DLQ. Si cela échoue ou coûte trop cher, migrer le même Payment Service vers .NET avant de basculer l’autorité; ne jamais accepter des écritures dans les deux instances.

## 6. DB et sources de vérité

Payment possède une DB PostgreSQL dédiée (ex. `DemoBank_Payments`), rôle SQL distinct, migrations versionnées, tables de paiements, historique d’états, idempotency records, outbox et inbox. Une instance PostgreSQL commune en local est acceptable, pas une base/schéma partagé ni credentials communs. Fraud a DB/Redis propres; Transaction et Account gardent leurs DB. Payment est l’unique source de vérité Payment. Account DB/agrégat reste l’unique source de vérité de solde. Les autres services ne détiennent que références/projections.

Préserver les données par sauvegarde/inventaire et migration d’IDs/états vers Payment, `legacyPaymentReference`, archive en lecture seule selon rétention et rapprochement des volumes/empreintes. Ne pas inventer versions ou décisions fraude absentes. Pendant migration, un seul writer autoritaire à la fois; pas de dual-write entre deux DB Payment.

## 7. Machine à états Payment

`APPROVED` doit être un état de poursuite, non un succès financier. `REVIEW` attend résolution humaine et n’atteint pas Transaction. Payment ne se termine qu’après le fait Transaction fondé sur confirmation Account.

```text
CREATED → FRAUD_CHECK_PENDING
  ├─ FraudEvaluationCompleted(APPROVED) → APPROVED
  │    └─ demande bancaire → TRANSACTION_PENDING
  │         ├─ TransactionCompleted → COMPLETED [terminal]
  │         └─ TransactionFailed/Rejected → FAILED [terminal métier]
  ├─ FraudEvaluationCompleted(REVIEW) → MANUAL_REVIEW
  │    ├─ FraudReviewResolved(APPROVED) → APPROVED → TRANSACTION_PENDING → COMPLETED/FAILED
  │    └─ FraudReviewResolved(BLOCKED) → BLOCKED [terminal fraude]
  └─ FraudEvaluationCompleted(BLOCKED) → BLOCKED [terminal fraude]

États pending peuvent devenir EXPIRED/CANCELLED seulement selon règles explicites.
```

202 peut être renvoyé si Payment et outbox sont persistés. Erreur technique laisse un état pending; expiration ne vaut jamais approbation. Nouvelle tentative après terminal = nouveau Payment relié à l’ancien.

## 8. Contrats, commandes et événements

JSON camelCase versionné (`contractVersion`), UUID, UTC RFC3339, décimal sous forme chaîne et devise ISO 4217. Tous portent `messageId`, `correlationId`, `causationId`, `occurredAt`; commandes portent `commandType`, événements `eventType`. Décisions fraude incluent `paymentId`, `evaluationId`, APPROVED/REVIEW/BLOCKED, `riskScore`, `reasons`, `modelVersion`, `rulesVersion`, `evaluatedAt`.

| Contrat | Nature; producteur → consommateur | Données / rôle |
|---|---|---|
| `FraudEvaluationRequested` | commande; Payment → Fraud | paymentId, evaluationId, customer, montant/devise, merchant et features minimales; ne signifie pas décision |
| `FraudEvaluationCompleted` | événement; Fraud → Payment | décision complète et versions; toutes décisions, APPROVED compris; Fraud DB est source |
| `FraudReviewCaseRequested` / `FraudReviewCaseOpened` | commande / événement; Fraud → Fraud Case | Créer un dossier; distinguer demande de fait créé |
| `FraudReviewResolved` | événement; Fraud Case → Fraud et Payment | case/payment/evaluation IDs, APPROVED/BLOCKED, raisons, acteur référencé et audit |
| `BankTransactionRequested` | commande; Payment → Transaction | paymentId, transactionId demandé ou référence, type, comptes, amount/currency, idempotence; déclenché après APPROVED final |
| `TransactionAccepted` | événement; Transaction → Payment | Opération bancaire créée durablement et son ID; alternativement réponse HTTP 202 idempotente, mais ne pas doubler le canal |
| `TransactionCompleted` | événement; Transaction → Payment/projections | Fait final après `FundsApplied` Account |
| `TransactionFailed`/`TransactionRejected` | événement; Transaction → Payment | Rejet bancaire définitif et code stable |
| `FundsApplicationRequested` | commande; Transaction → Account | Commande détaillée dans `fraud-integration-contracts.md`; Payment ne la produit pas |
| `FundsApplied`/`FundsApplicationRejected` | événements; Account → Transaction | Résultat autoritatif du mouvement; Payment ne le consomme pas directement |

`BankTransactionRequested` peut être un message ou une API interne idempotente; choisir un seul chemin pour une demande. Recommandation : RabbitMQ/outbox et `TransactionAccepted`, si interop validée. Payment ne contourne pas Transaction avec un appel mutationnel à Account.

## 9. Échanges et Kafka→RabbitMQ

Payment→Fraud est asynchrone via `FraudEvaluationRequested`, puis `FraudEvaluationCompleted`. Fraud persiste décision/audit et outbox avant publication. Payment valide IDs/état actif. REVIEW ouvre la revue; BLOCKED termine sans transaction bancaire. Pas de double chemin événement + API pour une même évaluation.

Payment→Transaction intervient après APPROVED final (automatique ou humain). Transaction vérifie sa propre règle bancaire/idempotence, produit `FundsApplicationRequested` à Account et attend le résultat. Account publie `FundsApplied` ou rejet; Transaction produit le fait final. Payment ne lit pas le solde.

Migration progressive :

1. Sauvegarder DB et Kafka; inventorier offsets, topics, états et payloads.
2. Geler le contrat prototype et mapper `transactions` vers `FraudEvaluationRequested`, `approved`/`fraud-alerts` vers une seule forme `FraudEvaluationCompleted`.
3. Adapter la frontière Fraud à RabbitMQ sans changer les algorithmes. Choisir un chemin d’autorité par payment/evaluationId; l’autre est shadow/read-only.
4. Migrer les données et l’unique Payment writer, activer outbox Rabbit par cohortes et rapprocher résultats.
5. Garder Kafka en lecture/historique temporaire. Tout bridge éventuel doit être borné, idempotent, observable et non autoritaire; pas de double publication durable.
6. Réconcilier les opérations non terminales/offsets, couper Kafka pour ce parcours puis archiver selon rétention.

Ne pas supposer que deux clients RabbitMQ sont interopérables sans essais de topology, headers MassTransit, JSON, ack/retry/DLQ. Outbox rend atomiques mutation et message; Inbox rend les consommateurs idempotents. Rabbit indisponible : persister Payment+outbox et répondre pending/202; jamais annoncer succès.

## 10. Retries, erreurs, idempotence

Payment API exige clé idempotence scope tenant/client + opération et empreinte payload. Même clé/même payload retourne le même Payment; payload différent = 409. Outbox conserve `messageId`; Inbox déduplique. Fraud déduplique `(paymentId,evaluationId)`; une réévaluation est explicitement un nouvel ID et une décision auditée. `BankTransactionRequested` porte une clé stable dérivée de paymentId; Transaction impose unicité paymentId et renvoie la même opération en retry. Account impose la clé transaction-opération atomiquement avec mutations du solde. Un redelivery ne peut donc appliquer qu’une fois le débit/crédit.

Retry exponentiel borné avec jitter, timeout/circuit breaker pour pannes transitoires. Rejets métier (compte fermé, fonds insuffisants, devise) ne sont pas retryés. Résultat technique inconnu : consulter/rejouer avec mêmes IDs/clés, jamais créer un deuxième Payment/Transaction. Après limite, DLQ/quarantaine masquée des PII, alerte et replay contrôlé conservant les identifiants. Valeur/version/ID invalide ou décision obsolète ne déclenche jamais Transaction.

Fraud indisponible = `FRAUD_CHECK_PENDING`, fail-closed, aucune demande bancaire; après SLA expiration/intervention explicite, jamais APPROVED par défaut. ML fallback est décidé et audité dans Fraud, pas par Payment. Transaction/Account transitoirement indisponible laisse paiement pending; rejet définitif devient FAILED.

## 11. Correlation, versioning et audit

Gateway crée/valide UUID `correlationId`, le renvoie en header, Payment le conserve. Chaque message garde le même correlationId; `causationId` référence le message immédiat consommé. Propager aussi `paymentId`, `evaluationId`, `transactionId`, W3C trace context et headers MassTransit `CorrelationId`, `ConversationId`, `InitiatorId`, `MessageId`. Ces identifiants ne sont pas des clés d’idempotence ni des PII.

`contractVersion: 1`; ajout optionnel compatible, changement de sens/type/obligation = v2 et période de coexistence planifiée. Les consommateurs ignorent les champs inconnus; décision inconnue est mise en quarantaine et ne signifie jamais APPROVED. JSON explicite, pas de classe Java/CLR comme contrat interlangage. Contract-tests avec fixtures communes.

Payment garde historique append-only des états, acteur, IDs/corrélation/causation, timestamp, évaluation et transaction référencées. Fraud garde détail, score, raisons, versions, mode fallback et décision humaine. Transaction garde validation/résultat; Account garde application et idempotence. Outbox relie mutation à publication; aucun service n’accède aux tables d’audit d’un autre. Minimiser données sensibles et définir rétention/accès.

## 12. Tests à prévoir

- Unitaires : transitions, idempotence/concurrence, état terminal, montants/devise et payload invalide.
- Contract tests Java↔.NET : fixtures JSON, headers, topology Rabbit, versions, correlation/causation et décisions.
- Outbox/Inbox : crash après commit, redelivery, ACK, publication durable.
- Parcours : APPROVED→Transaction→Account→COMPLETED; REVIEW sans fonds jusqu’à résolution; REVIEW-BLOCKED et BLOCKED sans commande; Account rejet; Fraud/broker down; DLQ/replay.
- Invariant Account : un effet maximum par transaction malgré doubles messages/concurrence; aucun accès DB Account depuis Payment.
- Migration : volumes, IDs, statuts, collisions d’idempotence, non terminés, rapprochement/rollback.
- Sécurité Gateway, authorization/ownership Payment, service-to-service auth et absence de secrets.

Aucun test n’a été exécuté pour ce document.

## 13. Fichiers/classes à modifier ultérieurement

### fraud-detection-project — Payment existant

- `payment-service/src/main/java/com/paymentservice/controller/PaymentController.java` : API Payments.
- `.../model/Transaction.java` : agrégat Payment et migration contrôlée.
- `.../model/TransactionRequest.java` : DTO création Payment.
- `.../repository/TransactionRepository.java` : DB, idempotence, recherche d’état.
- `.../service/TransactionProducer.java` : outbox/contrats fraude et commande Transaction.
- `.../service/VerdictListener.java` : consommateurs des décisions/revues, transitions et Inbox.
- `.../config/KafkaProducerConfig.java` : retrait post-bascule et config AMQP.
- `payment-service/src/main/resources/application.yml`, `payment-service/pom.xml` : DB dédiée, secrets, broker, migrations/observabilité/dépendances.
- Ajouter Dockerfile Payment (absent aujourd’hui), migrations, contrats, historique/outbox/inbox et tests.

Frontière Fraud future, sans changement d’algorithme : `fraud-detection-service/.../consumer/FraudConsumer.java`, `model/TransactionEvent.java`, `model/FraudAlert.java`, `service/FraudAlertRepository.java`, `config/KafkaConfig.java`, `application.yml`, `pom.xml`. `RuleEngine`, `RestMlScorer`, `StubMlScorer`, `UserProfileService` restent Fraud-owned. Fraud Case Service/revue humaine n’est pas présent dans le dépôt analysé.

### BankSystemMicroservices-local — intégration

- Gateway: `Gateway/ApiGateway/appsettings*.json`, `Middlewares/CorrelationIdMiddleware.cs`.
- Aucun projet Payment n’existe aujourd’hui. Hypothèse : garder le code dans son repository et l’intégrer par contrats, sans fusion de dépôts; décider plus tard où son déploiement sera orchestré.
- Transaction : `Services/Transaction/src/Transaction.Api/Controllers/TransactionsController.cs`, `Transaction.Application/Commands/CreateTransactionCommand.cs`, `Handlers/TransactionHandlers.cs`, `Transaction.Domain/Entities/Transaction.cs`, `Enums/TransactionStatus.cs`, `Transaction.Infrastructure/Data/TransactionDbContext.cs`, `DependencyInjection.cs`, `Messaging/TransactionEventPublisher.cs`.
- Contrats : `Common/src/BankSystem.Shared.Domain/Events/Transaction/TransactionCompletedIntegrationEvent.cs`, `Events/Account/AccountTransactionAppliedIntegrationEvent.cs` et nouveaux contrats de `fraud-integration-contracts.md`. Ne pas utiliser `TransactionCompletedIntegrationEvent` comme commande Account.
- Account : `Services/Account/src/Account.Infrastructure/Consumers/TransactionCompletedConsumer.cs`, `Services/Account/src/Account.Domain/Entities/Account.cs`; mutation idempotente et résultat tout-ou-rien.
- Projections : `Services/Movement/.../TransactionCompletedConsumer.cs`, `Services/Reporting/.../TransactionCompletedConsumer.cs`, `Services/Notification/.../AccountTransactionAppliedConsumer.cs`.
- Infra : `Common/src/BankSystem.Shared.Infrastructure/Extensions/OutboxServiceCollectionExtensions.cs`, `CrossServiceConsumerExtensions.cs`, `Configuration/MassTransitOptions.cs`, `docker-compose/docker-compose.yml`, `docker-compose/postgres/init/01-create-databases.sql`, `AppHost/AppHost.cs`.

Tous ces fichiers sont des candidats futurs, pas des changements actuels.

## 14. Risques, stratégie de tests et étapes de migration

Risques principaux : confusion Payment/Transaction et IDs; interop MassTransit/Spring AMQP; double writer pendant migration; absence d’outbox actuelle; statut APPROVED mal interprété comme transfert exécuté; identité client non fiable; Fraud indisponible laissant des pending; transfert Account non atomique; historiques prototype sans versions fraude; secrets compose en clair; tests fraude absents; revue manuelle non implémentée.

Étapes recommandées :

1. Valider produits/types de Payment, mapping `paymentId`/`transactionId`, identité, opérations bancaires, SLA/expiration et propriétaire du Fraud Case.
2. Geler contrats JSON v1 et valider interop AMQP Java/.NET, topology, headers, erreurs et DLQ.
3. Concevoir DB/migrations Payment, archive des anciennes lignes et bascule d’un writer unique.
4. Adapter Payment localement : agrégat/états, clé idempotence, historique, outbox/inbox, sécurité et tests.
5. Adapter seulement les frontières Fraud aux contrats; garder Kafka legacy isolé jusqu’à bascule, algorithmes inchangés.
6. Adapter Transaction à la demande Payment et au résultat Account; Account garantit mutation idempotente atomique; remapper projections aux faits confirmés.
7. Déployer isolé, contract/integration/failure tests, shadow sans double mutation, migration et rapprochement de données.
8. Basculer cohortes vers RabbitMQ, surveiller outbox lag, DLQ, divergences d’état et effets Account; étendre après rapprochement.
9. Réconcilier paiements non terminés/offsets puis désactiver Kafka pour ce parcours et archiver selon politique.

Aucune de ces étapes n’est effectuée ici. Le Payment Service existant est préservé comme base d’adaptation et demeure l’unique Payment Service cible.
