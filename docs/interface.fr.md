# Protocole de la passerelle X2

[中文](interface.md) | [English](interface.en.md) | **Français**

Référence v1.0, alignée sur LinkSoul AgentSDK v1.4.0. Unity/le robot est le serveur WebSocket ; l’agent est le client. La passerelle capture le microphone, lit le PCM reçu, affiche le texte et exécute les commandes.

## Connexion et authentification

Adresse : `ws://<robot-host>:9002/api/V1/open-portal/app/wss/agent-sdk`. Les messages sont des trames texte JSON ; l’audio est du PCM en base64. Un seul client peut se connecter. Désactivez la compression WebSocket (`compression=None` en Python).

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
| 503 | Session déjà occupée |

Attendez `agentsdk.robot_state.sync` avec `state=online`, sans supposer qu’il s’agit de la première trame : des événements audio peuvent arriver avant. Après une déconnexion, reconnectez-vous ; l’exemple attend trois secondes. Aucun message périodique de maintien de connexion n’est imposé au niveau applicatif.

## Enveloppe

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

Par défaut, l’agent ouvre l’envoi ASR à `start`, transmet les données `append` pendant l’enregistrement et attend le résultat final après `commit`.

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

Ce tableau correspond aux sources Unity actuelles. L’EXE portable n’a pas été reconstruit à partir de ces sources et peut proposer des actions différentes.

| `skillType` | `skillName` | `skillParam` |
|---|---|---|
| `gesture` | `wave_hands`, `open_arms` | `{}` |
| `movement` | `walk` | `{"distanceM": 1.0}` ; plage de référence 0,2–5 m |
| `movement` | `turn` | `{"angleDeg": 90}` ; valeur positive vers la droite |
| `movement` | `stop` | `{}` |
| `emotion` | `happy`, `sad`, `surprised`, `angry`, `love`, `neutral` | `{"durationMs": 3000}` |

L’exemple expose ces actions via l’outil LLM `robot_skill`. Les gestes suivent des trajectoires articulaires, les expressions pilotent le visage et l’énergie TTS anime la bouche. La fin d’un déplacement est signalée une fois le robot stabilisé. Une action inconnue échoue sans couper la chaîne vocale.

Le simulateur renvoie `running`, `done` ou `failed` par `agentsdk.skill_response.state`. L’exécution est asynchrone ; le client peut utiliser ces états pour coordonner la suite. Une intégration matérielle peut ignorer cette extension de simulation.

Une interruption explicite arrête le TTS et les mouvements/gestes en cours. Parler pendant la lecture ne déclenche pas d’interruption dans le build semi-duplex actuel.

| Code d’erreur de l’exemple | Signification |
|---|---|
| 3101 / 3102 | Échec ASR / résultat vide |
| 3201 / 3202 | Échec LLM / réponse vide |
| 3301 | Échec TTS |

## Audio et délais

Audio PCM mono signé 16 bits à 16 000 Hz, encodé en base64 dans JSON. Un fragment montant fait généralement 100 ms, soit 1 600 échantillons ou 3 200 octets. Paramètres VAD de référence du build initial : seuil RMS de départ 0,02, arrêt 0,008, silence 600 ms, tour maximal 15 000 ms. Tous les paramètres ne sont pas exposés dans le binaire ; consultez les journaux pour les délais réels. Le VAD s’arrête pendant le TTS pour éviter de capter la voix du robot.

Un tour suit `start → append × N → commit → ASR final`, puis les deltas LLM et fragments TTS entrelacés, les fins de flux et éventuellement les états d’action. Distinguez temps d’enregistrement/silence, attente ASR après commit, premier jeton LLM et premier audio. Aucun délai global fixe n’est garanti.

## Compatibilité et débogage

Les types inconnus sont ignorés. Certains types, dont `llm_response.item.done`, `vlm_*`, `greet_*` et `xlm_response.control`, peuvent être journalisés sans action dans cette version. Conservez les messages de fin pour la compatibilité du protocole.

**F1** affiche le panneau Unity : connexion, texte ASR/LLM et actions. Les boutons exécutent les actions localement. Après synchronisation, l’exemple envoie l’accueil sous forme de texte LLM et d’audio TTS. `--greeting` le modifie ; une valeur vide le désactive. Voir le [guide de l’exemple](../example/docs/README.fr.md).
