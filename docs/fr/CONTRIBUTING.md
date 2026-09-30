# Contribuer

[中文](../../CONTRIBUTING.md) | [English](../en/CONTRIBUTING.md) | **Français**

Proposez corrections, fonctionnalités, documentation et traductions par pull request. Consultez le [README](README.md) pour l’installation et le [guide de développement](development.md) pour les modules et les commandes de test.

## Signaler un problème

Recherchez d’abord les [issues existantes](https://github.com/AgiBot-Community/Unity-Agent-Playground/issues). Indiquez :

- La version publiée ou le commit, le système et les versions Python / Unity.
- Les étapes minimales de reproduction, le résultat attendu et le résultat obtenu.
- Les journaux utiles ; ajoutez des captures ou une courte vidéo pour les problèmes d’interface ou de mouvement.
- Les identifiants du modèle, de la voix et des ressources pour les problèmes vocaux. Retirez les clés API, signatures et conversations privées des journaux.

Pour une fonctionnalité, décrivez le cas d’usage, la limite actuelle et le résultat souhaité. Discutez les changements importants de protocole, dépendances ou organisation dans une issue avant de les réaliser.

## Proposer une modification

1. Créez un fork et une branche de travail depuis la branche cible.
2. Limitez chaque PR à un problème, avec les tests ou la documentation correspondants.
3. Exécutez les vérifications des composants concernés et indiquez les résultats, y compris les tests Unity ou cloud non exécutés.
4. Décrivez le problème, le comportement obtenu et les étapes de vérification dans la PR. Ajoutez les liens vers les issues concernées.

Les tests Python utilisent des services simulés locaux, sans Unity ni clés cloud. Après installation des dépendances correspondantes, exécutez depuis la racine :

```powershell
cd example/x2_agent
python -B -m unittest discover -s tests -v
cd ../x2_console
python -B -m unittest discover -s tests -v
cd ../..
```

Vérifiez l’agent et la console lors d’une modification du protocole. Pour les actions, l’audio ou les sessions Unity, utilisez les vérifications du [guide de développement](development.md).

## Documentation et traductions

Le README présente le projet et son démarrage minimal. Les guides décrivent les opérations, la référence du protocole définit les messages et comportements, et le guide de développement couvre tests et publications. Placez les ajouts dans la page correspondante et liez-les depuis l’[index](../README.md).

Les guides généraux se trouvent dans `docs/zh-CN/`, `docs/en/` et `docs/fr/`, avec les mêmes noms de fichiers par sujet. Le README du projet et CONTRIBUTING chinois restent à la racine ; leurs traductions sont dans les dossiers de langue. Les guides de l’agent suivent la même organisation dans `example/x2_agent/docs/`. Mettez à jour les traductions lors des changements de comportement ou de paramètres. Conservez commandes, champs du protocole, identifiants des modèles et noms de fichiers.

Décrivez des étapes, valeurs par défaut et résultats précis. Écartez les journaux de travail personnels, notes de vérification temporaires et chemins propres à une machine. Vérifiez les liens relatifs, ancres et répertoires de travail des commandes.

## Fichiers et licences

Conservez les fichiers Unity `.meta`. Ne versionnez pas `.env`, clés, journaux, caches ou sorties de compilation ; consultez [`.gitignore`](../../.gitignore). Publiez exécutables portables et empreintes via GitHub Releases selon le [guide de développement](development.md).

Le code original utilise [Apache License 2.0](../../LICENSE). Conservez les licences et mentions de provenance des ajouts tiers.
