# Analyse d’intégration de la détection de fraude

## Périmètre et méthode

Analyse statique des fichiers présents dans `BankSystemMicroservices-local` et `fraud-detection-project` (sources, configuration, Docker, tests et README). Aucun service n’a été exécuté et aucun test n’a été lancé. Les affirmations décrivent le code observé ; les propositions sont des recommandations de cible, pas une implémentation.

## Synthèse des implémentations actuelles

### BankSystemMicroservices-local

- **API Gateway** : YARP, routes/configuration dans `Gateway/ApiGateway/appsettings*.json`, authentification JWT, middleware de correlation ID et transformations de réponse. Il route notamment vers Transaction Service ; il ne porte pas le domaine paiement.
- **Security Service** : ASP.NET Core/Identity, SQL Server via EF Core, authentification/JWT, audit sécurité et API gRPC pour contacts utilisateur. Pas de responsabilité de paiement/fraude.
- **Account Service** : PostgreSQL et EF Core ; l’agrégat `Account` est propriétaire du solde et applique les mouvements, contrôles d’état, devise et fonds disponibles. Il consomme `TransactionCompletedIntegrationEvent`, met à jour le solde et publie `AccountTransactionAppliedIntegrationEvent`.
- **Transaction Service** : API `deposit`, `withdrawal`, `transfer`, `GET`; EF Core/PostgreSQL et index unique sur `IdempotencyKey`. L’handler crée `Pending`, appelle immédiatement `Complete()`, publie `TransactionCompletedIntegrationEvent`, sauvegarde puis répond `202`. Son événement est donc traité comme une instruction d’application du solde, malgré son nom « completed ».
- **Movement Service** : consomme `TransactionCompletedIntegrationEvent`, enregistre une projection de mouvement et vérifie l’existence préalable du `TransactionId` pour éviter les doublons applicatifs.
- **Notification Service** : consomme `AccountTransactionAppliedIntegrationEvent`, déduplique par recherche dans le corps, crée une notification puis appelle l’émetteur externe avant de sauvegarder.
- **Reporting Service** : consomme événements de transaction et d’application compte pour construire ses propres données d’analyse.
- **Messagerie** : MassTransit avec RabbitMQ. `Common/.../OutboxServiceCollectionExtensions.cs` configure bus outbox EF, inbox/déduplication, retry exponentiel et circuit breaker. L’intégration est inégale : Account enregistre ses consommateurs et outbox ; Transaction configure l’outbox et la publication mais son `ConfigureDomainPublishing` semble enregistrer seulement le type achevé, sans consommateur retour ; Movement/Notification/Reporting reçoivent des messages mais la configuration infrastructure est à examiner service par service. Le compose déclare un broker RabbitMQ unique.
- **Événements et domaines** : `TransactionCompletedIntegrationEvent` est dans Shared.Domain ; `AccountTransactionAppliedIntegrationEvent` est aussi partagé. Les événements de domaine Account sont dispatchés via interceptor. Le type `TransactionCompletedIntegrationEvent` est un événement d’intégration utilisé comme commande implicite par Account, ce qui mélange sémantique événement/commande et déclenche une mutation métier chez le consommateur.
- **Idempotence / outbox** : index unique transaction sur clé idempotence ; vérification préalable dans le handler, qui peut produire une course correctement arrêtée par l’index mais transforme le cas répétitif en conflit au lieu de restituer le résultat initial. MassTransit outbox/inbox est configuré, mais son existence et son usage transactionnel effectif doivent être vérifiés pour chaque service/context. Mouvement et reporting ont un garde-fou métier sur TransactionId ; Notification utilise un marqueur fragile dans le texte.
- **Bases** : PostgreSQL pour Account, Transaction, Movement, Notification et Reporting, avec bases nommées séparément dans `docker-compose/postgres/init/01-create-databases.sql`; SQL Server pour Security. PostgreSQL et SQL Server sont tous deux présents dans Docker. Une instance partagée n’implique pas une base métier partagée ; conserver des identifiants et permissions séparés.
- **Tests** : plusieurs tests unitaires Domain/Application et quelques tests d’intégration Account/Security et infrastructure partagée. Aucun test Java/Python n’apparaît dans le dépôt fraud. Le test d’intégration couvrant le scénario complet transaction→compte→mouvement→fraude n’est pas présent dans les fichiers repérés.
- **Cycle transaction/paiement** : dépôts/retraits/virements sont des transactions bancaires internes. Transaction Service publie un message considéré comme achevé ; Account Service applique le solde de façon asynchrone ; Movement, Reporting et Notification suivent. Le statut `Completed` peut précéder un échec du débit (compte absent, inactif, devise, fonds insuffisants), conduisant à une transaction déclarée achevée sans solde appliqué. Les consommateurs de solde doivent aussi garantir l’idempotence au niveau de l’écriture, et le transfert multi-compte n’est pas atomique localement (une défaillance entre les deux comptes exige une stratégie de compensation/atomicité Account).
- **Docker** : compose déploie PostgreSQL, SQL Server, RabbitMQ, les six services, le Gateway. Des identifiants/secrets de développement sont en clair dans la configuration. `transaction` ne dépend pas explicitement de RabbitMQ dans le compose alors qu’il publie des événements ; le démarrage peut donc être fragile. AppHost orchestre les projets sans infrastructure déclarée dans le fichier consulté.

### fraud-detection-project

- **payment-service** (Spring Boot/JPA/PostgreSQL) expose `POST /api/v1/transactions` et `GET /{transactionId}`, persiste une entité `Transaction` `PENDING`, puis publie de façon asynchrone sur Kafka. Il reçoit APPROVED ou fraud-alerts et modifie son état à partir du message.
- **fraud-detection-service** consomme `transactions`, applique règles Redis, calcule un score ML REST, persiste les alertes dans PostgreSQL et publie APPROVED ou fraud-alerts. Les règles couvrent montant absolu, écart à moyenne EMA, vélocité, déplacement géographique, nouveau terminal et mots-clés commerçants. Le seuil de blocage ML est configuré à 0,7 (commentaire incohérent avec le README qui annonce 0,75) ; REVIEW à 0,30. Les alertes n’existent que pour REVIEW/BLOCKED et contiennent motifs/score ; APPROVED est un simple identifiant.
- **ML** : `ml-training/train_model.py` entraîne un Random Forest ; `ml_service.py` sert `/score`; `RestMlScorer` fait un appel HTTP avec délai de 1 seconde et bascule vers `StubMlScorer` en cas d’erreur. Il n’y a pas d’artefact/version modèle attaché à la décision dans les structures observées.
- **Redis** : état d’utilisateur pour compteur, moyenne, terminal et position ; ce n’est pas une base métier. Les opérations de lecture/écriture multiples ne forment pas une décision atomique. La clé vélocité est incrémentée à l’évaluation : une redélivrance Kafka peut compter plusieurs fois.
- **PostgreSQL** : les deux applications pointent sur `frauddb` avec les mêmes identifiants. Cela viole l’isolation attendue, même si les tables diffèrent. `ddl-auto: update`; pas d’outbox observée.
- **Kafka** : topics transactions, approved, fraud-alerts ; événement transaction partitionné par userId selon le README. Pas d’outbox, accusé de confirmation producteur exploité, schéma/version de contrat, correlation ID, stratégie DLQ ou déduplication observés dans les classes consultées. La publication est non bloquante avant réponse; l’écriture DB et publication Kafka ne sont pas atomiques. Le listener de verdict ne vérifie pas de transition légale ni de répétition.
- **Cycle** : POST persiste PENDING, publie, répond 202 ; fraude consomme, score, persiste éventuellement alerte et publie verdict ; paiement met à jour le statut. Aucun mouvement de fonds, réservation ou interaction avec un compte bancaire n’existe : ce service est une démo de scoring et d’état de transaction, pas un moteur de paiement bancaire intégré.
- **Docker** : Zookeeper/Kafka, Redis, un PostgreSQL et Kafka UI. Config locale, credentials en clair, image UI `latest`, pas de healthchecks/depends_on readiness robustes ni réplication Kafka (facteur 1).
- **Tests** : aucun fichier de test Java/Python n’a été repéré. README propose des commandes curl manuelles.

## Réponses aux questions

### 1. Responsabilités communes / chevauchements

Les deux projets créent ou suivent une opération financière nommée transaction/paiement, portent un identifiant et un état asynchrone, et transportent les mêmes faits métier (compte/client, montant, devise, date, référence). Le prototype payment-service chevauche le Transaction Service comme registre d’opération et son API/status ; il ne chevauche pas le propriétaire des fonds, qui est Account Service. Fraude partage un vocabulaire d’événement transactionnel, mais sa décision de risque doit rester un contexte séparé. Le domaine « transaction interne bancaire » ne doit pas être assimilé sans distinction à un paiement marchand externe.

### 2. Responsabilités du Payment Service

Introduire un **Payment Service bancaire** seulement si le produit doit orchestrer paiements externes/acquéreurs, méthodes de paiement, autorisations/captures/remboursements et cycle de vie de paiement. Il reçoit la commande idempotente, vérifie format/autorisation, conserve son agrégat Payment et son état, orchestre fraude et autorisation/réservation via APIs/événements, expose statut, traite callbacks prestataires et assure réconciliation. Il n’accède pas aux bases Account/Transaction et ne calcule pas le score fraude.

### 3. Responsabilités du Transaction Service

Registre et orchestration des opérations bancaires internes (dépôt, retrait, transfert), validation métier de commande, clé idempotence, état opérationnel, journal de référence et publication outbox. Il demande à Account Service d’appliquer/réserver les fonds et ne marque `Completed` qu’après confirmation durable de l’application. Il ne possède ni le solde ni la logique/règle de fraude. À terme, Payment peut référencer une transaction via identifiant/événement sans dupliquer la même responsabilité de registre.

### 4. Responsabilités du Fraud Detection Service

Évaluation pure de risque, règles et modèle, gestion des features/profils de risque, décision APPROVED/REVIEW/BLOCKED avec motifs, score, version de règles/modèle, audit immuable, traitement des cas d’analyse et publication d’une réponse. Aucun mouvement financier, réservation/débit/crédit, accès à la DB bancaire ou décision d’insuffisance de fonds.

### 5. Que faire du payment-service existant ?

Ne pas le fusionner ni l’utiliser tel quel comme nouveau composant connecté à la banque. Le conserver comme prototype indépendant jusqu’à conception/migration explicitement planifiée. Réutiliser comme référence les idées de workflow asynchrone et de scoring, après extraction de contrats et réécriture/adaptation dans un Payment Service .NET conforme aux conventions/observabilité/outbox de la plateforme (ou conserver un service Java séparé avec contrat stable si c’est un choix d’exploitation assumé). Ne pas faire cohabiter deux sources de vérité du paiement. Si les paiements externes ne sont pas au périmètre, n’ajouter aucun Payment Service : le Transaction Service soumet les opérations à fraude.

### 6–7. RabbitMQ ou Kafka ? Avantages/inconvénients

**Recommandation : RabbitMQ + MassTransit**, déjà exploités par la plateforme, avec contrat versionné, outbox/inbox et files dédiées. Cela minimise l’ajout d’infrastructure, s’intègre aux patterns existants et convient au workflow de commandes/réponses de services. Inconvénients : moins adapté au rejeu massif et au flux d’événements durable multi-consommateur, exigences de clustering/HA à configurer, couplage aux conventions broker et attention aux files/TTL/DLQ.

Kafka apporte journal durable, rejeu, débit élevé, partitions et ordre par clé (utile aux features séquentielles par client), écosystème stream et plusieurs consommateurs indépendants. En contrepartie : nouveau cluster et compétences/exploitation, gestion des partitions/schémas/offsets, éventuel double broker, et Kafka ne règle pas la cohérence base-message. Le prototype ne justifie pas à lui seul son introduction. Envisager Kafka plus tard si les besoins de rejeu/rétention/analyse de flux à grande échelle sont établis ; éviter une topologie RabbitMQ→Kafka ad hoc sans stratégie d’interopérabilité.

### 8–9. Communication et retour de décision

Le service initiateur publie `FraudEvaluationRequested` via son outbox et place l’opération en `FraudCheckPending`. Fraud Detection consomme de manière idempotente, enregistre sa décision et publie `FraudDecisionRecorded` via son propre outbox. L’initiateur consomme la réponse et avance sa machine d’état. Une API synchrone interne est une alternative si une décision immédiate est indispensable, avec timeout/circuit breaker et persistance de la demande ; éviter le double chemin API + événement pour la même évaluation. Préférer l’asynchrone pour disponibilité et cohérence éventuelle, et exposer au client un statut/polling ou webhook.

Le résultat est un événement de réponse corrélé au `paymentId` ou `transactionId`, `decisionId`, résultat, motifs codés, score facultatif, version de politique/modèle, `evaluatedAt`, `expiresAt` éventuel. APPROVED/REVIEW/BLOCKED sont des résultats explicites. Ne pas envoyer les données sensibles/features non nécessaires dans les événements.

### 10. Événements d’intégration

Noms indicatifs, contrats versionnés : `PaymentInitiated` (si Payment Service existe), `TransactionSubmittedForFraudEvaluation` ou commande `EvaluateTransactionFraud`, `FraudDecisionRecorded`, `FraudReviewRequested`, `FraudReviewResolved`, `PaymentAuthorized`/`PaymentDeclined`, `FundsReserved`/`FundsReservationRejected` (Account), `TransactionApplied`/`TransactionFailed`, `PaymentCompleted`/`PaymentFailed`, `PaymentCancelled`/`PaymentRefunded`. Les messages qui demandent une action doivent être nommés comme commandes ; les événements décrivent un fait déjà survenu. Éviter de transformer l’actuel `TransactionCompletedIntegrationEvent` en commande implicite.

### 11. Machine à états du paiement

`Received → FraudCheckPending → (ReviewPending | FraudApproved | FraudBlocked)` ; `ReviewPending → FraudApproved | FraudBlocked | Cancelled`; après approbation, `AuthorizationPending → Authorized | AuthorizationFailed`; puis `CapturePending → Completed` (ou directement `FundsApplicationPending → Completed` pour débit bancaire interne). Depuis états pending, gérer `Expired`/`Cancelled` selon règles. `REVIEW` ne vaut ni autorisation ni débit : attente d’analyste/step-up et expiration définie. `BLOCKED` est terminal pour cette tentative, aucune opération de fonds. Transitions versionnées/concurrentes, monotones et contrôlées par l’agrégat propriétaire. Ne pas appeler l’opération Completed avant l’accusé Account.

### 12–13. Propriété des soldes et bases

Account Service reste seul propriétaire du solde disponible/comptable et des réservations, avec API/commande d’application; les autres services gardent uniquement identifiants et projections minimales. Base par service : Account → sa base PostgreSQL actuelle ; Transaction → sa propre base PostgreSQL ; Security → SQL Server actuel ; Movement, Notification, Reporting → chacun leur base propre ; Fraud Detection → nouvelle base PostgreSQL dédiée (décisions, audit et données propres) ; Payment, s’il est créé → base dédiée. PostgreSQL peut partager le même serveur/conteneur en développement, mais pas schéma/credentials/accès métier. Redis fraude est son cache/état de features, non source de vérité, avec TTL/versionnement et stratégie de reconstruction.

### 14. APPROVED, REVIEW, BLOCKED

- **APPROVED** : la politique fraude permet de poursuivre l’autorisation métier. Cela ne confirme ni solde suffisant ni paiement exécuté ; seul Account/processor confirme ensuite.
- **REVIEW** : geler le workflow avant toute réservation/débit irréversible ; créer une tâche de revue/step-up, délai/expiration et mécanisme de résolution audité. Pas d’approbation implicite au timeout.
- **BLOCKED** : rejeter la tentative et interdire toute commande d’application de fonds ; notifier avec motif de présentation non sensible. Une réévaluation éventuelle constitue une nouvelle décision référencée.

### 15. Retries

Réessayer uniquement erreurs transitoires et opérations idempotentes, backoff exponentiel avec jitter, limite, timeout et circuit breaker. Broker : redélivrance via inbox/déduplication, DLQ/quarantaine après épuisement, monitoring et replay contrôlé. Erreurs métier permanentes (compte fermé/fonds insuffisants/contrat invalide) doivent être des résultats, pas des retries. Ne jamais rejouer une mutation de solde sans clé de déduplication stable. Le retry de la décision ML ne doit pas ré-incrémenter les compteurs comme nouvelle opération ; séparer l’identification événement/évaluation.

### 16. Idempotence

Clé fournie/générée par client et portée tenant/client + opération, persistée avec contrainte unique et empreinte de requête. Répétition identique renvoie la ressource/résultat initial ; même clé avec payload différent = conflit. Garder `messageId`/`eventId` uniques et table Inbox dans la même transaction que l’effet métier. Déduplication fraude par `evaluationId`/version, avec traitement Redis atomique ou Lua pour compteurs/features. Account doit enregistrer `transactionId`/operationId appliqué dans sa propre DB de manière atomique avec solde et réponse, y compris transfert. Outbox stockée atomiquement avec changement d’état ; ne pas s’appuyer seulement sur une vérification préalable en mémoire/lecture.

### 17. Correlation IDs

Créer ou valider un `correlationId` à la frontière Gateway, le propager dans HTTP, contexte MassTransit (`CorrelationId`, `ConversationId`, `InitiatorId`) et headers; conserver `causationId`, `messageId`, `paymentId/transactionId` séparés. Les consommateurs propagent la corrélation à leurs publications et logs/traces. Ne pas accepter aveuglément un ID mal formé, ne jamais s’en servir comme clé d’idempotence ni y placer de données personnelles.

### 18. Audit fraude

Fraud Detection conserve chaque évaluation/décision (y compris APPROVED) : identifiants/corrélation, heure, version règle/modèle, score, motifs normalisés, données d’entrée minimisées ou référence sécurisée, décision et décision humaine ultérieure. Historique append-only, contrôle d’accès, rétention, horodatage UTC, journal des changements admin/modèle et outbox pour événement de décision. Les logs ne remplacent pas le registre d’audit ; protéger données personnelles et secrets ML.

### 19. Fraud Detection indisponible

Fail closed pour l’autorisation/l’application des fonds : garder l’opération `FraudCheckPending`, ne pas débiter/réserver, répondre `202` avec statut et retry/reprise; après SLA, état `FraudCheckUnavailable`/`Expired` selon politique et intervention explicite. Un fallback ML heuristique peut réduire un mode de panne modèle comme aujourd’hui, mais indisponibilité du service entier ne doit pas devenir APPROVED par défaut. Pour des opérations non risquées définies par politique, éventuel mode dégradé doit être une décision métier explicite, plafonnée, auditée et réconciliable.

### 20. Broker indisponible

Outbox transactionnelle conserve les messages avec le changement métier ; API peut accepter la demande si enregistrement local réussi, indique un état en attente et n’affirme pas l’achèvement. Le relay republie après rétablissement. Consumers persistent leurs effets et Inbox ; health/readiness, backpressure, alertes sur retard/outbox, DLQ et replay opérateur. Ne pas tenter d’écrire directement dans une DB distante ni perdre le message après commit. Aujourd’hui Transaction outbox existe conceptuellement, mais vérifier migration/résolution réelle et démarrage RabbitMQ pour ce service.

### 21. Fichiers/classes à modifier ultérieurement (candidats)

Les fichiers ci-dessous sont une carte de changement future, pas des changements effectués. À confirmer par tests/contrats avant toute implémentation.

**BankSystemMicroservices-local :**

- API/route/proxy : `Gateway/ApiGateway/appsettings.json`, `appsettings.Docker.json`, `Gateway/ApiGateway/Middlewares/CorrelationIdMiddleware.cs`.
- Transaction : `Services/Transaction/src/Transaction.Api/Controllers/TransactionsController.cs`, `Transaction.Application/Commands/CreateTransactionCommand.cs`, `Handlers/TransactionHandlers.cs`, `Transaction.Domain/Entities/Transaction.cs`, `Enums/TransactionStatus.cs`, `Transaction.Infrastructure/Data/TransactionDbContext.cs`, `DependencyInjection.cs`, `Messaging/TransactionEventPublisher.cs`, `Transaction.Api/appsettings*.json`, migrations et tests Transaction.
- Contrats communs : `Common/src/BankSystem.Shared.Domain/Events/Transaction/TransactionCompletedIntegrationEvent.cs`, `Events/Account/AccountTransactionAppliedIntegrationEvent.cs` (à remplacer/versionner plutôt que réutiliser sémantique ambiguë).
- Account : `Services/Account/src/Account.Domain/Entities/Account.cs`, `Account.Infrastructure/Consumers/TransactionCompletedConsumer.cs`, `DependencyInjection.cs`, `Data/AccountDbContext.cs` et tests/inbox/outbox. Propriété du solde demeure inchangée.
- Consumers projections : `Services/Movement/src/Movement.Infrastructure/Consumers/TransactionCompletedConsumer.cs`, `Services/Reporting/src/Reporting.Infrastructure/Consumers/TransactionCompletedConsumer.cs`, `Services/Notification/src/Notification.Infrastructure/Consumers/AccountTransactionAppliedConsumer.cs` et leurs DI/tests.
- Infrastructure : `Common/src/BankSystem.Shared.Infrastructure/Extensions/OutboxServiceCollectionExtensions.cs`, `CrossServiceConsumerExtensions.cs`, `Configuration/MassTransitOptions.cs`; `docker-compose/docker-compose.yml`, `docker-compose/postgres/init/01-create-databases.sql`, `AppHost/AppHost.cs`; configurations appsettings des services concernés.
- Tests de référence : `Services/Transaction/tests/**`, `Services/Account/tests/**`, `Common/tests/BankSystem.Shared.Infrastructure.*`; ajouter ultérieurement tests contrat, intégration et résilience dans les projets de tests existants.

**fraud-detection-project (si retenu comme base/adaptation) :**

- `payment-service/.../PaymentController.java`, `Transaction.java`, `TransactionRequest.java`, `TransactionRepository.java`, `TransactionProducer.java`, `VerdictListener.java`, `KafkaProducerConfig.java`, `application.yml`, `pom.xml`.
- `fraud-detection-service/.../FraudConsumer.java`, `RuleEngine.java`, `RestMlScorer.java`, `StubMlScorer.java`, `MlScorer.java`, `UserProfileService.java`, `FraudAlert.java`, `FraudAlertRepository.java`, `TransactionEvent.java`, `KafkaConfig.java`, `FraudAlertController.java`, `application.yml`, `pom.xml`.
- `docker-compose.yml`, `ml-training/train_model.py`, `ml-training/ml_service.py`, `README.md` pour déploiement, contrats et documentation. Aucune modification n’est proposée maintenant.

### 22. Risques architecturaux constatés

1. Transaction bancaire marquée Completed avant application Account asynchrone : incohérence de statut et risque de débits rejetés après annonce de succès.
2. Un fait `TransactionCompleted` agit comme commande de mutation de solde. Clarifier commande de demande et événement de confirmation.
3. Débit/crédit source-destination du transfert n’est pas atomique et la déduplication/effet solde n’est pas explicitement protégée dans Account.
4. Prototype fraud partage le même `frauddb` et credentials entre services : violation directe de l’isolation des données.
5. `save()` puis `Kafka send` dans payment-service n’est pas atomique ; les erreurs/acks de publication ne sont pas traités. De même, sauvegarde d’alerte puis publication sans outbox peut perdre l’un des deux effets.
6. Topics/verdicts hétérogènes, payload APPROVED réduit à une chaîne, parsing permissif, absence de version de contrat/correlation/audit APPROVED.
7. Réception répétée du verdict peut écraser un état terminal ; aucune transition légale/concurrence/expiration n’est validée.
8. Redis peut compter deux fois après redelivery et séquences de features sont non atomiques ; ordre Kafka ne suffit pas à garantir ordre métier si timestamps ou retries diffèrent.
9. Seuils et README divergent (0.70 configuré contre 0.75 annoncé) ; des règles codées en dur, score modèle et règles peuvent se contredire. Scoring fallback automatique change l’autorité de décision sans tracer le scorer utilisé.
10. Features de scoring et données entrantes non authentifiées/normalisées ; exemple de prototype n’intègre ni devise, compte source, auth client, limites par compte ni fraude des remboursements.
11. Partage RabbitMQ par les services bancaires est cohérent mais l’enregistrement/activation de l’outbox et les dépendances Docker semblent inégaux. Vérifier aussi la disponibilité des migrations/outbox Transaction et des tables dans son DbContext.
12. Idempotence Notification par contenu texte est fragile ; outbox ne rend pas automatiquement l’effet externe (email/SMS) exactement une fois.
13. Secrets, mots de passe et clés JWT de développement en clair dans compose/configuration ; ne pas promouvoir ces valeurs.
14. Aucun test automatisé visible dans le projet fraude, et couverture de workflow distribué / erreurs broker limitée dans les tests repérés.

## Architecture cible recommandée

Conserver le dépôt bancaire comme système de référence et RabbitMQ/MassTransit comme bus d’intégration initial. Pour opérations internes, Transaction Service persiste une demande idempotente en `Pending`, publie `FraudEvaluationRequested` par outbox; Fraud Detection renvoie une décision auditée par événement. `APPROVED` autorise ensuite une commande idempotente explicite vers Account Service, qui seul vérifie et applique le solde puis publie `FundsApplied` ou `FundsApplicationRejected`. Transaction Service ne passe à `Completed` qu’après cette confirmation. `REVIEW` suspend le workflow sans mouvement de fonds; `BLOCKED` termine la tentative sans mouvement. Movement, Notification et Reporting consomment les événements de faits confirmés. Chaque service possède sa base, son outbox/inbox et ses contraintes d’idempotence ; fraude garde une base propre et Redis comme état/cache.

Créer un Payment Service distinct uniquement pour le domaine de paiement externe (autorisation/capture/prestataire), avec base et machine d’état propres, en orchestrant Fraud et Transaction/Account par contrats versionnés. Ne pas intégrer le prototype existant comme seconde source de vérité. Cette option est la plus sûre car aucune évaluation fraude ne peut déplacer de fonds, le solde reste sous une autorité unique et les confirmations sont nécessaires avant « completed ». Elle est propre car les responsabilités, données, contrats et états sont séparés, sans accès DB croisé. Elle convient au modèle microservices car les pannes sont absorbées par les outboxes, retries et états en attente, tout en gardant audit, rejeu contrôlé et évolutivité indépendante.
