# X2 Agent Playground

[![AgiBot Community](https://img.shields.io/badge/Community-AgiBot-181717?style=flat-square&logo=github&logoColor=white)](https://github.com/AgiBot-Community)
[![GitHub Issues](https://img.shields.io/badge/Feedback-GitHub_Issues-238636?style=flat-square&logo=github&logoColor=white)](https://github.com/AgiBot-Community/Unity-Agent-Playground/issues)

![Unity 2022.3](https://img.shields.io/badge/Unity-2022.3-222222?style=flat-square&logo=unity&logoColor=white)
![C#](https://img.shields.io/badge/C%23-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Python 3.10+](https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat-square&logo=python&logoColor=white)

🌐 [中文 ↗](../README.md) | [English ↗](README.en.md) | **Français**

Dialoguez avec un robot X2 dans Unity à l’aide d’un agent vocal Python et déclenchez des gestes, des déplacements et des expressions. Ce dépôt contient un simulateur Windows portable, des scripts Python d’exemple et la documentation du protocole de la passerelle.

## Contenu du dépôt

| Chemin | Rôle |
|---|---|
| [exe/](simulator.fr.md) | Exécutable portable `x2模拟器.exe` et empreinte SHA-256 |
| [unity-agent-playground/](unity.fr.md) | Sources Unity, modèles, actions et passerelle |
| [example/](../example/docs/README.fr.md) | Scripts Python de l’agent, modèle de configuration et tests locaux |
| [docs/](index.fr.md) | Index, protocole et guide de développement en trois langues |

Unity joue le rôle de serveur WebSocket. L’agent Python reçoit le son du microphone, appelle les services ASR, LLM et TTS, puis renvoie texte, audio et commandes au robot. Le simulateur et l’agent se lancent séparément.

Ces guides s’appuient sur les exécutables, sources, modèles de configuration et documents gérés dans Git. La version de l’éditeur est indiquée dans [ProjectVersion.txt](../unity-agent-playground/ProjectSettings/ProjectVersion.txt), actuellement `2022.3.62f3c1`.

## Démarrage rapide

Choisissez une méthode pour démarrer le robot. L’EXE ne nécessite ni Unity ni Python ; l’agent d’exemple nécessite Python 3.10+. L’interaction vocale demande un microphone et des haut-parleurs. Les conversations réelles nécessitent des clés API Volcengine Speech et Ark.

### Parcours A : démarrer depuis l’EXE

1. Sous Windows 10/11 x64, ouvrez [exe/x2模拟器.exe](../exe/x2模拟器.exe) et attendez la fenêtre du robot. L’[empreinte SHA-256](../exe/x2模拟器.sha256) permet de vérifier le téléchargement.
2. **F1** affiche le panneau. Ses boutons permettent de tester les actions sans agent.
3. Pour les échanges vocaux, poursuivez avec « Démarrer l’agent » ci-dessous. Consultez le [guide EXE](simulator.fr.md).

### Parcours B : démarrer depuis le projet Unity

1. Installez Unity Hub et l’éditeur **2022.3.62f3c1** en suivant le [guide Unity](unity.fr.md).
2. Dans Hub, choisissez **Add project from disk** puis [unity-agent-playground/](../unity-agent-playground/), qui contient `Assets/`, `Packages/` et `ProjectSettings/`.
3. Attendez l’import et la compilation, ouvrez `Assets/X02Competition/Scenes/scene.unity`, puis cliquez sur **Play**.
4. Activez la fenêtre Game et appuyez sur **F1**. Démarrez ensuite l’agent ci-dessous. Cliquez de nouveau sur Play pour arrêter.

Les deux parcours écoutent sur `127.0.0.1:9002` : ne lancez qu’un simulateur ou une scène Editor à la fois. **C** change de vue ; **F2–F5** sélectionnent la vue générale, le suivi de face, le suivi latéral et l’orbite libre. Cette dernière utilise le bouton droit et la molette.

### Démarrer l’agent (les deux parcours)

Gardez le robot actif et ouvrez PowerShell à la racine du dépôt :

```powershell
cd example
python -m pip install -r requirements.txt
python demo.py
```

La démo renvoie du texte fixe et lit les enregistrements fournis pour vérifier la passerelle et la chaîne audio. Elle ne reconnaît pas la parole et ne synthétise pas de réponse en temps réel. Un enregistrement absent ou invalide est remplacé par un signal sinusoïdal.

`state=online` confirme la connexion. Arrêtez la démo avec Ctrl+C, puis utilisez le modèle `.env.example` du dépôt dans le même terminal `example/` pour configurer et lancer Doubao :

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Renseigner DOUBAO_SPEECH_API_KEY et ARK_API_KEY dans .env
python agent.py
```

Attendez la fin de l’accueil avant de parler. Exemples en chinois : « 你好 » (bonjour), « 挥挥手 » (faire un signe de la main), « 往前走一米 » (avancer d’un mètre). Un seul agent peut se connecter. Le fonctionnement est semi-duplex : la détection vocale s’arrête pendant la lecture. Parler n’interrompt donc pas la réponse ; une commande explicite du protocole permet de l’interrompre.

Les trois langues de documentation ne signifient pas que les services vocaux, la voix chinoise par défaut ou l’interface Unity sont adaptés à ces trois langues.

## Distribution et compilation des sources

Le fichier `exe/x2模拟器.exe` du dépôt peut être distribué seul ; son empreinte est dans le fichier `.sha256` adjacent. L’agent Python est fourni séparément dans `example/`.

Après une modification des sources, utilisez Build Settings selon le [guide Unity](unity.fr.md) et conservez tout le dossier produit. Les modifications ne mettent pas automatiquement à jour l’EXE portable du dépôt.

## Développement et validation

```powershell
cd example
# Tests locaux, sans appel cloud
python -B -m unittest discover -s tests -v
```

Consultez le [guide de développement](development.fr.md). Les [règles Git](../.gitignore) et celles du projet définissent les fichiers concernés. Les modèles de configuration et l’EXE sont des livrables du dépôt ; les clés personnelles ne doivent pas être versionnées.

## Documentation

| Besoin | Guide |
|---|---|
| Installer Unity Hub, ajouter le projet autonome, exécuter et compiler | [Prise en main Unity](unity.fr.md) |
| Modèles, voix, accueil, paramètres et dépannage | [Agent d’exemple](../example/docs/README.fr.md) |
| Démarrage EXE, vérification et changement de vue | [Simulateur](simulator.fr.md) |
| Agents personnalisés, authentification et échanges | [Protocole](interface.fr.md) |
| Toutes les langues disponibles | [Index documentaire](index.fr.md) |
