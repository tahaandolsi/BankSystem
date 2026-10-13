# Contrats d’intégration fraude — opérations bancaires internes

## Portée et décisions

Ce document définit une cible de contrats ; il ne change ni le workflow en production ni le code. La base est `docs/fraud-integration-analysis.md` et les types actuellement observés dans les deux dépôts.

Le scénario concerne dépôts, retraits et transferts bancaires internes du Transaction Service. Il n’établit pas de domaine Payment distinct : le `payment-service` Java du prototype reste hors du chemin bancaire. Fraud Detection évalue le risque mais n’applique jamais de fonds. Account reste seul propriétaire du solde.

RabbitMQ/MassTransit est le transport recommandé dans un premier temps. Les contrats logiques ci-dessous sont indépendants du transport. Les commandes demandent une action ; les événements annoncent un fait déjà validé.

### Point d’intégration des données client

Le Transaction Service actuel connaît `accountId`, montant et type, mais pas `customerId`. Il ne doit pas lire la base Account. Avant d’émettre l’évaluation, le flux devra obtenir le `customerId` via une API Account authentifiée ou une identité fiable déjà disponible dans le contexte d’authentification. Account devra toujours valider que l’opération est autorisée et que les comptes correspondent ; le `customerId` transporté est un contexte de risque, jamais une preuve d’autorisation. Si le produit ne peut pas résoudre cette donnée, le champ peut être `null` et Fraud doit appliquer la politique explicite « identité indisponible » ; ne pas inventer une valeur.

## 1. Architecture des échanges

```text
Client → Transaction Service
           │ transaction persistée FRAUD_CHECK_PENDING + outbox
           └─ FraudEvaluationRequested (commande)
                    ↓
             Fraud Detection Service
             évalue, écrit décision/audit + outbox
                    └─ FraudEvaluationCompleted (événement)
                            ↓
             Transaction Service
               ├─ APPROVED → persiste FUNDS_APPLICATION_PENDING + outbox
               │                 └─ FundsApplicationRequested (commande)
               │                         ↓
               │                    Account Service (valide et applique atomiquement)
               │                       ├─ FundsApplied (événement)
               │                       └─ FundsApplicationRejected (événement)
               │                               ↓
               └──────────────────── Transaction Service → COMPLETED / FAILED

               ├─ REVIEW → MANUAL_REVIEW (aucune commande Account)
               └─ BLOCKED → REJECTED (aucune commande Account)
```

Chaque service possède sa base, son outbox et sa Inbox/déduplication locale. Aucun service ne lit/écrit la base d’un autre. Les états locaux et l’émission de l’outbox sont validés dans la même transaction DB. Une même décision fraude n’est consommée qu’une fois logiquement, même si RabbitMQ redélivre.

## 2. Conventions de contrat et enveloppe

JSON UTF-8, noms de propriétés en camelCase, identifiants UUID en chaînes, instants RFC 3339 UTC (`Z`), montants décimaux en chaînes afin d’éviter les conversions flottantes. `currency` est un code ISO 4217 majuscule. Les exemples ci-dessous sont des exemples JSON valides. Les noms sont des types de message MassTransit ; chaque contrat a `messageId` stable et `contractVersion` explicite.

- `messageId` identifie l’occurrence du message et sert à Inbox/déduplication.
- `eventType` est le nom du fait pour un événement. Pour une commande, utiliser `commandType`, pas `eventType` : une commande n’est pas un événement.
- `transactionId` est l’identifiant de l’agrégat Transaction et la clé métier de toute l’opération.
- `evaluationId` identifie une évaluation précise. Réévaluation après nouvelle donnée ou décision humaine = nouvel ID, avec `supersedesEvaluationId` si applicable.
- `correlationId` reste constant de la requête initiale à l’issue finale. `causationId` est le `messageId` immédiat ayant déclenché le message courant.
- `occurredAt` signifie création/émission du message (pas l’heure d’évaluation, distincte `evaluatedAt`).
- `idempotencyKey` exprime l’intention métier stable ; elle ne remplace pas `messageId`.
- `sourceAccountId` est le compte débité ou le compte concerné pour retrait/dépôt selon conventions ; `destinationAccountId` est renseigné pour transfert seulement. `accountId` est utilisé sur les réponses Account pour désigner le compte dont le solde a été modifié (sur transfert, deux effets peuvent être inclus dans le même résultat atomique).
- `customerId` est le client associé à la transaction/compte source, nullable seulement si la politique l’autorise.

### Types métier partagés

`transactionType`: `DEPOSIT | WITHDRAWAL | TRANSFER`.

`decision`: `APPROVED | REVIEW | BLOCKED`.

`reasons`: tableau de codes stables, pas de texte localisé ni données personnelles. Exemple : `AMOUNT_SPIKE_VS_USER_AVERAGE`.

## 3. Commandes

### 3.1 `FraudEvaluationRequested` — Transaction → Fraud Detection

**Sémantique : commande.** Demande une nouvelle évaluation pour une transaction déjà persistée. Elle ne signifie pas que l’évaluation a eu lieu. Le nom proposé dans l’analyse est approprié si traité comme commande, et non comme événement métier.

**DB source :** Transaction DB détient la transaction et sa demande/outbox. Fraud DB détient l’évaluation reçue, features admissibles et décision. **Producteur :** Transaction Service. **Consommateur :** Fraud Detection Service.

```json
{
  "messageId": "7c8ebbc1-47d3-4cf0-a749-c52ad44ae1a0",
  "commandType": "FraudEvaluationRequested",
  "contractVersion": 1,
  "transactionId": "d89c06cc-c27e-4b0e-a33f-65161ab71470",
  "evaluationId": "b52a88d0-c8d8-4a91-937c-3960bd865a01",
  "sourceAccountId": "c07a9e8f-1fd0-4b3e-8aed-e7a2c8742ae5",
  "destinationAccountId": "9f3835eb-c263-4f35-9ef2-40ec643b8b92",
  "customerId": "f25c424c-891b-47b8-b8a9-50e220c8f17d",
  "transactionType": "TRANSFER",
  "amount": "125.50",
  "currency": "EUR",
  "occurredAt": "2026-10-08T09:30:00Z",
  "correlationId": "a6e2d3b3-19f5-42f7-912d-e1168a34ba77",
  "causationId": "095b1c4a-0f45-4987-b634-0a379c874002",
  "idempotencyKey": "bank-client-42:transfer:request-8031",
  "transactionReference": "optional-business-reference"
}
```

For deposit/withdrawal, `destinationAccountId` is `null`. `transactionReference` may be omitted or null. Do not include PAN, credentials, full location history, or unneeded profile attributes. Fraud obtains/maintains its own features under its own data policy.

### 3.2 `FundsApplicationRequested` — Transaction → Account

**Sémantique : commande.** Demande à Account de valider puis d’appliquer les fonds. Elle n’affirme ni l’application ni le succès. Publiée uniquement après une décision fraude APPROVED (ou une résolution manuelle explicite APPROVED). Le nom proposé est approprié comme commande.

**DB source :** Transaction DB contient état et outbox ; Account DB contient ses soldes et Inbox/applied-operation records. **Producteur :** Transaction Service. **Consommateur :** Account Service.

```json
{
  "messageId": "36d196e9-adf4-4b82-ad0f-c12a6ba2f2d9",
  "commandType": "FundsApplicationRequested",
  "contractVersion": 1,
  "transactionId": "d89c06cc-c27e-4b0e-a33f-65161ab71470",
  "evaluationId": "b52a88d0-c8d8-4a91-937c-3960bd865a01",
  "sourceAccountId": "c07a9e8f-1fd0-4b3e-8aed-e7a2c8742ae5",
  "destinationAccountId": "9f3835eb-c263-4f35-9ef2-40ec643b8b92",
  "customerId": "f25c424c-891b-47b8-b8a9-50e220c8f17d",
  "transactionType": "TRANSFER",
  "amount": "125.50",
  "currency": "EUR",
  "occurredAt": "2026-10-08T09:30:02Z",
  "correlationId": "a6e2d3b3-19f5-42f7-912d-e1168a34ba77",
  "causationId": "7a2b4806-7349-4429-94c7-cc81c4a403f0",
  "idempotencyKey": "d89c06cc-c27e-4b0e-a33f-65161ab71470:apply-funds:v1"
}
```

Account ne fait pas confiance au `customerId` fourni : elle relit son agrégat par son API interne de commande (dans son propre service) et valide compte actif, devise, fonds, autorisation/propriété. Pour un transfert, Account doit garantir l’application débit+crédit dans une transaction locale atomique, ou concevoir une saga/compensation Account-owned avant d’accepter le contrat ; aucun événement de succès ne doit annoncer un transfert partiel.

## 4. Événements

### 4.1 `FraudEvaluationCompleted` — Fraud Detection → Transaction

**Sémantique : événement.** Une décision calculée et enregistrée existe. `FraudEvaluationCompleted` est acceptable, à condition qu’il signifie explicitement « évaluation terminée » et que la décision ne soit pas confondue avec l’application financière.

**DB source :** Fraud DB est source de vérité de la décision/audit ; son outbox publie. Transaction DB garde une projection de l’état fraude. **Producteur :** Fraud Detection Service. **Consommateur :** Transaction Service.

```json
{
  "messageId": "7a2b4806-7349-4429-94c7-cc81c4a403f0",
  "eventType": "FraudEvaluationCompleted",
  "contractVersion": 1,
  "transactionId": "d89c06cc-c27e-4b0e-a33f-65161ab71470",
  "evaluationId": "b52a88d0-c8d8-4a91-937c-3960bd865a01",
  "sourceAccountId": "c07a9e8f-1fd0-4b3e-8aed-e7a2c8742ae5",
  "destinationAccountId": "9f3835eb-c263-4f35-9ef2-40ec643b8b92",
  "customerId": "f25c424c-891b-47b8-b8a9-50e220c8f17d",
  "amount": "125.50",
  "currency": "EUR",
  "decision": "APPROVED",
  "riskScore": 0.08,
  "reasons": [],
  "modelVersion": "rf-2026-10-01",
  "rulesVersion": "fraud-rules-1.4.0",
  "evaluatedAt": "2026-10-08T09:30:01Z",
  "occurredAt": "2026-10-08T09:30:01Z",
  "correlationId": "a6e2d3b3-19f5-42f7-912d-e1168a34ba77",
  "causationId": "7c8ebbc1-47d3-4cf0-a749-c52ad44ae1a0",
  "idempotencyKey": "d89c06cc-c27e-4b0e-a33f-65161ab71470:fraud:b52a88d0-c8d8-4a91-937c-3960bd865a01",
  "supersedesEvaluationId": null
}
```

`riskScore` est borné [0,1], nullable si la politique de décision n’utilise pas de score; pour v1, le contrat devrait le rendre obligatoire si le scorer produit toujours un score. `reasons` contient les raisons machine normalisées (peut être vide pour APPROVED). `modelVersion` et `rulesVersion` sont des chaînes identifiantes, pas libres/PII. `evaluationId` doit correspondre à la demande active de Transaction ; une réponse inconnue ou obsolète est mise en quarantaine/auditée, jamais utilisée pour émettre une commande Account.

Une décision humaine après REVIEW ne réécrit pas cette décision. Elle est un fait distinct `FraudReviewResolved` contenant `reviewCaseId`, `transactionId`, `evaluationId` initial, décision finale APPROVED/BLOCKED, `reviewerId` pseudonymisé, `reasons`, `occurredAt`, correlation/causation et un nouvel `messageId`. Transaction ne demande alors les fonds que pour l’APPROVED final. La décision et l’audit restent Fraud-owned. `reviewerId` n’est pas diffusé au-delà des consommateurs autorisés.

### 4.2 `FundsApplied` — Account → Transaction

**Sémantique : événement.** Account a validé et appliqué tous les effets de fonds de cette opération. C’est le seul signal de succès qui permet à Transaction de passer à COMPLETED. Le nom est approprié.

**DB source :** Account DB, mise à jour du/des solde(s), enregistrement d’opération appliquée et outbox atomiques ; Transaction DB met à jour son état en réaction. **Producteur :** Account Service. **Consommateur :** Transaction Service. Les projections autorisées (Movement, Reporting) peuvent aussi consommer cet événement/fait selon le mapping.

```json
{
  "messageId": "6fe9c928-c790-4e25-8e13-04db857733f5",
  "eventType": "FundsApplied",
  "contractVersion": 1,
  "transactionId": "d89c06cc-c27e-4b0e-a33f-65161ab71470",
  "evaluationId": "b52a88d0-c8d8-4a91-937c-3960bd865a01",
  "sourceAccountId": "c07a9e8f-1fd0-4b3e-8aed-e7a2c8742ae5",
  "destinationAccountId": "9f3835eb-c263-4f35-9ef2-40ec643b8b92",
  "customerId": "f25c424c-891b-47b8-b8a9-50e220c8f17d",
  "transactionType": "TRANSFER",
  "amount": "125.50",
  "currency": "EUR",
  "appliedAccounts": [
    {"accountId": "c07a9e8f-1fd0-4b3e-8aed-e7a2c8742ae5", "direction": "DEBIT"},
    {"accountId": "9f3835eb-c263-4f35-9ef2-40ec643b8b92", "direction": "CREDIT"}
  ],
  "occurredAt": "2026-10-08T09:30:03Z",
  "correlationId": "a6e2d3b3-19f5-42f7-912d-e1168a34ba77",
  "causationId": "36d196e9-adf4-4b82-ad0f-c12a6ba2f2d9",
  "idempotencyKey": "d89c06cc-c27e-4b0e-a33f-65161ab71470:apply-funds:v1"
}
```

Ne pas publier `balanceAfter` dans un contrat générique si les soldes ne sont pas nécessaires aux consommateurs ; cela divulgue une donnée sensible et est ambigu pour un transfert à deux comptes. Si audit/ledger impose des montants par compte, contrat sécurisé avec deux effets et devise explicites, accès restreint.

### 4.3 `FundsApplicationRejected` — Account → Transaction

**Sémantique : événement.** Account a traité la commande et l’a refusée pour une raison métier définitive. Les défaillances techniques temporaires ne sont pas des rejets métier : elles doivent déclencher retry et n’émettent pas cet événement tant que le résultat n’est pas connu. Le nom proposé est approprié.

**DB source :** Account DB conserve le résultat idempotent/rejet lié à l’opération et l’outbox ; Transaction DB projette l’échec. **Producteur :** Account Service. **Consommateur :** Transaction Service.

```json
{
  "messageId": "82e35616-4148-46a6-9d39-3f20a70693cd",
  "eventType": "FundsApplicationRejected",
  "contractVersion": 1,
  "transactionId": "d89c06cc-c27e-4b0e-a33f-65161ab71470",
  "evaluationId": "b52a88d0-c8d8-4a91-937c-3960bd865a01",
  "sourceAccountId": "c07a9e8f-1fd0-4b3e-8aed-e7a2c8742ae5",
  "destinationAccountId": "9f3835eb-c263-4f35-9ef2-40ec643b8b92",
  "customerId": "f25c424c-891b-47b8-b8a9-50e220c8f17d",
  "transactionType": "TRANSFER",
  "amount": "125.50",
  "currency": "EUR",
  "reasonCode": "INSUFFICIENT_FUNDS",
  "occurredAt": "2026-10-08T09:30:03Z",
  "correlationId": "a6e2d3b3-19f5-42f7-912d-e1168a34ba77",
  "causationId": "36d196e9-adf4-4b82-ad0f-c12a6ba2f2d9",
  "idempotencyKey": "d89c06cc-c27e-4b0e-a33f-65161ab71470:apply-funds:v1"
}
```

Codes stables possibles : `ACCOUNT_NOT_FOUND`, `ACCOUNT_NOT_ACTIVE`, `CURRENCY_MISMATCH`, `INSUFFICIENT_FUNDS`, `TRANSFER_NOT_ATOMICALLY_APPLIED`, `ACCOUNT_POLICY_REJECTED`. Ne pas renvoyer de détails internes exploitables. Si un transfert a échoué, il doit être garanti qu’aucun effet partiel ne reste, ou qu’Account a lancé et suit sa propre compensation avant de déclarer le rejet.

### Événement final Transaction

Après réception idempotente de `FundsApplied`, Transaction met à jour sa transaction et publie via son outbox **`TransactionCompleted`** (`eventType`, v1) comme fait métier final. Après `FundsApplicationRejected`, il publie `TransactionFailed` (ou `TransactionRejected` selon convention produit) avec `reasonCode`. Ces événements destinent les projections Movement/Reporting/Notification. `TransactionCompletedIntegrationEvent` actuel ne doit pas être réutilisé comme commande Account ; il pourra être remplacé par un contrat final à nom explicite après migration coordonnée. Cet événement final est ici décrit pour fermer le workflow, mais n’est pas un sixième message de commande/décision demandé.

## 5. Producteurs, consommateurs et ownership — résumé

| Message | Nature | Producteur → consommateurs | Source de vérité |
|---|---|---|---|
| `FraudEvaluationRequested` | Commande | Transaction → Fraud Detection | Transaction DB pour demande, Fraud DB pour traitement |
| `FraudEvaluationCompleted` | Événement | Fraud Detection → Transaction | Fraud DB |
| `FundsApplicationRequested` | Commande | Transaction → Account | Transaction DB pour demande, Account DB pour application |
| `FundsApplied` | Événement | Account → Transaction (+ projections autorisées) | Account DB |
| `FundsApplicationRejected` | Événement | Account → Transaction | Account DB |
| `FraudReviewResolved` (complément nécessaire) | Événement | outil/processus de revue autorisé → Fraud Detection et Transaction | Fraud DB/case audit |
| `TransactionCompleted` / `TransactionFailed` (faits finaux) | Événements | Transaction → Movement/Reporting/Notification | Transaction DB, fondé sur résultat Account |

## 6. Machine à états exacte de Transaction

La proposition donnée doit être ajustée : `APPROVED`, `REVIEW` et `BLOCKED` sont des décisions fraude, pas tous des états de Transaction. `MANUAL_REVIEW` est un état, puis la décision humaine doit être un fait séparé. `COMPLETED` n’est accessible qu’après `FundsApplied`.

```text
PENDING
  └─ transaction acceptée et outbox prête → FRAUD_CHECK_PENDING
       ├─ FraudEvaluationCompleted(APPROVED) → FUNDS_APPLICATION_PENDING
       │    ├─ FundsApplied → COMPLETED [terminal réussi]
       │    └─ FundsApplicationRejected → FAILED [terminal métier]
       ├─ FraudEvaluationCompleted(REVIEW) → MANUAL_REVIEW
       │    ├─ FraudReviewResolved(APPROVED) → FUNDS_APPLICATION_PENDING
       │    │    ├─ FundsApplied → COMPLETED
       │    │    └─ FundsApplicationRejected → FAILED
       │    └─ FraudReviewResolved(BLOCKED) → REJECTED [terminal fraude]
       └─ FraudEvaluationCompleted(BLOCKED) → REJECTED [terminal fraude]
```

Ajouter les fins opérationnelles `CANCELLED` et `EXPIRED` uniquement avec règles métier explicites. Erreur technique après acceptation laisse le statut dans un état pending et la livraison se reprend ; ne pas la traduire en `FAILED` arbitraire. `FAILED` signifie Account a répondu rejet définitif. Aucun `APPROVED` de fraude ne vaut succès financier.

Machine de décision fraude distincte : `REQUESTED → EVALUATING → COMPLETED(APPROVED|REVIEW|BLOCKED)`, éventuellement `REVIEW → RESOLVED(APPROVED|BLOCKED)` ; une nouvelle analyse utilise un nouvel `evaluationId` et n’écrase pas l’audit précédent.

## 7. Idempotence et prévention du double mouvement de solde

1. L’API Transaction impose une `Idempotency-Key` métier (scope client/tenant + opération) et stocke empreinte de requête et résultat. Même clé + même payload renvoie le même `transactionId`/statut ; clé identique + payload différent = conflit. La contrainte unique DB est l’autorité, pas seulement une lecture préalable.
2. Chaque envoi outbox a `messageId` persistant. MassTransit inbox et consommateur dédupliquent par identifiant de message ; cela protège la redélivrance identique.
3. Fraud déduplique par `(transactionId, evaluationId)` ; une deuxième livraison ne modifie pas les features ni ne crée une décision concurrente. Un nouvel essai logique est un nouvel `evaluationId`, explicitement demandé et audité. Les mises à jour Redis doivent être atomiques et idempotentes au niveau de l’événement.
4. Account déduplique **par `transactionId` + opération** à l’aide d’une contrainte unique sur son journal interne (`FundsApplicationId`/`idempotencyKey`), et enregistre ce marqueur dans la même transaction DB que tous les changements de solde. Sur redelivery, il ne rejoue jamais le débit/crédit : il republie le résultat durable précédent avec un nouvel `messageId` si nécessaire et la même clé métier.
5. `FundsApplicationRequested.idempotencyKey` est déterministe pour une tentative d’application (`transactionId:apply-funds:v1`). Un nouvel essai n’autorise pas une seconde application ; correction/compensation est une opération distincte, auditée.
6. Account met à jour source, destination et marqueur d’idempotence sous une seule transaction locale avec verrouillage/concurrence approprié. Le transfert ne peut pas avoir un débit seul. Transaction passe à COMPLETED seulement sur `FundsApplied` correspondant à son `transactionId`, sa clé, son montant/type, et sa décision APPROVED active.
7. Réception d’un événement duplicatif `FundsApplied` après état COMPLETED est un no-op enregistré. Un message contradictoire (par ex. Applied puis Rejected) est quarantiné/alerte, jamais transitionné silencieusement.

## 8. Retry, erreurs et DLQ

- MassTransit configure retry borné exponentiel avec jitter (valeurs opératoires à choisir), timeout et circuit breaker pour pannes transitoires. Le retry ne contourne pas les clés d’idempotence.
- Erreurs transitoires DB/broker/ML : ne pas ACK le traitement métier avant la persistance/outbox ; laisser reprendre par redelivery. Un timeout de résultat inconnu ne doit pas provoquer une seconde application non dédupliquée.
- Erreurs métier Account : produire `FundsApplicationRejected` avec code stable, ACK après persistance atomique du rejet/outbox ; ne pas faire de retry pour fonds insuffisants, compte fermé ou devise incompatible.
- Erreurs de validation/contrat permanentes : quarantaine/DLQ avec payload protégé, exception, endpoint, version et IDs de corrélation; alerte opérateur. Les PII et secrets doivent être masqués/chiffrés dans la DLQ.
- Après épuisement des retries, la DLQ conserve le message original et les métadonnées de redelivery. Outils de métrique/alerte, procédure de correction et replay contrôlé sont nécessaires. Replay conserve `transactionId`, `evaluationId`, `idempotencyKey`; il ne crée pas une nouvelle opération financière.
- Une réponse fraude inconnue/obsolète, un `evaluationId` non attendu ou une somme/compte qui ne correspond pas à la transaction est rejeté/quarantiné et ne déclenche jamais Account.
- Si Fraud est indisponible, Transaction reste `FRAUD_CHECK_PENDING`; aucune application de fonds. Si RabbitMQ est indisponible après l’acceptation locale, l’outbox garde la commande et l’API expose un état non final. Pas d’approbation implicite au timeout.

## 9. Correlation ID / Causation ID

Le Gateway crée un UUID `correlationId` quand absent ou invalide, le renvoie au client et le propage dans le contexte/log HTTP. Transaction le fixe sur son outbox initial. MassTransit transporte `CorrelationId`, `ConversationId`, `InitiatorId` et `MessageId` dans les headers ; les champs applicatifs ci-dessus sont une représentation de contrat, mappée sans divergence. Chaque message copie le même `correlationId`; `causationId` référence le message consommé immédiatement (et non un événement arbitraire en amont). Les traces incluent `transactionId`, `evaluationId` et `messageId`. Ces IDs ne contiennent pas de PII et ne sont jamais des secrets ou clés d’idempotence.

## 10. Versioning des contrats

- `contractVersion: 1` est obligatoire dans l’enveloppe v1. Les types peuvent être des records C# partagés versionnés sous un namespace de contrats d’intégration ; MassTransit sérialise le type, mais le nom CLR ne doit pas être l’unique mécanisme de compatibilité inter-langage.
- Changements compatibles (champ optionnel ajouté, valeur d’extension tolérée) restent v1 ; les lecteurs ignorent les propriétés inconnues et traitent les champs optionnels avec valeurs par défaut explicites.
- Changement cassant (sens/type/obligation/enum supprimée) crée v2/type ou contrat clairement versionné, période de double lecture/publication convenue et migration des producteurs/consommateurs. Ne pas changer silencieusement le sens d’un champ.
- Les valeurs inconnues de décision/type ne sont jamais interprétées comme APPROVED ; consumer les met en quarantaine et garde l’opération sans mouvement de fonds.
- La compatibilité est testée par exemples JSON/contract tests dans les deux dépôts et publiée avec propriétaire, version, politique de dépréciation et durée de support.

## 11. Audit

Fraud Detection conserve chaque décision (APPROVED inclus), la demande/message source, transaction/evaluation/correlation, `evaluatedAt`, `decision`, score et explications normalisées, versions de modèle/règles, mode de scoring (modèle/fallback) et issue humaine. La donnée sensible d’entrée est minimisée ou référencée de façon sécurisée. Enregistrements append-only avec contrôle d’accès/rétention et journal d’accès/changements de politique ; une correction crée une nouvelle évaluation liée, ne réécrit pas la précédente.

Transaction conserve les transitions d’état avec `messageId`, source, timestamp et ancien/nouvel état. Account conserve l’application idempotente et son résultat, sans faire de Fraud la source de son solde. Outbox établit le lien entre mutation locale et fait publié. Les logs techniques seuls ne sont pas le journal d’audit.

## 12. Parcours d’exemple

Les exemples illustrent l’ordre et les liens causaux ; les UUID diffèrent de ceux des schémas complets.

### APPROVED

1. Transaction crée `T-100` et la commande `FraudEvaluationRequested` (`evaluationId=E-1`, `messageId=M1`), état `FRAUD_CHECK_PENDING`.
2. Fraud persiste évaluation E-1, produit `FraudEvaluationCompleted(APPROVED, riskScore=0.08, messageId=M2, causationId=M1)`.
3. Transaction valide T-100/E-1, passe à `FUNDS_APPLICATION_PENDING`, publie `FundsApplicationRequested` (M3, causationId M2).
4. Account valide les comptes et fonds, écrit une fois le débit/crédit et la clé idempotence puis publie `FundsApplied` (M4, causationId M3).
5. Transaction consomme M4, passe à `COMPLETED` et publie `TransactionCompleted` depuis son outbox. Une redelivery de M3/M4 ne change aucun solde/état additionnel.

### REVIEW

1. Transaction émet `FraudEvaluationRequested` E-2 puis attend `FRAUD_CHECK_PENDING`.
2. Fraud rend `FraudEvaluationCompleted(REVIEW, reasons=["NEW_DEVICE_HIGH_VALUE"])`; Transaction passe `MANUAL_REVIEW`. Aucune commande Account n’est publiée.
3. Le processus de revue authentifié enregistre son résultat dans le contexte Fraud et émet `FraudReviewResolved(APPROVED)` avec référence à E-2 et acteur audité.
4. Transaction valide la résolution, passe à `FUNDS_APPLICATION_PENDING`, publie `FundsApplicationRequested`; seul le chemin normal Account peut ensuite donner `COMPLETED` ou `FAILED`.
5. Si résultat BLOCKED, Transaction passe `REJECTED`, n’émet aucune commande de fonds. Expiration de revue est explicitement `EXPIRED`, jamais auto-APPROVED.

### BLOCKED

1. Transaction émet `FraudEvaluationRequested` E-3 et reste `FRAUD_CHECK_PENDING`.
2. Fraud émet `FraudEvaluationCompleted(BLOCKED, reasons=["IMPOSSIBLE_TRAVEL_SPEED_EXCEEDED"])` avec audit et versions.
3. Transaction valide la décision correspondant à E-3 et passe à `REJECTED`; elle publie l’événement final correspondant. Aucun message `FundsApplicationRequested` n’est produit et Account n’est pas appelé.

## 13. Contrats à créer ou modifier ultérieurement

### BankSystemMicroservices-local

**À créer (contrats v1 proposés) :**

- `FraudEvaluationRequested` (commande Transaction → Fraud Detection).
- `FraudEvaluationCompleted` (événement Fraud Detection → Transaction).
- `FundsApplicationRequested` (commande Transaction → Account).
- `FundsApplied` et `FundsApplicationRejected` (événements Account → Transaction).
- `FraudReviewResolved` (événement du workflow de revue autorisé; producteur de revue à préciser avant implémentation).
- Contrat final explicite `TransactionCompleted` et `TransactionFailed`/`TransactionRejected` pour projections, si nécessaire après remappage des consumers.

**À modifier/remplacer lors d’une migration coordonnée :**

- `Common/src/BankSystem.Shared.Domain/Events/Transaction/TransactionCompletedIntegrationEvent.cs` : ne plus l’utiliser pour demander l’application de fonds ; remplacer son rôle par `FundsApplicationRequested`, et réserver l’événement final à `TransactionCompleted` après confirmation Account.
- `Common/src/BankSystem.Shared.Domain/Events/Account/AccountTransactionAppliedIntegrationEvent.cs` : décider s’il devient `FundsApplied` ou reste un événement de projection dérivé ; harmoniser noms, enveloppe et idempotence sans exporter inutilement `balanceAfter`.
- Consumers actuels `Services/Account/src/Account.Infrastructure/Consumers/TransactionCompletedConsumer.cs`, `Services/Movement/.../TransactionCompletedConsumer.cs`, `Services/Reporting/.../TransactionCompletedConsumer.cs` devront être remappés selon le nouveau rôle : Account consomme commande; Movement/Reporting consomment faits confirmés. Cette étape est future et non réalisée ici.

### fraud-detection-project

**À créer/adopter comme contrats d’entrée/sortie :**

- Consommer `FraudEvaluationRequested` au lieu de son contrat prototype `TransactionEvent`/topic `transactions`.
- Émettre `FraudEvaluationCompleted` pour toutes les décisions, y compris APPROVED, avec `evaluationId`, score, raisons, versions, audit, causation/correlation.
- Émettre `FraudReviewResolved` uniquement si ce dépôt héberge réellement le workflow de revue ; sinon le contrat est produit par le service/outillage responsable et Fraud en reste l’autorité d’audit.

**À ne pas intégrer au workflow interne bancaire :**

- Topics/payloads `approved` et `fraud-alerts` actuels, qui sont incomplets et contradictoires au contrat unifié.
- Le `payment-service` existant n’est pas producteur de l’opération bancaire ni propriétaire du statut Transaction. Il demeure prototype isolé tant qu’une décision produit distincte sur paiements externes n’est pas prise.

Aucun code ni contrat existant n’a été modifié dans cette étape.
