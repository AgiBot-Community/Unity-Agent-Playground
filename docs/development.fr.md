# Développement et maintenance

[中文](development.md) | [English](development.en.md) | **Français**

## Environnement et lancement

Créez un environnement Python 3.10+ dans `example/` et exécutez `python -m pip install -e .`. Lancez `python -m x2_agent` ou `python -m x2_agent.demo` ; aucun EXE client n’est généré. Voir le [guide de l’agent](../example/docs/README.fr.md).

Déclarez les dépendances uniquement dans `pyproject.toml`. Priorité de configuration : arguments CLI, environnement existant, `.env`, puis valeurs par défaut. Les imports ne chargent pas les clés. Utilisez `--env-file` pour choisir un fichier.

## Responsabilités des modules

`agent.py` gère les sessions et la concurrence. `asr.py`, `llm.py`, `tts.py` et `sentence_tts.py` gèrent les transports correspondants. Les messages Unity appartiennent à `gateway.py`, les trames binaires Doubao à `speech_protocol.py`. Les outils WAV et la configuration se trouvent dans `audio.py` et `config.py`.

Préservez l’annulation, la propagation des erreurs et la fermeture des connexions. N’introduisez pas d’appels réseau synchrones bloquant la chaîne vocale. Vérifiez les deux clients lors d’une modification des champs de la passerelle.

## Vérifications et tests

Depuis la racine du dépôt, avec un environnement Python contenant les dépendances :

```powershell
python -B scripts/check_docs.py
cd example
python -B -m unittest discover -s tests -v
```

Les tests utilisent des services simulés locaux, sans Unity ni clés API. La CI est configurée sous Windows avec Python 3.10 et 3.12. La présence du workflow ne prouve pas qu’une exécution distante a déjà réussi.

Validation manuelle complète : lancer Unity → connecter un seul agent → attendre l’accueil → parler → vérifier texte ASR, texte LLM et audio → déclencher une action → déconnecter et reconnecter. Noter modèle, ressources vocales et délais par étape, sans transformer une mesure ponctuelle en garantie.

## Modifications et documentation

- Accompagner les changements de comportement de tests locaux ; vérifier les liens relatifs et les trois langues pour la documentation.
- Le chinois n’a pas de suffixe ; l’anglais utilise `.en.md`, le français `.fr.md`. Conserver les identifiants techniques.
- Exclure `.env`, enregistrements, journaux, environnements, caches et `.diagnostics/`. Ne pas publier de journaux contenant des clés.
- `exe/x2模拟器.exe` est un livrable volontaire ; ne pas ignorer tous les `*.exe`. Noter taille, SHA-256 et vérification de lancement lors d’un remplacement.
- `build/`, `dist/` et `*.egg-info/` sont générés. Supprimer uniquement les dossiers de génération identifiés ; conserver configuration et sources.
- Une PR décrit le problème, le comportement final et les vérifications. Préciser les essais Unity ou cloud non effectués.

## Unity et distribution

Le projet Unity complet est absent. Le dossier `../x2-simulator/` du mainteneur contient les éléments d’empaquetage, indisponibles dans un clone normal. Le lanceur utilise un cache disque : ne pas le présenter comme une exécution uniquement en mémoire. Voir le [guide du simulateur](simulator.fr.md).
