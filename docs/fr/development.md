# Développement et maintenance

[中文](../zh-CN/development.md) | [English](../en/development.md) | **Français**

Cette page décrit les modules, tests et publications. Consultez le [démarrage rapide](README.md) pour une première utilisation et le [guide de contribution](CONTRIBUTING.md) pour les issues et pull requests.

## Environnement et lancement

Pour les tests d’intégration, lancez le [simulateur portable](simulator.md) ou la scène principale du [projet Unity](unity.md) en mode Play. Les deux utilisent la même interface Agent ; exécutez une seule instance à la fois.

Utilisez Python 3.10+. Dans `example/x2_agent/`, installez les dépendances tierces avec `python -m pip install -r requirements.txt`, puis lancez `python agent.py` ou `python demo.py`. Voir le [guide de l’agent](../../example/x2_agent/docs/fr/README.md).

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
lent et la transmission des journaux. Elle couvre aussi plusieurs tours de dialogue,
la reprise après erreur ou expiration et les fermetures concurrentes :

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
2. **Voix et services cloud :** arrêtez la démo, configurez les clés et lancez `agent.py`. Après l’accueil, enchaînez plusieurs tours. Vérifiez à chaque fois le texte ASR, la réponse LLM, le TTS et la reprise de l’écoute, puis testez les actions, l’interruption explicite et la reconnexion. Un nouvel enregistrement ne doit pas remplacer un tour dont la réponse est encore attendue.

Notez le modèle, les ressources vocales, les résultats et les délais par étape. Distinguez l’enregistrement et la détection du silence du traitement cloud ; une mesure isolée ne garantit pas les performances.

## Tests des composants Unity

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

## Compilation et publication

Le [projet Unity](../../unity-agent-playground/) utilise `2022.3.62f3c1`. Le lanceur, le HUD et les caméras sont dans `Assets/X02Competition/Bootstrap/`, les vérifications dans `Assets/X02Competition/Tests/Editor/`. Compilez les changements via Build Settings selon le [guide Unity](unity.md), puis conservez toute la sortie.

La compilation Unity produit le dossier du Player ; l’empaquetage et la publication sont des étapes distinctes.

Pour compiler le Player Windows x64 en batch :

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod X2PlayerBuild.Windows -x2Output "$PWD/build/UnityEnvironment.exe" `
  -logFile "$PWD/unity-build.log"
```

`-x2Output` doit être un chemin EXE absolu. Vérifiez `X2_WINDOWS_BUILD_PASSED`,
puis empaquetez tout le dossier Player avec `tools/unity-packager/pack-x2.ps1`.

Pour publier :

1. Nommez l’exécutable `x2-simulator-windows-x64.exe` et vérifiez que le fichier `.sha256` indique ce nom exact.
2. Vérifiez que l’EXE fait moins de 100 000 000 octets. Testez l’extraction dans un cache neuf, le démarrage, les connexions Agent, les actions et les caméras. Notez la version et le SHA-256.
3. Téléversez l’EXE et son empreinte dans la même GitHub Release.

`exe/`, `build/` et `release/` sont des dossiers locaux ignorés. Les fichiers distribués ne sont pas versionnés dans le dépôt source.
