# Plan technique détaillé d’intégration Payment–Fraude–Banque

## 1. Objet et invariants

Ce plan s’appuie sur les trois documents validés : `fraud-integration-analysis.md`, `fraud-integration-contracts.md` et `payment-service-adaptation-plan.md`. Il décrit des travaux futurs seulement : aucun code, service, dépôt, workflow existant, algorithme ou donnée n’est modifié ici.

Le `payment-service` Java existant de `fraud-detection-project` est la base de l’unique Payment Service cible. Payment est seul propriétaire du cycle de vie des Payments; Transaction est seul propriétaire des transactions bancaires (dépôts, retraits, transferts); Account est seul propriétaire des comptes/soldes et seul à les modifier. Aucun accès DB interservices, aucun double writer. Payment n’appelle pas Account pour les fonds.

```text
Angular → YARP → Payment → Fraud Detection (Rules / Redis / ML)
                           ├─ APPROVED → Payment → Transaction → Account
                           ├─ REVIEW → Fraud Case → revue manuelle
                           │                       ├─ APPROVED → Payment → Transaction
                           │                       └─ BLOCKED → Payment BLOCKED
                           └─ BLOCKED → Payment BLOCKED
Account → événements confirmés RabbitMQ → Movement / Notification / Reporting
```

APPROVED fraude signifie poursuivre, pas fonds débités. Payment devient COMPLETED seulement après `TransactionCompleted`, émis sur confirmation `FundsApplied` par Account. REVIEW/BLOCKED ne provoquent aucun mouvement. Seul Transaction émet `FundsApplicationRequested` à Account.

## 2. Analyse précise du Payment prototype

Spring Boot 3.2.5 / Java 17, JPA, PostgreSQL, Kafka. `PaymentController` expose POST/GET `/api/v1/transactions`; il crée une entité `Transaction` PENDING, la persiste, la publie puis répond 202. `Transaction` porte transactionId, userId, amount, merchant, deviceId, location, timestamp, état PENDING/APPROVED/REVIEW/BLOCKED. `TransactionRequest` valide ces champs mais l’identité vient du payload et aucun compte/devise bancaire n’est défini. `TransactionRepository` est un JpaRepository standard.

`TransactionProducer` publie l’entité Java directement sur `transactions`, key=userId. `VerdictListener` consomme `approved` (ID en texte) et `fraud-alerts` (JSON `FraudAlert`), parse ad hoc et écrase le statut sans transition/idempotence. `KafkaProducerConfig` active JSON sans type headers, `acks=all`, idempotence Kafka; le code n’attend pas le résultat de `send`. DB commit et publication ne sont pas atomiques. `application.yml` partage `frauddb`/credentials, utilise `ddl-auto:update`, localhost et SQL logging. `pom.xml` contient Web/JPA/Kafka/Postgres/validation/test. Compose du projet fraude a Kafka, Redis et PostgreSQL mais pas de container/Dockerfile Payment; README lance Maven. Aucun test Java/Python n’a été repéré.

| Fichier/élément | Conserver | Modifier/remplacer ultérieurement | Hors périmètre Payment |
|---|---|---|---|
| `PaymentController` | API et 202 asynchrone, statut | `/api/v1/payments`, auth/ownership, Idempotency-Key, DTO et erreurs | règles fraude, Account |
| `Transaction` | UUID, BigDecimal, timestamp, persistance | Agrégat Payment, statuts/références/version/historique, migration contrôlée | statut bancaire et audit détaillé fraude |
| `TransactionRequest` | validation Bean Validation | DTO Payment, identité fiable, données requises de paiement | confiance au userId fourni |
| `TransactionRepository` | accès JPA | DB Payment, migrations, contraintes uniques/idempotence, transitions | DB Fraud/Account |
| `TransactionProducer` | adaptateur de publication | outbox et commandes JSON Rabbit versionnées | règles/scoring/solde |
| `VerdictListener` | réception asynchrone | consumers `FraudEvaluationCompleted`, `FraudReviewResolved`, résultats Transaction, Inbox/états | décision fraude source |
| `KafkaProducerConfig` | exigences durabilité | Kafka legacy jusqu’au cutover, puis client AMQP | Kafka autoritaire après migration |
| `application.yml` | configuration externalisable | DB dédiée, secrets hors Git, Rabbit/TLS/health/migrations | identifiants frauddb et logs sensibles |
| `pom.xml` | Spring Web, Validation, JPA, PostgreSQL, tests | AMQP, migrations, tracing/metrics; retirer Kafka après réconciliation | dépendances algorithmes Fraud |
| Docker | déploiement futur | Dockerfile absent, config compose/environnement | fusion de repos |

Aucune suppression pendant les étapes de planification. Schéma prototype `transactions` : archiver ou migrer avec mapping stable legacyTransactionId→paymentId, jamais réutiliser en coexistence comme deuxième source de vérité.

## 3. Responsabilités et choix de technologie

Payment futur : API authentifiée, agrégat/état/historique/idempotence du Payment, orchestration Payment→Fraud→Review→Transaction, références `evaluationId` et `bankTransactionId`. Exclure calcul fraude, score ML, détail d’audit Fraud, accès DB Account, mutation des soldes et ledger bancaire.

Fraud possède règles, score, features Redis, modèle, décision et audit automatisé. Fraud Case (à créer plus tard) possède dossiers/revue humaine. Transaction possède opération bancaire et son orchestration/idempotence. Account possède et applique le solde. Movement/Notification/Reporting projettent les faits confirmés.

| Critère | Java 17/Spring conservé | .NET |
|---|---|---|
| Coût/risque | Adaptation ciblée du code existant | Rewrite, validation d’équivalence et migration plus coûteuses |
| Réutilisation | Forte, préserve l’actif | Faible côté code, données migrables |
| RabbitMQ/MassTransit | Spring AMQP possible; interop à prouver | MassTransit natif dans bancaire |
| Tests/observabilité/maintenance | Stack mixte à gouverner | Conventions/outils plus cohérents |

**Décision recommandée : garder Java/Spring Boot pour l’unique service Payment**, car cela préserve le service demandé et réduit coût/risque immédiat. Contrats JSON explicites, outbox/inbox et interop MassTransit validée sont conditions préalables. Si l’interop ou l’exploitation échoue, migration future du même service vers .NET possible, avec writer unique; ne pas maintenir deux Payments actifs.

## 4. Profil RabbitMQ Java ↔ MassTransit à tester

Le protocole AMQP commun ne garantit pas mêmes conventions, topology, headers ou retries. Avant intégration métier, un spike en vrai RabbitMQ doit tester les deux sens, ack, publisher confirm, redelivery, crash, DLQ et sérialisation. MassTransit doit être configuré en topology explicite correspondant au profil Java, sans dépendre de noms de classes CLR/Java.

- Vhost/ACL dédiée, users et permissions minimales par service, TLS en environnement partagé/production; secrets hors repo.
- Exchanges durables `topic`: `banksystem.commands.v1`, `banksystem.events.v1`; routing keys exactes documentées : `fraud.evaluation.requested.v1`, `fraud.evaluation.completed.v1`, `fraud.review.case.requested.v1`, `fraud.review.case.opened.v1`, `fraud.review.resolved.v1`, `bank.transaction.requested.v1`, `bank.transaction.accepted.v1`, `bank.transaction.completed.v1`, `bank.transaction.failed.v1`, `account.funds.applied.v1`, `account.funds.application-rejected.v1`.
- Files unicast commands : `fraud-detection.evaluate.v1`, `transaction.create-from-payment.v1`; files événement distinctes par consumer/groupe : `payment.fraud-decisions.v1`, `payment.fraud-review.v1`, `payment.transaction-results.v1`, `fraud-case.open-requests.v1`, projections séparées. Un competing consumer group ne broadcast pas.
- MassTransit `SetEntityName`/bindings/endpoints doit imposer cette topology (API exacte selon version). Spring déclare bindings identiques. Events fan-out via queues liées; commandes à propriétaire unique.
- `content-type: application/json; charset=utf-8`, UTF-8, camelCase, UUID strings, UTC RFC3339, montant décimal string, enum uppercase. `contractVersion` dans body, `commandType` ou `eventType`; aucun type header CLR ou `__TypeId__` Spring requis.
- Headers : messageId, correlationId, causationId, contractVersion, traceparent; mapping vers MassTransit MessageId/CorrelationId/ConversationId/InitiatorId sans valeurs divergentes. Contract tests valident headers/body.
- Messages persistants, queues durables/quorum selon disponibilité; publisher confirmation avant succès d’envoi; ACK consumer après commit DB + outbox. NACK/retry transitoire; aucun ACK après simple parse.
- DLX `banksystem.commands.dlx.v1`/`banksystem.events.dlx.v1`; DLQ par consumer (ex. `payment.fraud-decisions.dlq.v1`), `x-death`/headers d’origine conservés, données sensibles masquées. Redrive manuel contrôlé et audité.
- Retry borné exponentiel+jitter, différé selon mécanisme MassTransit configuré; aligner comportement Spring explicitement. Erreurs permanentes → DLQ; pas de TTL silencieux de commande financière.

Critère stop : tant que publication MassTransit→Spring et Spring→MassTransit, routage, confirmations, ACK, redelivery, retry/DLX, JSON et headers ne passent pas, ne pas connecter le workflow financier.

## 5. DB Payment

PostgreSQL dédié Payment, credentials/rôle dédié et migrations Flyway/Liquibase; aucune table partagée Fraud. Instance locale peut être commune, DB logique et permissions non partagées. Remplacer `ddl-auto:update` pour le schéma cible.

Tables à définir :

- `payments`: paymentId PK, client/tenant, source/destination/instrument selon produit, montant NUMERIC, devise, merchant/reference, status, version optimiste, fraudEvaluationId, bankTransactionId, correlationId, timestamps/reasonCode.
- `payment_state_history`: paymentId, from/to, acteur/service, message/cause, timestamp, reasonCode; append-only.
- `idempotency_records`: scope + clé unique, payload hash, paymentId/réponse, expiration.
- `outbox_messages`: messageId/type/version/body/headers, created/delivered/attempts.
- `inbox_messages`: consumer + messageId unique, handled/outcome, validé atomiquement avec état.
- `payment_legacy_mapping` facultative : ancienne référence→paymentId et checkpoint de rapprochement.

Agrégat Payment impose les transitions; données de demande ne changent pas silencieusement après fraude. PaymentStatus est défini section états ci-dessous. Fraud decision détaillée et Transaction status restent dans leurs propres DB. Paiements historiques : backup, migration idempotente avec mapping IDs et archive; ne pas reconstruire des scores/versions absents; comparer volumes/hashes avant cutover.

## 6. Payment → Fraud

`FraudEvaluationRequested` est une commande produite par Payment après persistance du Payment à `FRAUD_CHECK_PENDING` et de l’outbox dans la même transaction locale. Fraud Detection la consomme sur `fraud-detection.evaluate.v1`, stocke l’évaluation dans sa base propre, puis calcule avec les algorithmes existants. Payload v1 : `messageId`, `commandType`, `contractVersion`, `paymentId`, `evaluationId`, `customerId` fiable/opaque, montant, devise, merchant/category, features nécessaires et minimisées, `occurredAt`, `correlationId`, `causationId`, `idempotencyKey=paymentId:fraud:evaluationId`. Pas de secrets/tokens; device/location seulement selon nécessité et politique données. Fraud n’accède pas à la DB Payment.

`FraudEvaluationCompleted` est un événement publié par Fraud après sauvegarde de décision + audit + outbox; Payment le consomme via `payment.fraud-decisions.v1`. Body : message/event type, version, `paymentId`, `evaluationId`, décision `APPROVED|REVIEW|BLOCKED`, `riskScore` [0,1], `reasons[]` stables, `modelVersion`, `rulesVersion`, `evaluatedAt`, occurred/correlation/causation/idempotency IDs. Toutes les décisions, APPROVED inclus, ont même contrat. Fraud DB est source de vérité de décision; Payment n’en garde qu’une projection minimale et référence.

Payment valide payment/evaluation actifs, IDs, montant/devise et décision compatible à son statut. Décision inconnue/obsolète/contradictoire → inbox/quarantaine/alerte, aucun ordre Transaction. Fraud déduplique `(paymentId,evaluationId)`; redelivery ne recalcule pas les effets de features, ou les met à jour atomiquement et idempotemment. Payment déduplique `messageId` puis transition atomique. Retry transitoire borné; validation permanente/DLQ; si Fraud est indisponible, Payment reste FRAUD_CHECK_PENDING, fail-closed. Aucune décision par défaut.

| Sens | Exchange/routing | Queue | Sémantique |
|---|---|---|---|
| Payment→Fraud | commands.v1 / fraud.evaluation.requested.v1 | fraud-detection.evaluate.v1 | commande unicast |
| Fraud→Payment | events.v1 / fraud.evaluation.completed.v1 | payment.fraud-decisions.v1 | événement |

## 7. Payment → Transaction → Account

Payment envoie uniquement après APPROVED final (automatique ou humain), après commit de son état `TRANSACTION_PENDING` et outbox.

| Contrat | Nature; producteur → consumer | Pourquoi |
|---|---|---|
| `BankTransactionRequested` | commande; Payment→Transaction, queue `transaction.create-from-payment.v1` | Crée une opération bancaire liée au `paymentId`, avec type opération, comptes, montant/devise, référence et clé stable `paymentId:bank-transaction:v1`. N’est pas une mutation Account. |
| `TransactionAccepted` | événement; Transaction→Payment, queue `payment.transaction-results.v1` | Confirme création durable et `transactionId`; alternativement réponse HTTP 202 idempotente, mais choisir un seul canal d’acceptation. |
| `TransactionCompleted` | événement; Transaction→Payment/projections | Fait final après `FundsApplied`; Payment peut alors COMPLETED. |
| `TransactionFailed` | événement; Transaction→Payment | Rejet métier définitif/code stable après Account; Payment FAILED. Erreur technique reste pending/retry. |
| `FundsApplicationRequested` | commande; Transaction→Account | Contrat existant dans `fraud-integration-contracts.md`; Transaction seul producteur. |
| `FundsApplied` / `FundsApplicationRejected` | événements; Account→Transaction | Account est source autoritative. Écriture solde + marqueur idempotence atomiques. |

`paymentId` est unique côté Transaction afin qu’un retry de BankTransactionRequested retourne la même opération. Account déduplique transactionId/opération dans la même transaction DB que le ou les changements de balance. Un transfert n’annonce pas succès partiel. Payment ne consomme pas directement FundsApplied et ne demande jamais FundsApplicationRequested. Le `TransactionCompletedIntegrationEvent` actuel est utilisé comme commande implicite chez Account; il faudra remplacer cette sémantique par commande explicite et fait final, dans une migration coordonnée des projections.

## 8. Fraud Case Service : futur, pas existant

Le dépôt fraude présente des alertes (`FraudAlert`) mais aucun service complet de case, affectation, résolution ou audit de revue. Il ne faut pas le présenter comme déjà disponible. Ce bounded context futur doit : ouvrir/assigner dossier REVIEW, gérer priorité/SLA, fournir API de file et résolution protégée par rôle, stocker acteur/raison/date, empêcher décisions non autorisées, publier la résolution auditée. Il ne calcule pas de score, ne change pas le solde et ne crée pas de Transaction.

Base distincte `FraudCaseDb` (ou DB dédiée à ce contexte), sans accès à Fraud DB/Payment DB. Fraud Detection reste autorité pour décision automatique et audit de décision; Case Service est autorité du dossier et acte de revue humaine. Contrats futurs : `FraudReviewCaseRequested` commande Fraud→Case; `FraudReviewCaseOpened` événement après stockage; `FraudReviewResolved` événement Case→Fraud et Payment, portant caseId/paymentId/evaluationId, APPROVED/BLOCKED, reasons, reviewer reference, timestamp, correlation/causation. Fraud consigne l’issue humaine à son audit; Payment vérifie IDs/état et avance. Résolution APPROVED seulement déclenche BankTransactionRequested; BLOCKED termine Payment sans fonds.

À créer ultérieurement : service/API, auth/roles, UI ou intégration analyste, DB/migrations `fraud_cases`, `case_history`, assignments, idempotency/outbox/inbox, consumers/producers, observabilité et tests. Définir avant implémentation qui résout et SLA; aucune classe existante du repo ne suffit à cette responsabilité.

## 9. Machines à états distinctes

```text
Payment: CREATED → FRAUD_CHECK_PENDING
  ├─ Fraud APPROVED → APPROVED → TRANSACTION_PENDING
  │    ├─ TransactionCompleted → COMPLETED
  │    └─ TransactionFailed/Rejected → FAILED
  ├─ Fraud REVIEW → MANUAL_REVIEW
  │    ├─ FraudReviewResolved APPROVED → APPROVED → TRANSACTION_PENDING → COMPLETED/FAILED
  │    └─ FraudReviewResolved BLOCKED → BLOCKED
  └─ Fraud BLOCKED → BLOCKED
pending → EXPIRED/CANCELLED seulement selon politique explicite
```

| Objet | Owner | Interprétation |
|---|---|---|
| Payment status : CREATED, FRAUD_CHECK_PENDING, APPROVED, MANUAL_REVIEW, TRANSACTION_PENDING, COMPLETED, FAILED, BLOCKED, EXPIRED, CANCELLED | Payment Service | Cycle de l’intention Payment. COMPLETED uniquement après TransactionCompleted. |
| Fraud decision : APPROVED, REVIEW, BLOCKED; score, reasons, versions | Fraud Detection; issue humaine via Fraud Case | Verdict de risque, non résultat financier. |
| Transaction status (pending, completed, failed, reversed selon modèle cible) | Transaction Service | Cycle d’opération bancaire distincte référencée par bankTransactionId. |
| Account result (applied ou rejection métier) | Account Service | Seul fait autoritatif de mutation des fonds. |

Aucune commande bancaire depuis REVIEW/BLOCKED. APPROVED seul ne vaut ni TransactionCompleted ni solde modifié. FAILED requiert rejet métier définitif; panne temporaire garde pending. EXPIRED/CANCELLED après lancement bancaire nécessitent annulation/compensation orchestrée, pas simple changement de statut.

## 10. Retry, idempotence, indisponibilités, DLQ

Payment API impose clé idempotence scope tenant/client+opération et payload hash : même clé/payload retourne le même Payment, différente payload = conflit. Outbox associe messageId stable au commit; Inbox `(consumer,messageId)` unique et effet DB atomique. Fraud déduplique `(paymentId,evaluationId)`; nouvel essai logique = nouvel evaluationId auditée. BankTransactionRequested porte clé stable dérivée paymentId; Transaction impose unicité paymentId; Account impose unicité transactionId/opération dans la transaction solde. Double message ne peut pas créer deux mouvements.

Retry exponentiel borné avec jitter pour panne transitoire, timeout/circuit breaker; erreurs métier/validation non retryées. Timeout à résultat inconnu : reprendre avec mêmes IDs/clés et réconcilier. Limite atteinte : DLQ par queue, payload sensible protégé, metadata/IDs conservés, alerte propriétaire, correction puis replay audité. Ne jamais modifier clé métier au replay. Account unavailable laisse Transaction/Payment en attente; rejet de fonds/devise/compte est `TransactionFailed`. Fraud unavailable garde Payment FRAUD_CHECK_PENDING, pas de Transaction; fail-closed. Rabbit unavailable : Payment+outbox doivent commit ensemble, HTTP 202/pending, relay au retour; jamais statut succès. Une DLQ ne doit pas être un état terminal métier automatique.

## 11. Corrélation, versioning, sécurité, audit

Gateway crée/valide UUID correlationId et le renvoie en HTTP. Payment le conserve; chaque message propage même correlationId, causationId référence le message immédiatement consommé. Propager `messageId`, `paymentId`, `evaluationId`, `caseId`, `transactionId`, W3C traceparent et mapper explicitement vers headers MassTransit (`MessageId`, `CorrelationId`, `ConversationId`, `InitiatorId`). IDs séparés des idempotency keys et sans PII.

Contrats `contractVersion: 1`; ajout optionnel toléré, cassant = v2 avec double-read planifiée. JSON explicite, champs inconnus ignorés, décision enum inconnue quarantinée et jamais interprétée comme APPROVED. Contract tests partagés Java/.NET.

JWT validé à Gateway et Payment; ownership basé sur claim authentifié. Auth service-to-service dédiée (API key/OAuth workload), permissions minimales. Users Rabbit par service, rotation/secret manager, TLS; mTLS si politique réseau/identité l’exige. Payment n’envoie à Fraud que montant/devise, merchant/category, identifiants opaques et features minimales (device/localisation seulement nécessaires); aucune credential/token. Logs sans payload brut, PII, localisation précise ou secret. Payment garde historique d’états/acteur/message et refs; Fraud garde décision complète, score/raisons/versions/fallback/revue; Case garde audit humain; Transaction garde décision opération bancaire; Account garde effet/idempotence. Chaque service reste propriétaire de son audit, liés par IDs/correlation.

## 12. Tests à écrire avant activation

Aucun test n’est lancé dans cette phase de conception. Pour l’implémentation, prévoir :

- **Unitaires Payment** : transitions permises/interdites, APPROVED non terminal, BLOCKED/COMPLETED terminal, expiration, idempotency key/payload hash, répétition/concurrence et validations.
- **Contrats Java/.NET** : fixtures JSON v1, enum/fields inconnus, headers, correlation/causation, content type, routing keys, MassTransit publish ↔ Spring consume dans les deux sens.
- **RabbitMQ** : publisher confirm, ACK après commit, crash entre commit et ack, redelivery, outbox/inbox, retry, DLX/DLQ, redrive contrôlé et indisponibilité broker.
- **Payment→Fraud / Fraud→Payment** : toutes décisions, duplicate/stale/unknown evaluationId, fraude indisponible, audit/outbox cohérents.
- **Fraud Case** : ouverture/assignation/résolution, rôles, historique immuable, duplicate review, APPROVED/BLOCKED, timeout.
- **Payment→Transaction** : uniquement après APPROVED final; BankTransactionRequested répété produit le même transactionId; TransactionAccepted/Completed/Failed; panne Transaction.
- **Transaction→Account** : FundsApplicationRequested idempotent, fonds insuffisants, compte/devise invalides, compte inactive, atomicité transfert, FundsApplied/Rejected; même transactionId répété ne débite qu’une fois.
- **Parcours E2E** : APPROVED→COMPLETED après Account; REVIEW sans mutation avant résolution et BLOCKED sans mutation; fraude BLOCKED sans Transaction; REVIEW résolu BLOCKED; broker/Fraud/Account indisponibles; trace/audit bout-en-bout.
- **Migration** : snapshot/restore, count/hash, map ancien ID→paymentId, statuts historiques/non terminés, collision idempotence, offsets Kafka, rapprochement/rollback.
- **Sécurité** : JWT/ownership, S2S, permissions Rabbit, absence de credential Account DB, PII dans logs/DLQ.

## 13. Kafka → RabbitMQ : six phases sans double writer

Aucune phase ci-dessous n’est exécutée maintenant. L’unicité d’écriture doit être prouvée par cohortes/configuration et checkpoints, pas par convention.

1. **Kafka actuel fonctionnel** : inventorier topics, offsets, consumers, lignes et statuts; sauvegarder; définir une autorité par paiement. Ne pas altérer le flux présent.
2. **Rabbit introduit pour nouveaux contrats** : exchanges/queues/contrats v1 en environnement de test; réussir tests bidirectionnels MassTransit/Spring. Pas de production double publication actionnable.
3. **Fraud adapté aux contrats Rabbit** : adapter uniquement transport/DTO/outbox/inbox/audit de boundary; conserver les algorithmes. Pendant transition, Kafka reste chemin legacy autoritaire pour les paiements legacy; Rabbit test/shadow n’émet pas de décision actionnable pour les mêmes IDs.
4. **Payment Rabbit devient autoritaire** : migrer Payment DB/données avec mapping et cutover explicite; une seule instance/route accepte les écritures d’une cohorte. Sa seule publication actionnable passe outbox Rabbit. Les retries gardent paymentId/evaluationId/idempotencyKey.
5. **Kafka legacy/read-only** : ne plus accepter de nouvelles commandes dans le chemin legacy; garder lecture/historique et finir les évaluations/paiements en vol; réconcilier offsets et statuts.
6. **Retrait seulement après réconciliation** : vérifier aucune opération active, égalité DB/événements et fenêtre rollback close; archiver conformément à rétention, approbation opérationnelle requise. Retrait effectif est une étape future distincte.

Pas de dual-write Payment, Fraud décisionnel, ou double consommation ayant effets pour une même evaluationId. Si un bridge est absolument requis, il est temporaire, unidirectionnel, idempotent et non autoritaire d’un côté; un propriétaire unique détermine le message actionnable. Aucun message ne doit être perdu : offsets/confirmations, outbox et rapprochement des messages en vol sont critères de cutover. Les décisions historisées restent réconciliables.

## 14. Ordre exact d’implémentation ultérieure

1. Confirmer frontière Payment/Transaction, type de Payment, mapping IDs, source d’identité, politique REVIEW/expiration et owner Fraud Case.
2. Geler les contrats JSON v1, exchanges/routing/queues/headers et fixture schema.
3. Faire le spike Rabbit MassTransit↔Spring AMQP en deux sens (confirm/ack/retry/DLQ/restart); ne pas poursuivre si non fiable.
4. Définir DB Payment, schéma/history/idempotency/outbox/inbox, politique migration/backup et single-writer.
5. Implémenter Payment agrégat/API/auth/state machine/tests unitaires dans le service Java existant, sans appel Account.
6. Implémenter outbox/inbox et adaptateurs Rabbit Payment; contract tests.
7. Adapter boundary Fraud pour `FraudEvaluationRequested/Completed` et audit de toutes décisions; aucune modification RuleEngine/ML.
8. Concevoir puis créer Fraud Case service/base/API/roles/events; tests audit/revue. Il n’existe pas aujourd’hui.
9. Adapter Transaction pour recevoir `BankTransactionRequested` et appliquer idempotence/paymentId; produire Accepted/Completed/Failed.
10. Adapter Account à `FundsApplicationRequested`, Inbox + solde atomique, résultat Applied/Rejected et transfert tout-ou-rien.
11. Remapper Movement/Notification/Reporting aux seuls faits confirmés; tests projection/idempotence.
12. Ajouter déploiement Docker/secrets/health, observabilité, contract/integration/E2E/resilience/security tests.
13. Migrer données Payment avec backup, IDs mappés, simulation et rapprochement; unique writer gardé.
14. Exécuter six phases Kafka→Rabbit avec cohortes, suivi outbox/DLQ/écarts et réconciliation non terminés.
15. Cutover global uniquement après critères de sortie; arrêter Kafka pour ce parcours après clôture de rollback.

## 15. Fichiers et classes concernés par repository

Cette carte n’autorise aucune modification. Tests sont à étendre/ajouter au même jalon que le fichier associé.

### `fraud-detection-project`

| Fichier/classe | Changement prévu / raison | Dépendances et tests |
|---|---|---|
| `payment-service/src/main/java/com/paymentservice/controller/PaymentController.java` | API `/payments`, auth, idempotence/status | DTO/domain; MVC, validation, ownership |
| `.../model/Transaction.java` | Agrégat conceptuel Payment, état/history | migration DB; transition unit tests |
| `.../model/TransactionRequest.java` | DTO Payment/contrat | identité; validation/privacy tests |
| `.../repository/TransactionRepository.java` | clé unique, recherches/transition | DB Payment; concurrence/repository tests |
| `.../service/TransactionProducer.java` | Outbox + commandes Fraud et Transaction | contracts/AMQP; publisher tests |
| `.../service/VerdictListener.java` | Consumers Fraud/Review/Transaction et Inbox | states; duplicate/stale tests |
| `.../config/KafkaProducerConfig.java` | Kafka legacy puis désactivation; config AMQP ajoutée | phase cutover; broker test |
| `payment-service/src/main/resources/application.yml` | DB dédiée, AMQP/TLS/health, secrets externes | migrations/config tests |
| `payment-service/pom.xml` | AMQP, Flyway/Liquibase, metrics/tracing; Kafka retiré après réconciliation | build/smoke/contract tests |
| nouveaux packages Payment | contrats JSON, state/audit, idempotence, outbox/inbox, adapters | unit + integration suites |
| `payment-service/Dockerfile` | à ajouter plus tard (absent) | image/health tests |
| Fraud `consumer/FraudConsumer.java`, `model/TransactionEvent.java`, `model/FraudAlert.java`, `config/KafkaConfig.java`, `application.yml`, `pom.xml` | adapter boundary/transport/outbox; émettre décision complète incl. APPROVED | contract, duplicate, retry tests; pas les algorithmes |
| Fraud `engine/RuleEngine.java`, `RestMlScorer.java`, `StubMlScorer.java`, `UserProfileService.java` | restent Fraud-owned, algorithmes inchangés | non-régression si étape autorisée |
| Fraud Case service (absent) | nouveau bounded context/API/DB/auth/outbox | case lifecycle/security/audit tests |
| `docker-compose.yml`, `README.md` fraude | infra transition/docs après autorisation | compose health smoke |

### `BankSystemMicroservices-local`

| Fichier/classe | Changement prévu / raison | Tests liés |
|---|---|---|
| `Gateway/ApiGateway/appsettings*.json` | route Payment déployé | route/auth integration |
| `Gateway/ApiGateway/Middlewares/CorrelationIdMiddleware.cs` | propagation correlation/trace | gateway-payment headers |
| `Transaction.Api/Controllers/TransactionsController.cs` | accepter demande bancaire liée Payment | API tests |
| `Transaction.Application/Commands/CreateTransactionCommand.cs`, `Handlers/TransactionHandlers.cs` | idempotence/paymentId et workflow confirm Account | unit/saga tests |
| `Transaction.Domain/Entities/Transaction.cs`, `Enums/TransactionStatus.cs` | vrais états pending/completed/failed | transition tests |
| `Transaction.Infrastructure/Data/TransactionDbContext.cs`, `DependencyInjection.cs`, migrations | unique payment ref, Inbox/outbox | EF/Postgres tests |
| `Transaction.Infrastructure/Messaging/TransactionEventPublisher.cs` | Accepted/Completed/Failed faits versionnés | contract/broker tests |
| `Common/.../Events/Transaction/TransactionCompletedIntegrationEvent.cs` | ne plus agir comme commande; événement final après FundsApplied | compat consumer tests |
| `Common/.../Events/Account/AccountTransactionAppliedIntegrationEvent.cs` | remappage/harmonisation avec FundsApplied | consumer contracts |
| `Account.Infrastructure/Consumers/TransactionCompletedConsumer.cs` | remplacer par consumer FundsApplicationRequested | idempotence/concurrency |
| `Account.Domain/Entities/Account.cs`, `AccountDbContext.cs` | solde uniquement ici, marqueur unique atomique, transfert total | balance/atomic transfer |
| Movement/Reporting/Notification `*Consumer.cs` existants | projections depuis faits confirmés | duplicate projection tests |
| `Common/.../OutboxServiceCollectionExtensions.cs`, `CrossServiceConsumerExtensions.cs`, `MassTransitOptions.cs` | topology/retry/DLQ explicite interop | Rabbit integration |
| `docker-compose/docker-compose.yml`, `postgres/init/01-create-databases.sql`, `AppHost/AppHost.cs` | infrastructure/route DB Payment, readiness/secrets sans fusion repos | compose/deploy tests |
| tests `Services/Transaction/tests/**`, `Services/Account/tests/**`, `Common/tests/**` | workflow, contracts, resilience | suites ciblées ci-dessus |

Payment reste dans son repository; YARP consomme son endpoint. Déploiement commun éventuel ne signifie pas fusion de repositories.

## 16. Critères d’acceptation avant activation

- Un seul writer Payment, une seule DB Payment propriétaire; un seul writer de chaque transaction; Account seul peut muter balance.
- Payment n’a ni credentials DB Account ni contrat `FundsApplicationRequested`; Fraud n’a aucun accès Account.
- APPROVED fraude mène seulement à la demande Transaction; REVIEW/BLOCKED n’entraînent aucun message financier.
- COMPLETED Payment seulement après TransactionCompleted après FundsApplied; duplicates/retry ne changent pas deux fois balance.
- Interop Rabbit réelle testée pour contrats, headers, confirms/ACK, retries/DLQ; audit et corrélation bout-en-bout vérifiables.
- Outbox lag, DLQ, redrive, backup, migration mapping, reconciliation et rollback sont opérables.
- Tous les paiements Kafka non terminés et offsets sont réconciliés avant arrêt du chemin legacy.

Plan uniquement : aucun code, service ou algorithme modifié, aucune donnée migrée.
