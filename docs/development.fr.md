# Développement et maintenance

[中文](development.md) | [English](development.en.md) | **Français**

Ce guide s’adresse aux développeurs qui modifient le client Python, la passerelle Unity ou les fichiers distribués. Pour une première utilisation, consultez le [démarrage rapide](README.fr.md). Cette page précise les responsabilités du code, les vérifications et la livraison.

## Environnement et lancement

Démarrez le robot avec l’[EXE du dépôt](simulator.fr.md) ou la scène principale du [projet Unity](unity.fr.md) en mode Play. Les deux utilisent la même interface Agent ; n’en lancez qu’un à la fois.

Utilisez Python 3.10+. Dans `example/`, installez les dépendances tierces avec `python -m pip install -r requirements.txt`, puis lancez `python agent.py` ou `python demo.py`. Voir le [guide de l’agent](../example/docs/README.fr.md).

Déclarez les dépendances Python dans `example/requirements.txt`. Priorité : arguments CLI > variables d’environnement existantes > `example/.env` > valeurs par défaut. Les imports ne chargent pas les clés. Utilisez `--env-file <chemin>` pour choisir un autre fichier.

## Responsabilités des modules

`example/x2_agent/agent.py` gère les sessions et la concurrence. Les modules voisins `asr.py`, `llm.py`, `tts.py` et `sentence_tts.py` gèrent les transports. Les messages Unity sont dans `gateway.py`, les trames Doubao dans `speech_protocol.py`, les outils audio et la configuration dans `audio.py` et `config.py`. `example/agent.py` et `example/demo.py` sont les scripts de lancement.

Préservez l’annulation, la propagation des erreurs et la fermeture des connexions. N’introduisez pas d’appels réseau synchrones bloquant la chaîne vocale. Vérifiez les deux clients lors d’une modification des champs de la passerelle.

## Vérifications et tests

Depuis la racine du dépôt, avec un environnement Python contenant les dépendances :

```powershell
cd example
python -B -m unittest discover -s tests -v
```

Les tests utilisent des services simulés locaux, sans Unity ni clés API.

Effectuez la validation manuelle en deux étapes :

1. **Intégration locale :** lancez un simulateur et connectez `demo.py`. Vérifiez `state=online`, les sous-titres et la lecture audio, puis testez les actions dans le panneau F1.
2. **Voix et services cloud :** arrêtez la démo, configurez les clés et lancez `agent.py`. Après l’accueil, parlez et vérifiez le texte ASR, la réponse LLM et la lecture TTS. Demandez une action, puis déconnectez et reconnectez le client.

Notez le modèle, les ressources vocales, les résultats et les délais par étape. Distinguez l’enregistrement et la détection du silence du traitement cloud ; une mesure isolée ne garantit pas les performances.

## Modifications et documentation

- Accompagner les changements de comportement de tests locaux ; vérifier les liens relatifs et les trois langues pour la documentation.
- Le chinois n’a pas de suffixe ; l’anglais utilise `.en.md`, le français `.fr.md`. Conserver les identifiants techniques.
- Les tableaux, liens et points d’entrée documentés concernent uniquement les fichiers gérés dans Git ; vérifier les règles pour les nouveaux fichiers. Suivre les [règles du dépôt](../.gitignore) et le [modèle de configuration](../example/.env.example). Ne jamais versionner de clés personnelles.
- `exe/x2模拟器.exe` est un livrable volontaire ; ne pas ignorer tous les `*.exe`. Noter taille, SHA-256 et vérification de lancement lors d’un remplacement.
- Ne pas imposer de chemins personnels, outils temporaires ou scripts absents du dépôt aux utilisateurs.
- Une PR décrit le problème, le comportement final et les vérifications. Préciser les essais Unity ou cloud non effectués.

## Unity et distribution

Le [projet Unity](../unity-agent-playground/) utilise `2022.3.62f3c1`. Le lanceur, le HUD et les caméras sont dans `Assets/X02Competition/Bootstrap/`, les vérifications dans `Assets/X02Competition/Tests/Editor/`. Compilez les changements via Build Settings selon le [guide Unity](unity.fr.md), puis conservez toute la sortie.

Les livrables Git sont [x2模拟器.exe](../exe/x2模拟器.exe) et son [empreinte](../exe/x2模拟器.sha256). Lors de leur remplacement, actualisez l’empreinte et vérifiez le démarrage, les connexions Agent et le changement de vue. Un Build Unity normal ne met pas automatiquement à jour ces fichiers.
