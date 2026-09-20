# Projet Unity

[中文](unity.md) | [English](unity.en.md) | **Français**

[unity-agent-playground/](../unity-agent-playground/) contient les modèles X2, la passerelle Agent et les sources des gestes, expressions et déplacements. La version d’éditeur enregistrée est **2022.3.62f3c1**. Les dépendances URP, Sentis, ML-Agents et URDF Importer suivent la configuration du projet et les paquets locaux fournis.

## Ouvrir et exécuter

Dans Unity Hub, choisissez Open et sélectionnez `unity-agent-playground/`. Attendez l’import et la compilation, ouvrez `Assets/X02Competition/Scenes/scene.unity`, puis cliquez sur Play. **F1** affiche le panneau de débogage. Le mode Play utilise les sources actuelles ; l’EXE portable provient d’un build antérieur et peut proposer des actions différentes.

## Organisation des sources

| Chemin dans le projet Unity | Rôle |
|---|---|
| `Assets/X02Competition/Bootstrap/` | Lanceur et panneau de débogage |
| `Assets/X02Competition/Gateway/` | Service WebSocket et sessions |
| `Assets/X02Competition/Protocol/` | Protocole des messages |
| `Assets/X02Competition/Robot/Skills/` | Gestes, expressions, déplacements et routage |
| `Assets/X02Competition/Scenes/` | Scène et configuration des actions |
| `Assets/RobotModel/` | URDF, maillages et politiques ML |
| `Assets/SceneEnvironment/` | Environnement de la scène |

## Configuration et compilation

La passerelle se configure dans `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs`. L’écoute par défaut est `127.0.0.1:9002`. Reconstruisez après une modification de l’écoute réseau ou de `StrictAuth`. Les actions se configurent dans le `SkillCatalog.asset` de la scène ; les valeurs par défaut sont dans `Robot/Skills/SkillCatalog.cs`.

Sélectionnez Windows x86_64 dans `File → Build Settings`, activez `Run In Background` dans Player Settings et placez le build complet dans `exe/` à la racine du dépôt. Conservez tous les fichiers produits par Unity. Consultez le [guide du simulateur](simulator.fr.md) pour l’empaquetage en un seul fichier.

Voir le [guide de l’Agent](../example/docs/README.fr.md) et le [protocole](interface.fr.md).
