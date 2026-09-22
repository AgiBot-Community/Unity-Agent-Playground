# Projet Unity

[中文](unity.md) | [English](unity.en.md) | **Français**

Ce guide explique comment installer l’éditeur requis, importer le projet, exécuter la scène du robot et compiler une application Windows. Pour essayer directement le robot, utilisez le [simulateur portable](simulator.fr.md). Les deux parcours permettent de connecter le même [agent d’exemple](../example/docs/README.fr.md).

[unity-agent-playground/](../unity-agent-playground/) contient les modèles X2, la passerelle Agent et les sources des gestes, expressions et déplacements. La version d’éditeur enregistrée est **2022.3.62f3c1**. Les dépendances URP, Sentis, ML-Agents et URDF Importer suivent la configuration du projet et les paquets locaux fournis.

## Installer Unity Hub et l’éditeur

L’exécutable fourni `exe/x2模拟器.exe` fonctionne sans Unity Hub ni éditeur. Installez-les pour modifier les scènes, les sources ou reconstruire le simulateur :

1. Téléchargez Unity Hub pour votre système depuis la [page officielle](https://unity.com/download), installez-le et connectez-vous. Activez une licence adaptée dans Settings → Licenses ; Unity Personal est disponible pour les personnes éligibles.
2. Installez **2022.3.62f3c1**, conformément à `ProjectSettings/ProjectVersion.txt`. La liste recommandée du Hub ne contient pas toutes les anciennes versions ; si celle-ci manque, suivez « Télécharger un ancien éditeur » ci-dessous.
3. Si l’éditeur est installé séparément, utilisez Installs → Locate pour sélectionner son exécutable (`Editor/Unity.exe` sous Windows). Installer Hub seul n’installe pas l’éditeur requis.
4. Choisissez les modules selon la plateforme de compilation, comme indiqué ci-dessous. L’exécution de la scène sous Windows ne nécessite pas les modules Android, iOS ou WebGL.

### Télécharger un ancien éditeur

**Pour ce projet, utilisez la version chinoise publiée sur le site Unity Chine.** Le suffixe `c1` fait partie de `2022.3.62f3c1` ; la version mondiale `2022.3.62f3` utilise un autre programme d’installation.

1. Ouvrez la [page des versions Unity Chine](https://unity.cn/releases), sélectionnez la série **2022** et recherchez `2022.3.62f3`. Le titre peut omettre `c1` : vérifiez également le téléchargement de la version chinoise. La révision attendue est `1623fc0bbb97`.
2. Si la fiche propose une installation via Hub, sélectionnez-la, autorisez le navigateur à ouvrir Unity Hub, puis confirmez la version et les modules. Si cette méthode est indisponible, utilisez sous Windows le [programme officiel pour 2022.3.62f3c1](https://download.unitychina.cn/download_unity/1623fc0bbb97/Windows64EditorInstaller/UnitySetup64.exe). Sous macOS ou Linux, choisissez le système et l’architecture correspondants sur la même page.
3. Exécutez le programme d’installation autonome et notez son dossier de destination. Dans Hub, choisissez **Installs → Locate**, puis `Editor/Unity.exe` dans ce dossier sous Windows. Locate ajoute l’éditeur déjà installé sans le télécharger à nouveau.
4. Vérifiez la version complète **2022.3.62f3c1** dans Hub avant d’ajouter le projet. Les autres versions de l’éditeur peuvent rester installées.

**Pour d’autres anciennes versions mondiales :** ouvrez **Installs → Install Editor → Archive → Download archive**, ou directement les [archives mondiales](https://unity.com/releases/editor/archive). Filtrez la version, sélectionnez son lien **Install / Unity Hub** et autorisez l’ouverture du Hub. Vous pouvez aussi choisir un programme d’installation autonome pour votre système, puis ajouter l’éditeur avec Locate. Les archives mondiales ne permettent pas d’omettre le suffixe `c1` requis ici. Une mise à niveau doit faire l’objet d’une migration et de vérifications distinctes ; ne modifiez pas le fichier de version pour contourner ce choix.

### Choisir les modules de compilation

| Usage | Prise en charge requise |
|---|---|
| Exécuter les scènes sous Windows ou compiler avec Mono pour Windows | Incluse dans l’éditeur Windows |
| Compiler avec IL2CPP pour Windows | Windows Build Support (IL2CPP) et outils C++ correspondants |
| Compiler pour Linux | Linux Build Support adapté au backend Mono / IL2CPP |

Pour un éditeur installé via Hub, utilisez **Installs → Manage → Add modules**. Un éditeur installé séparément puis ajouté avec Locate ne propose généralement pas cette option ; Locate ne transforme pas l’installation en une installation gérée par Hub. Pour gérer les modules avec Hub, suivez les instructions officielles et réinstallez l’éditeur requis via Hub. Les paquets de compilation Linux du projet ne remplacent pas les modules de plateforme.

Sources officielles consultées en ligne le 2026-09-23 : [versions chinoises](https://unity.cn/releases), [archives mondiales](https://unity.com/releases/editor/archive), [installation des anciennes versions et Locate](https://docs.unity.com/en-us/hub/add-editor), [gestion des modules](https://docs.unity.com/en-us/hub/add-modules). Le lien Windows était accessible ; le programme d’installation n’a pas été téléchargé ni exécuté pour cette mise à jour documentaire.

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

1. Arrêtez Play et ouvrez **File → Build Settings**. Ajoutez la scène principale avec **Add Open Scenes**, cochez-la et retirez les scènes qui ne doivent pas démarrer avec l’application.
2. Sélectionnez **PC, Mac & Linux Standalone → Windows → x86_64**, puis **Switch Platform** si nécessaire.
3. Activez `Run In Background` dans Player Settings pour traiter les messages de l’agent lorsque la fenêtre perd le focus.
4. Cliquez sur **Build**, choisissez un dossier de sortie vide dédié hors de `Assets/`, puis attendez la fin de la compilation.

Exécutez l’EXE du dossier produit et vérifiez la connexion avec les commandes Agent ci-dessus. Conservez tous les fichiers pour l’exécution et la distribution. Un Build Unity normal ne crée ni ne remplace automatiquement l’[EXE unique](../exe/x2模拟器.exe) du dépôt. Pour Linux, installez le module ci-dessus, changez de cible et utilisez une sortie distincte.

Voir le [guide de l’Agent](../example/docs/README.fr.md) et le [protocole](interface.fr.md).
