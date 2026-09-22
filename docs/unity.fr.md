# Projet Unity

[中文](unity.md) | [English](unity.en.md) | **Français**

Cette page décrit le parcours complet depuis le projet Unity. Pour essayer l’application prête à l’emploi, consultez [Démarrer depuis l’EXE](simulator.fr.md). Les deux parcours utilisent le même [agent d’exemple](../example/docs/README.fr.md).

[unity-agent-playground/](../unity-agent-playground/) contient les modèles X2, la passerelle Agent et les sources des gestes, expressions et déplacements. La version d’éditeur enregistrée est **2022.3.62f3c1**. Les dépendances URP, Sentis, ML-Agents et URDF Importer suivent la configuration du projet et les paquets locaux fournis.

## Installer Unity Hub et l’éditeur

L’exécutable fourni `exe/x2模拟器.exe` fonctionne sans Unity Hub ni éditeur. Installez-les pour modifier les scènes, les sources ou reconstruire le simulateur :

1. Téléchargez Unity Hub pour votre système depuis la [page officielle](https://unity.com/download), installez-le et connectez-vous. Activez une licence adaptée dans Settings → Licenses ; Unity Personal est disponible pour les personnes éligibles.
2. Installez **2022.3.62f3c1**, version exacte indiquée dans `ProjectSettings/ProjectVersion.txt`. Consultez Installs → Install Editor ou la [page des versions Unity Chine](https://unity.cn/releases). La version avec suffixe `c1` peut être absente des [archives mondiales](https://unity.com/releases/editor/archive). Ne la remplacez pas directement par Unity 6 et ne modifiez pas le fichier de version pour contourner la vérification.
3. Si l’éditeur est installé séparément, utilisez Installs → Locate pour sélectionner son exécutable (`Editor/Unity.exe` sous Windows). Installer Hub seul n’installe pas l’éditeur requis.
4. Sous Windows, l’éditeur inclut la prise en charge des scènes et des builds Windows Mono. IL2CPP nécessite Windows Build Support et les outils C++ correspondants. Pour Linux, ajoutez **Linux Build Support** pour le backend de script choisi via Add modules. Les dépendances de compilation Linux conservées dans le dépôt ne remplacent pas ce module de l’éditeur.

## Ajouter le projet autonome

Copiez le dossier entier pour utiliser le projet indépendamment du dépôt parent. Les deux paquets sources fournis, ML-Agents et URDF Importer, sont intégrés dans `Packages/` ; le premier import nécessite toujours un accès réseau pour les dépendances du registre. Consultez les [instructions autonomes du projet](../unity-agent-playground/README.md).

Décompressez entièrement le dépôt avant de l’ouvrir :

```text
Unity-Agent-Playground/          Dépôt
└── unity-agent-playground/      Projet Unity autonome à ajouter dans Hub
    ├── Assets/
    ├── Packages/
    └── ProjectSettings/
        └── ProjectVersion.txt
```

1. Dans Hub, choisissez **Projects → Add → Add project from disk** (Open dans certaines versions). Sélectionnez le dossier intérieur `unity-agent-playground/`, ou sa copie contenant les trois dossiers ci-dessus.
2. Choisissez l’éditeur `2022.3.62f3c1` et ouvrez le projet. Attendez la résolution des dépendances, l’import et la compilation. Conservez les fichiers `.meta` dans `Assets/` ainsi que l’intégralité de `Packages/` et `ProjectSettings/` lors de la copie.

**« No projects found. Select a folder that contains Unity projects. »** provient de l’entrée **Import projects**, qui recherche plusieurs projets dans les sous-dossiers. Utilisez Add project from disk, ou sélectionnez le dossier parent pour cette recherche. Le dossier parent ne devient pas pour autant le projet Unity.

## Exécuter la scène et connecter un Agent

1. Ouvrez `Assets/X02Competition/Scenes/scene.unity` depuis le panneau Project. Corrigez les erreurs rouges dans Window → General → Console avant de passer en mode Play.
2. Cliquez sur **Play**, activez la fenêtre Game et appuyez sur **F1** pour afficher le panneau de débogage. La passerelle écoute sur `127.0.0.1:9002`. Fermez l’EXE portable avant de lancer la scène dans l’éditeur pour éviter un conflit de port.
3. Pour tester la connexion, ouvrez un autre terminal à la racine du dépôt :

```powershell
python -m pip install -r example/requirements.txt
python example/demo.py
```

La démo ne nécessite aucune clé cloud. Pour les conversations vocales, suivez le [guide de l’Agent](../example/docs/README.fr.md), arrêtez la démo avec Ctrl+C puis lancez `python example/agent.py`. Les scripts Python se lancent directement, sans installer ce dépôt comme paquet Python. Si vous avez copié uniquement le projet Unity, préparez un client Agent compatible séparément.

`state=online` dans le terminal Agent confirme la connexion. Cliquez de nouveau sur Play pour arrêter la scène et utilisez Ctrl+C pour arrêter l’agent. Les modifications des sources ne mettent pas automatiquement à jour l’EXE portable.

**C** change de vue ; **F2** sélectionne la vue générale, **F3** le suivi de face, **F4** le suivi latéral et **F5** l’orbite libre. En orbite, maintenez le bouton droit dans la scène pour tourner et utilisez la molette pour régler la distance. Toutes les vues cadrent le robot entier. Le panneau F1 propose aussi des boutons de sélection.

## Dépannage

| Symptôme | Solution |
|---|---|
| Hub ne trouve aucun projet | Sélectionnez le projet intérieur avec Add project from disk, ou son parent avec Import projects. |
| Éditeur manquant | Installez la version complète `2022.3.62f3c1`, ou ajoutez l’installation existante avec Installs → Locate. |
| Résolution bloquée ou erreur de paquet | Vérifiez le réseau et les erreurs dans Console / Window → Package Manager. Conservez les manifestes et paquets intégrés de `Packages/`. |
| Robot absent | Ouvrez la scène indiquée, passez en mode Play et consultez la fenêtre Game. |
| Échec de la passerelle ou port 9002 occupé | Arrêtez l’autre EXE ou scène en cours. Exécutez un seul simulateur et connectez un seul Agent à la fois. |
| Connexion de l’Agent impossible | Vérifiez le mode Play et le démarrage de la passerelle, puis l’adresse selon le guide. `127.0.0.1` ne permet pas de joindre une autre machine. |

## Organisation des sources

| Chemin dans le projet Unity | Rôle |
|---|---|
| `Assets/X02Competition/Bootstrap/` | Lanceur, panneau et cadrage des caméras dans `RobotCameraRig.cs` |
| `Assets/X02Competition/Gateway/` | Service WebSocket et sessions |
| `Assets/X02Competition/Protocol/` | Protocole des messages |
| `Assets/X02Competition/Robot/Skills/` | Gestes, expressions, déplacements et routage |
| `Assets/X02Competition/Scenes/` | Scène et configuration des actions |
| `Assets/RobotModel/` | URDF, maillages et politiques ML |
| `Assets/SceneEnvironment/` | Environnement de la scène |

## Configuration et compilation

Configurez le port et `StrictAuth` sur le composant `CompetitionLauncher` de la scène, implémenté dans `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs`. L’adresse d’écoute est définie dans `Assets/X02Competition/Gateway/LinkskyGatewayServer.cs` et utilise la boucle locale par défaut. Les actions utilisent le `SkillCatalog.asset` de la scène ; leurs valeurs par défaut sont dans `Assets/X02Competition/Robot/Skills/SkillCatalog.cs`. Recompilez après modification.

Arrêtez Play, ouvrez `File → Build Settings`, ajoutez la scène principale avec Add Open Scenes et cochez-la. Retirez les scènes inutiles à la distribution. Sélectionnez PC, Mac & Linux Standalone → Windows → x86_64, puis Switch Platform si nécessaire. Activez `Run In Background` dans Player Settings, cliquez sur Build et choisissez un dossier de sortie vide dédié, hors de `Assets/`.

Exécutez l’EXE du dossier produit et vérifiez la connexion avec les commandes Agent ci-dessus. Conservez tous les fichiers pour l’exécution et la distribution. Un Build Unity normal ne crée ni ne remplace automatiquement l’[EXE unique](../exe/x2模拟器.exe) du dépôt. Pour Linux, installez le module ci-dessus, changez de cible et utilisez une sortie distincte.

Voir le [guide de l’Agent](../example/docs/README.fr.md) et le [protocole](interface.fr.md).
