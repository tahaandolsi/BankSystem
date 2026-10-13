# Tranche P0 : autorisation des API

Les contrôles sont limités aux points d'entrée HTTP. Aucun handler financier,
événement, consommateur, fichier Payment ou schéma de base n'est modifié.

## Identités

- `sub` (ou sa projection JWT `ClaimTypes.NameIdentifier`) et `UserId` désignent
  normalement l'utilisateur authentifié.
- `clientId` et `CustomerId` désignent le client bancaire.
- Exception temporaire Notification : `Notification.UserId` contient actuellement
  le `CustomerId`. Les lectures utilisent donc `clientId`, y compris la lecture par
  identifiant, qui renvoie 404 pour une notification absente ou étrangère. `/send`
  exige la politique existante `AdminAccess`. Aucun consommateur n'est modifié.

Cette exception est une dette technique : interpréter `Notification.UserId` comme
un `sub` masquerait les notifications légitimes ou pourrait attribuer des données
à la mauvaise identité. Une future harmonisation devra examiner les producteurs,
les destinataires de `/send` et les données existantes avant toute migration.

## Contrôles

- Account : lecture par ID et activation, gel, suspension et fermeture exigent
  la propriété bancaire, sauf pour les rôles existants `Admin` et `Manager`.
  La liste personnelle exige un `clientId` GUID non vide.
- Transaction : dépôt, retrait et transfert exigent la propriété du compte source
  `AccountId`. La lecture par ID exige également cette propriété. Le compte
  destinataire n'est pas soumis à une obligation de propriété dans cette tranche.
- Movement : les lectures par compte et par ID exigent la propriété de `AccountId`.
- Transaction et Movement interrogent Account avec le JWT Bearer courant, transmis
  sur la requête uniquement. Ils comparent explicitement `CustomerId` à `clientId`,
  même pour un appelant administratif. Aucun accès administratif implicite.
  Réponse non réussie, redirection, réponse invalide, absence d'identité ou de JWT,
  erreur réseau ou délai dépassé : l'opération reste bloquée.
- Reporting : `generate` remplace le `RequestedBy` fourni par le sujet authentifié.
  Une exécution est lisible uniquement si son `RequestedBy` correspond exactement
  à ce sujet. `/reports/me` reste une synthèse bancaire filtrée par `clientId`.

## Configuration et risques résiduels

Transaction et Movement utilisent `AccountApi:BaseUrl` (variable d'environnement
`AccountApi__BaseUrl`), avec `https://localhost:7119/` comme adresse locale par défaut,
conforme à AppHost. Configurer une adresse de confiance avec barre oblique finale
dans les autres environnements ; par exemple `http://account:8080/` sur le réseau
Docker local. Les redirections automatiques et les cookies du client HTTP sont
désactivés. Aucune désactivation de la validation des certificats TLS.

La disponibilité d'Account et une configuration correcte deviennent nécessaires
aux opérations protégées de Transaction et Movement. Le délai du client est de
10 secondes ; les mécanismes de résilience existants peuvent influencer le délai
observé. Une panne doit rester un refus, jamais une autorisation.

Les anciennes exécutions Reporting ont un `RequestedBy` auparavant fourni par
l'appelant : leur attribution est invérifiable. Celles qui ne correspondent pas au
sujet deviennent inaccessibles ; une ancienne valeur correspondant au sujet reste
lisible sans que cette concordance prouve sa provenance. Aucune donnée n'est
migrée ou corrigée dans cette tranche.

Les vérifications de propriété précèdent les opérations : elles ne garantissent
pas l'atomicité avec une éventuelle modification concurrente du compte. Les autres
points d'entrée que ces contrôleurs ne sont pas couverts par ces ajouts.

Aucun test, compilation ou démarrage de service n'est effectué. Les corrections
ne sont pas validées à l'exécution. La cohérence financière Transaction–Account
reste hors périmètre et attend une autorisation distincte.
