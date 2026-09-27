# Protocole de la passerelle X2

[中文](interface.md) | [English](interface.en.md) | **Français**

Référence v1.0, alignée sur LinkSoul AgentSDK v1.4.0. Cette page permet de développer un client pour la passerelle Unity du dépôt. Unity est le serveur WebSocket ; l’agent est le client. La passerelle capture le microphone, lit le PCM reçu, affiche le texte et exécute les actions. Vérifiez séparément les capacités de l’appareil lors d’une intégration matérielle.

Avant l’intégration, lancez le robot avec le [guide EXE](simulator.fr.md) ou le [guide du projet Unity](unity.fr.md), puis connectez-le selon le [guide de l’Agent](../example/x2_agent/docs/README.fr.md). Les deux méthodes utilisent le même protocole.

## Connexion et authentification

Adresse : `ws://<robot-host>:9002/api/V1/open-portal/app/wss/agent-sdk`. Les messages sont des trames texte JSON ; l’audio est du PCM en base64. Huit clients peuvent se connecter par défaut (`CompetitionLauncher.MaxConnections`). Désactivez la compression WebSocket (`compression=None` en Python).

Utilisez `127.0.0.1` pour une connexion locale. Un accès distant nécessite de modifier l’adresse d’écoute de la passerelle et de rendre son port accessible ; consultez la [configuration Unity](unity.fr.md).

| En-tête | Valeur |
|---|---|
| `X-App-Id`, `X-App-Key` | Identifiants de l’application |
| `X-Timestamp` | Temps Unix en millisecondes ; fenêtre de ±5 minutes en validation stricte |
| `X-Nonce` | Valeur aléatoire unique par connexion |
| `X-Signature` | HMAC-SHA256, hexadécimal en minuscules |
| `X-Callback-Types` | Tableau JSON `["audio2tts"]` |

```python
import hashlib
import hmac

payload = "GET\n" + path + "\n" + ts + "\n" + nonce
signature = hmac.new(app_secret.encode(), payload.encode(), hashlib.sha256).hexdigest()
```

Le chemin, avec sa casse exacte, participe à la signature. Identifiants de démonstration : `demo-app` / `demo-key` / `demo-secret`. Les clients signent toujours ; le contrôle dépend du build de la passerelle. Ne supposez pas un mode permissif. Sa modification nécessite de reconstruire la passerelle depuis les [sources Unity](../unity-agent-playground/).

| Code HTTP | Signification |
|---|---|
| 101 | Passage au protocole WebSocket accepté |
| 400 | Requête de mise à niveau incorrecte |
| 401 | Échec des identifiants, de la signature ou de l’horodatage |
| 404 | Chemin incorrect |
| 503 | Limite de connexions atteinte |

Attendez `agentsdk.robot_state.sync` avec `state=online`, sans supposer qu’il s’agit de la première trame : des événements audio peuvent arriver avant. Après une déconnexion, reconnectez-vous ; l’exemple attend trois secondes. Aucun message périodique de maintien de connexion n’est imposé au niveau applicatif.

## Enveloppe

En-têtes multi-client : `X-Client-Role` (`agent` par défaut, ou `controller` / `observer`),
`X-Client-Name` (nom ASCII), `X-Audio-Enabled` (`true` par défaut pour agent, sinon `false` ;
un observer ne reçoit jamais le microphone). `X-Control-Enabled` indique la participation initiale
aux actions (true si absent, sauf observer). La console de gestion envoie les deux indicateurs à `false`
pour ne pas interrompre l’Agent à la connexion. La priorité des consoles reste **1000** ;
les agents démarrent à **50**, les observers à **0**. La console active peut régler les autres
connexions non-console entre **0 et 999**. Les valeurs sont réinitialisées à la reconnexion.
À égalité, la connexion la plus ancienne est prioritaire.

Seule la connexion non-observer la plus prioritaire participant au contrôle exécute les actions et interruptions.
Le microphone est envoyé à la connexion audio la plus prioritaire ; une console manuelle peut donc
coexister avec un agent vocal. Les journaux et états sont diffusés à tous. Une commande non autorisée
est rejetée avec `4091`, sans être rejouée. Les rôles sont déclarés par les clients de confiance
et utilisent l’authentification existante.

`agentsdk.session.state` contient `revision`, `controlOwnerCid`, `audioOwnerCid`, `sessions`.
Chaque entrée indique `robotCid`, `name`, `role`, `priority`, `audioEnabled`, `controlActive`,
`audioActive`. La synchronisation initiale inclut aussi les indicateurs de capacité propres au client.
Un agent en attente ne lance ni accueil ni préconnexion cloud. Utilisez la révision la plus récente.
La console active modifie une priorité avec
`{"type":"agentsdk.session.priority.set","eventId":"...","targetRobotCid":"...","priority":800}`.
Le succès diffuse un nouvel état ; une modification interdite, une cible invalide ou une valeur
hors limites renvoie `4093`.

Une console cède ou reprend sa propre participation aux actions avec
`{"type":"agentsdk.session.control.set","eventId":"...","enabled":false}`.
`enabled` doit être un booléen JSON. La priorité reste 1000 et le réglage audio ne change pas.
La première console conserve la gestion via `managementOwnerCid`, même après avoir cédé les actions.
Les sessions exposent `controlEnabled` ; la synchronisation expose aussi `canManage`.
Le succès diffuse un nouvel état, une demande invalide renvoie `4093`.
Les Agents doivent appliquer les droits dès réception, même pendant un tour vocal, puis les revérifier avant une action.

```json
{
  "type": "agentsdk.asr_response.final",
  "agentId": "demo-app",
  "agentMode": "passive",
  "robotCid": "cid-example",
  "cid": "cid-example",
  "eventId": "evt-example",
  "itemId": "item-example",
  "text": "Bonjour"
}
```

Conservez les valeurs reçues de `agentId`, `robotCid`/`cid`, `eventId` et, s’il est fourni, `itemId` lorsque vous répondez à un enregistrement. Elles identifient le robot, le tour et l’élément. L’exemple envoie `agentMode=passive`. L’accueil constitue un tour distinct initié par l’agent après synchronisation.

## Passerelle → agent

| Type | Champs / comportement |
|---|---|
| `agentsdk.robot_state.sync` | `agentId`, `state`, `callbackType="audio2tts"`, `agentMeta` |
| `agentsdk.audio_request.start` | Enveloppe et `itemId` ; début d’enregistrement |
| `agentsdk.audio_request.append` | `audio` en base64, `audioLen` en octets décodés |
| `agentsdk.audio_request.commit` | Fin d’enregistrement après silence ou durée maximale |
| `agentsdk.state_request.meta` | `stateName`, `stateValue`, par exemple `power`, `ok` |
| `agentsdk.skill_response.state` | Extension du simulateur : `skillName`, `state`, `detail` |

Par défaut, l’agent commence l’envoi vers la reconnaissance vocale (ASR) à `start`, transmet les données `append` pendant l’enregistrement et attend le résultat final après `commit`. `audioLen` compte les octets PCM décodés, pas les caractères base64.

### Journaux Unity : `agentsdk.runtime.log`

Cette extension transmet les journaux du moteur et des scripts Unity sur le même WebSocket,
sans abonnement supplémentaire. Les deux clients Python les affichent aussi pendant l’accueil
et le traitement d’un tour de dialogue. L’enveloppe habituelle contient ensuite
`source: "unity"`, `level` (`info`, `warning`, `error`), `logType`
(`Log`, `Warning`, `Error`, `Assert`, `Exception`), `message`, `stackTrace`,
`timestampMs` (millisecondes Unix UTC), `sequence`, `threadId`, `droppedCount` et `truncated`.
Les assertions et exceptions utilisent le niveau `error` ; la pile dépend des réglages Unity.

Le tampon conserve les 256 entrées les plus récentes en cas de déconnexion ou de saturation.
`droppedCount` compte les entrées évincées depuis le démarrage. Au plus 32 entrées sont transférées
par frame et 128 attendent dans la file de diagnostic de la session ; la voix est prioritaire.
Le message et la pile sont limités respectivement à 8192 et 16384 unités UTF-16 ;
`truncated: true` signale une troncature. L’envoi des journaux ne produit pas de nouveaux journaux.
La livraison est sans garantie ni accusé de réception ; les entrées confiées à une ancienne
session ne sont pas rejouées. Les erreurs de compilation, messages de l’éditeur avant l’exécution
et journaux non envoyés avant un crash ne sont pas couverts. Les clients peuvent ignorer cette extension.
La passerelle limite chaque message complet, fragments compris, à 16 MiB ; le dépassement
entraîne une fermeture avec le code `1009`.

## Agent → passerelle

Tous les messages utilisent l’enveloppe de corrélation ci-dessus.

| Type | Contenu |
|---|---|
| `agentsdk.asr_response.middle` | `text`, résultat intermédiaire facultatif |
| `agentsdk.asr_response.final` | `text`, résultat final |
| `agentsdk.llm_response.item.delta` | `itemId`, `text` incrémental, pas la réponse complète |
| `agentsdk.llm_response.item.done` | `itemId`, fin de l’élément textuel |
| `agentsdk.llm_response.done` | Fin du tour LLM |
| `agentsdk.tts_response.item.delta` | `itemId`, `audio`, `audioLen`, fragment PCM |
| `agentsdk.tts_response.item.done` | Fin de l’élément audio |
| `agentsdk.tts_response.done` | Fin du tour audio et réinitialisation de la lecture |
| `agentsdk.xlm_response.skill` | `skillType`, `skillName`, `skillParam` |
| `agentsdk.xlm_response.interrupt` | `interruptType`, par exemple `chat` ; `interruptTips` facultatif |
| `agentsdk.error` | `errorCode`, `errorMsg` |

La lecture commence à l’arrivée des fragments PCM. Le texte LLM et l’audio TTS peuvent s’entrelacer : n’attendez pas la fin complète du LLM pour synthétiser. Le mode par défaut est bidirectionnel ; le mode par phrase sert de repli. Terminez chaque flux par ses messages de fin d’élément et de tour.

## Actions et interruptions

Ce tableau décrit les actions Unity par défaut. Après toute modification, recompilez et vérifiez l’application cible ; le [guide Unity](unity.fr.md) indique où se trouvent les configurations.

| `skillType` | `skillName` | `skillParam` |
|---|---|---|
| `gesture` | `wave_hands`, `open_arms` | `{}` |
| `movement` | `walk` | `{"distanceM": 1.0}` ; plage de référence 0,2–5 m |
| `movement` | `turn` | `{"angleDeg": 90}` ; valeur positive vers la droite |
| `movement` | `stop` | `{}` |
| `emotion` | `happy`, `sad`, `surprised`, `angry`, `love`, `neutral` | `{"durationMs": 3000}` |

Le client vocal `agent.py` expose ces actions via l’outil LLM `robot_skill` ; la démo hors ligne ne reconnaît pas les demandes vocales. Les gestes suivent des trajectoires articulaires, les expressions pilotent le visage et l’énergie TTS anime la bouche. La fin d’un déplacement est signalée une fois le robot stabilisé. Une action inconnue échoue sans couper la chaîne vocale.

Le simulateur renvoie `running`, `done` ou `failed` par `agentsdk.skill_response.state`. L’envoi est asynchrone : une commande envoyée n’est pas une action terminée. Attendez `done` ou `failed` avant une action dépendante. Vérifiez la prise en charge de cette extension lors d’une intégration matérielle.

Une interruption explicite arrête le TTS et les mouvements/gestes en cours. Parler pendant la lecture ne déclenche pas d’interruption dans le build semi-duplex actuel.

| Code d’erreur de l’exemple | Signification |
|---|---|
| 3101 / 3102 | Échec ASR / résultat vide |
| 3201 / 3202 | Échec LLM / réponse vide |
| 3301 | Échec TTS |

Le remplacement, l’arrêt ou l’interruption d’un mouvement ou geste en cours produit aussi
`failed` pour l’ancienne commande, afin de terminer l’attente de son état final.

## Audio et délais

Les gestes durent environ 6 secondes pour `wave_hands` et 6,5 secondes pour `open_arms`.
Le salut lève le bras droit fléchi avec une légère oscillation du poignet ; l’ouverture des bras
utilise une trajectoire arrondie, légèrement décalée entre les côtés, puis un temps de maintien.
Épaules, coudes, poignets et rotation de tête sont coordonnés avec transitions progressives,
limites articulaires et vitesse cible bornée. Un remplacement repart de la cible courante.
Une interruption renvoie immédiatement `failed`, puis ramène doucement au repos.
La stabilisation finale peut allonger légèrement la durée ; attendez `done` avant d’enchaîner.
Les gestes ne commandent ni taille, ni racine, ni jambes ; un reset annule l’ancienne trajectoire.

Audio PCM mono signé 16 bits à 16 000 Hz, encodé en base64 dans JSON. Un fragment montant fait généralement 100 ms, soit 1 600 échantillons ou 3 200 octets. Les valeurs de détection d’activité vocale (VAD) dans `Assets/X02Competition/Robot/Audio/VadGate.cs` sont : seuil RMS de départ 0,02, arrêt 0,008, silence 600 ms et tour maximal 15 000 ms. L’application portable n’expose pas tous les paramètres ; consultez les journaux pour les délais réels. Le VAD s’arrête pendant le TTS pour éviter de capter la voix du robot.

Un tour suit `start → append × N → commit → ASR final`, puis les deltas LLM et fragments TTS entrelacés, les fins de flux et éventuellement les états d’action. Distinguez temps d’enregistrement/silence, attente ASR après commit, premier jeton LLM et premier audio. Aucun délai global fixe n’est garanti.

## Compatibilité et débogage

Les types inconnus sont ignorés. Certains types, dont `llm_response.item.done`, `vlm_*`, `greet_*` et `xlm_response.control`, peuvent être journalisés sans action dans cette version. Conservez les messages de fin pour la compatibilité du protocole.

**F1** affiche le panneau Unity : connexion, texte ASR/LLM et actions. Les boutons exécutent les actions localement. Après synchronisation, l’agent envoie l’accueil sous forme de texte LLM et d’audio TTS. Dans `agent.py`, `--greeting` modifie le texte et la synthèse vocale ; dans la démo, il modifie uniquement les sous-titres et conserve l’enregistrement. Une valeur vide désactive l’accueil. Voir le [guide de l’exemple](../example/x2_agent/docs/README.fr.md).
