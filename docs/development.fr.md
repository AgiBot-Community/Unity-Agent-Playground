# Développement et maintenance

[中文](development.md) | [English](development.en.md) | **Français**

Ce guide s’adresse aux développeurs qui modifient le client Python, la passerelle Unity ou les fichiers distribués. Pour une première utilisation, consultez le [démarrage rapide](README.fr.md). Cette page précise les responsabilités du code, les vérifications et la livraison.

## Environnement et lancement

Démarrez le robot avec l’[EXE du dépôt](simulator.fr.md) ou la scène principale du [projet Unity](unity.fr.md) en mode Play. Les deux utilisent la même interface Agent ; n’en lancez qu’un à la fois.

Utilisez Python 3.10+. Dans `example/x2_agent/`, installez les dépendances tierces avec `python -m pip install -r requirements.txt`, puis lancez `python agent.py` ou `python demo.py`. Voir le [guide de l’agent](../example/x2_agent/docs/README.fr.md).

Chaque projet possède son `requirements.txt` ; `.env.example` et `.env` appartiennent uniquement à l’Agent vocal.
Priorité Agent : arguments CLI > environnement existant > `example/x2_agent/.env` > valeurs par défaut.
La console ne lit pas `.env` ; les paramètres de signature sont saisis dans son interface. Les imports ne chargent pas les clés.
L’Agent accepte `--env-file <chemin>` pour choisir un autre fichier.

## Responsabilités des modules

`example/x2_agent/x2_agent/agent.py` gère les sessions et la concurrence. Les modules voisins `asr.py`, `llm.py`, `tts.py` et `sentence_tts.py` gèrent les transports. Les messages Unity sont dans `gateway.py`, les trames Doubao dans `speech_protocol.py`, les outils audio et la configuration dans `audio.py` et `config.py`. `example/x2_agent/agent.py` et `example/x2_agent/demo.py` sont les scripts de lancement.

La console démarre avec `example/x2_console/main.py`. Son paquet `x2_console/` contient
uniquement la gestion et la surveillance, sans importer l’Agent ni des bibliothèques cloud.
Le code vocal appartient à l’Agent. Testez les deux distributions lors d’un changement du protocole commun.

Préservez l’annulation, la propagation des erreurs et la fermeture des connexions. N’introduisez pas d’appels réseau synchrones bloquant la chaîne vocale. Vérifiez les deux clients lors d’une modification des champs de la passerelle.

## Vérifications et tests

Depuis la racine du dépôt, avec un environnement Python contenant les dépendances :

```powershell
cd example/x2_agent
python -B -m unittest discover -s tests -v
cd ../x2_console
python -B -m unittest discover -s tests -v
cd ../..
```

Les tests utilisent des services simulés locaux, sans Unity ni clés API.

La régression Unity entre en mode Play réel et vérifie les sons courts, les files audio,
l’annulation des actions, les fragments et limites WebSocket, la fermeture face à un client
lent et la transmission des journaux :

```powershell
# $unityEditor désigne Editor/Unity.exe de la version locale 2022.3.62f3c1
& $unityEditor -batchmode -projectPath "$PWD/unity-agent-playground" `
  -executeMethod AuditRegressionVerification.Run -logFile "$PWD/unity-regressions.log"
```

Fermez d’abord tout éditeur utilisant ce projet. Le test quitte automatiquement :
n’ajoutez pas `-quit`. Vérifiez `AUDIT_REGRESSION_PASSED` et le code de sortie zéro.
Des avertissements, erreurs, assertions et exceptions sont émis volontairement pour tester
leur transmission, sans appel cloud ni microphone.

`MULTI_SESSION_VERIFICATION_PASSED` confirme aussi les connexions simultanées, la priorité fixe
de la console, les autres priorités réglables, les rejets, le routage audio unique,
la diffusion des journaux et le transfert après déconnexion.

Effectuez la validation manuelle en deux étapes :

1. **Intégration locale :** lancez un simulateur et connectez `demo.py`. Vérifiez `state=online`, les sous-titres et la lecture audio, puis testez les actions dans le panneau F1.
2. **Voix et services cloud :** arrêtez la démo, configurez les clés et lancez `agent.py`. Après l’accueil, parlez et vérifiez le texte ASR, la réponse LLM et la lecture TTS. Demandez une action, puis déconnectez et reconnectez le client.

Notez le modèle, les ressources vocales, les résultats et les délais par étape. Distinguez l’enregistrement et la détection du silence du traitement cloud ; une mesure isolée ne garantit pas les performances.

## Modifications et documentation

- Accompagner les changements de comportement de tests locaux ; vérifier les liens relatifs et les trois langues pour la documentation.
- Le chinois n’a pas de suffixe ; l’anglais utilise `.en.md`, le français `.fr.md`. Conserver les identifiants techniques.
- Les tableaux, liens et points d’entrée documentés concernent uniquement les fichiers gérés dans Git ; vérifier les règles pour les nouveaux fichiers. Suivre les [règles du dépôt](../.gitignore) et le [modèle de configuration](../example/x2_agent/.env.example). Ne jamais versionner de clés personnelles.
- Distribuer l’EXE portable et son empreinte uniquement comme pièces jointes GitHub Release, jamais dans Git. `exe/`, `build/` et `release/` sont des dossiers locaux ignorés. Exiger un EXE inférieur à 100 000 000 octets et consigner taille, SHA-256 et vérification de lancement.
- Ne pas imposer de chemins personnels, outils temporaires ou scripts absents du dépôt aux utilisateurs.
- Une PR décrit le problème, le comportement final et les vérifications. Préciser les essais Unity ou cloud non effectués.

## Unity et distribution

Vérifiez les expressions avec le rendu graphique activé :

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod EmotionVerification.Run -logFile "$PWD/emotion-verification.log"
```

N’ajoutez pas `-nographics`. Le marqueur de succès est `EMOTION_VERIFICATION_PASSED`.
Inspectez `.diagnostics/emotions/atlas.png` : neutral, happy, sad, surprised,
angry, love de gauche à droite ; repos en haut, parole en bas. Les contrôles
couvrent position, rotation, dimensions et résolution du panneau, différences
de silhouettes, maintien des yeux/sourcils pendant la parole et retour temporisé.

Pour modifier les gestes, lancez la vérification dans la scène réelle :

```powershell
& $unityEditor -batchmode -projectPath "$PWD/unity-agent-playground" `
  -executeMethod GestureVerification.Run -logFile "$PWD/unity-gestures.log"
```

Elle vérifie les deux gestes, limites, vitesse cible, stabilité, interruptions, remplacements
et reset d’épisode. Elle quitte automatiquement ; n’ajoutez pas `-quit`.
Le succès est indiqué par `GESTURE_VERIFICATION_PASSED`. Les captures frontales/obliques,
le CSV articulaire et les mesures sont dans `.diagnostics/gesture-polish/`.
Sans microphone ni service cloud. Les courbes sont dans `GestureMotion.cs`,
le lissage, les limites et le cycle de vie dans `GesturePlayer.cs`.

Le [projet Unity](../unity-agent-playground/) utilise `2022.3.62f3c1`. Le lanceur, le HUD et les caméras sont dans `Assets/X02Competition/Bootstrap/`, les vérifications dans `Assets/X02Competition/Tests/Editor/`. Compilez les changements via Build Settings selon le [guide Unity](unity.fr.md), puis conservez toute la sortie.

Joignez l’EXE portable et son empreinte à la même [GitHub Release](https://github.com/AgiBot-Community/Unity-Agent-Playground/releases). Après empaquetage, vérifiez le démarrage, les connexions Agent et le changement de vue, puis téléversez les deux fichiers. Un Build Unity normal ne les empaquette ni ne les publie automatiquement.

Pour compiler le Player Windows x64 en batch :

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod X2PlayerBuild.Windows -x2Output "$PWD/build/UnityEnvironment.exe" `
  -logFile "$PWD/unity-build.log"
```

`-x2Output` doit être un chemin EXE absolu. Vérifiez `X2_WINDOWS_BUILD_PASSED`,
puis empaquetez tout le dossier Player avec `tools/unity-packager/pack-x2.ps1`.
