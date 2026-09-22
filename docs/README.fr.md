<p align="center">
  <a href="https://github.com/AgiBot-Community">
    <img src="https://github.com/AgiBot-Community.png?size=304" alt="AgiBot Community logo" width="152">
  </a>
</p>

<h1 align="center">X2 Agent Playground</h1>

<p align="center">
  <a href="../README.md"><img src="https://img.shields.io/badge/语言-简体中文-22314E?style=for-the-badge" alt="简体中文"></a>
  <a href="README.en.md"><img src="https://img.shields.io/badge/Language-English-3776AB?style=for-the-badge" alt="English documentation"></a>
  <a href="README.fr.md"><img src="https://img.shields.io/badge/Langue-Français-0055A4?style=for-the-badge" alt="Documentation française"></a>
</p>

<p align="center">
  Dialoguez avec un robot X2 dans Unity à l’aide d’un agent vocal Python et déclenchez des gestes, des déplacements et des expressions. Ce dépôt contient un simulateur Windows portable, des scripts Python d’exemple et la documentation du protocole de la passerelle.
</p>

<p align="center">
  <a href="https://unity.com/releases/editor/archive"><img src="https://img.shields.io/badge/Unity-2022.3-222222?style=flat-square&amp;logo=unity&amp;logoColor=white" alt="Unity 2022.3"></a>
  <a href="https://learn.microsoft.com/dotnet/csharp/"><img src="https://img.shields.io/badge/C%23-512BD4?style=flat-square&amp;logo=dotnet&amp;logoColor=white" alt="C#"></a>
  <a href="https://www.python.org/"><img src="https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat-square&amp;logo=python&amp;logoColor=white" alt="Python 3.10+"></a>
</p>

<p align="center">
  <a href="https://github.com/AgiBot-Community"><img src="https://img.shields.io/badge/Community-AgiBot-181717?style=flat-square&amp;logo=github&amp;logoColor=white" alt="AgiBot Community"></a>
  <a href="https://github.com/AgiBot-Community/Unity-Agent-Playground/issues"><img src="https://img.shields.io/badge/Feedback-GitHub_Issues-238636?style=flat-square&amp;logo=github&amp;logoColor=white" alt="GitHub Issues"></a>
</p>

## Contenu du dépôt

| Chemin | Rôle |
|---|---|
| [exe/](simulator.fr.md) | Exécutable portable `x2模拟器.exe` et empreinte SHA-256 |
| [unity-agent-playground/](unity.fr.md) | Sources Unity, modèles, actions et passerelle |
| [example/](../example/docs/README.fr.md) | Scripts Python de l’agent, modèle de configuration et tests locaux |
| [docs/](index.fr.md) | Index, protocole et guide de développement en trois langues |

Unity capture le microphone, affiche le robot et exécute les actions en tant que serveur WebSocket. L’agent Python reçoit l’audio, appelle la reconnaissance vocale (ASR), un grand modèle de langage (LLM) et la synthèse vocale (TTS), puis renvoie texte, audio et commandes. Démarrez d’abord le simulateur, puis connectez un seul agent.

Le développement utilise Unity **2022.3.62f3c1**, indiqué dans [ProjectVersion.txt](../unity-agent-playground/ProjectSettings/ProjectVersion.txt). Si Hub ne propose pas cette ancienne version, suivez le [guide Unity](unity.fr.md) pour la télécharger depuis le site officiel et l’ajouter au Hub.

## Démarrage rapide

Choisissez un parcours selon votre objectif, puis connectez un agent :

| Objectif | Parcours | Prérequis |
|---|---|---|
| Essayer le robot et ses actions | Parcours A : EXE portable | Windows 10/11 x64 |
| Modifier les scènes, actions ou la passerelle et recompiler | Parcours B : projet Unity | Unity Hub et l’éditeur indiqué |
| Vérifier la connexion Agent et la lecture audio | Lancer `demo.py` après le robot | Python 3.10+, microphone et haut-parleurs |
| Dialoguer et demander des actions à la voix | Arrêter la démo, puis lancer `agent.py` | Clés API Volcengine Speech et Ark |

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

Les tests utilisent des services simulés locaux, sans Unity ni clés API. Le [guide de développement](development.fr.md) décrit les responsabilités des modules, la validation manuelle et les publications.

## Documentation

| Besoin | Guide |
|---|---|
| Installer Unity Hub, ajouter le projet autonome, exécuter et compiler | [Prise en main Unity](unity.fr.md) |
| Modèles, voix, accueil, paramètres et dépannage | [Agent d’exemple](../example/docs/README.fr.md) |
| Démarrage EXE, vérification et changement de vue | [Simulateur](simulator.fr.md) |
| Agents personnalisés, authentification et échanges | [Protocole](interface.fr.md) |
| Toutes les langues disponibles | [Index documentaire](index.fr.md) |
