# Agent vocal d’exemple

[中文](../README.md) | [English](README.en.md) | **Français**

Utilisez `python -m x2_agent` pour les conversations Doubao et les commandes du robot. Commencez par `python -m x2_agent.demo` pour vérifier la passerelle sans clé cloud : cette démo renvoie du texte fixe et lit les enregistrements fournis, sans reconnaissance ni synthèse en temps réel. Un enregistrement absent ou invalide est remplacé par un signal sinusoïdal.

## Organisation

| Chemin | Responsabilité |
|---|---|
| `pyproject.toml` | Métadonnées, version Python et dépendances |
| `x2_agent/agent.py` | Sessions, historique, commandes et arguments CLI |
| `x2_agent/asr.py` | Envoi audio pendant l’enregistrement, reconnaissance et annulation |
| `x2_agent/llm.py` | Réponses LLM en flux et réutilisation des connexions |
| `x2_agent/tts.py` | Synthèse vocale bidirectionnelle en flux |
| `x2_agent/sentence_tts.py` | Mode de repli par phrase |
| `x2_agent/speech_protocol.py` | Trames binaires Doubao V3 |
| `x2_agent/gateway.py` | Événements Unity, authentification HMAC et enveloppes |
| `x2_agent/demo.py` | Démo hors ligne de la passerelle |
| `x2_agent/config.py`, `audio.py` | Configuration, fichiers WAV et troncature des journaux |
| `tests/` | Tests locaux ASR, chaîne vocale et configuration |
| `.env.example` | Modèle public à copier dans un fichier `.env` privé |

## Installation et lancement

Python 3.10+ est requis. Pour les conversations réelles, activez Volcengine Speech (ASR/TTS) et Ark (LLM). Dans PowerShell, depuis le dossier `example/` du dépôt :

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
```

Lancez d’abord [le simulateur](../../docs/simulator.fr.md), puis :

```powershell
.\.venv\Scripts\python.exe -m x2_agent.demo
# Arrêter la démo avec Ctrl+C avant de lancer un autre client
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Renseigner les deux clés API dans .env
.\.venv\Scripts\python.exe -m x2_agent
```

Les clients démarrent comme modules Python, sans point d’entrée EXE. Sous Linux/macOS, utilisez `.venv/bin/python` ; le simulateur Unity fourni nécessite Windows. Une connexion distante demande une passerelle accessible qui écoute sur le réseau, en plus de `--host <adresse> --port 9002`.

Attendez la fin de l’accueil avant de parler. Le fonctionnement est semi-duplex. Le prompt et la voix par défaut ciblent le chinois ; ces traductions ne modifient pas les langues prises en charge par les services vocaux ou l’interface Unity.

## Commandes du robot

- « 挥挥手 », « 张开双臂 » : saluer et ouvrir les bras (`wave_hands`, `open_arms`).
- « 往前走一米 », « 向左转 », « 停 » : avancer, tourner et s’arrêter.
- Les demandes d’expressions heureuses, tristes ou surprises changent le visage. Le protocole prévoit aussi une expression neutre.
- **F1** ouvre le panneau Unity, dont les boutons fonctionnent sans clé cloud.

La liste correspond aux sources Unity actuelles. L’EXE portable n’a pas été reconstruit à partir de ces sources ; les anciennes versions peuvent ne pas gérer l’ouverture des bras.

Les options `--reply` et `--greeting` changent les sous-titres, pas les enregistrements. Les fichiers `x2_agent/greeting.wav` et `x2_agent/tts.wav` sont envoyés par tranches de 200 ms.

## Configuration

La configuration se charge au lancement, jamais à l’import. Priorité : arguments CLI > variables d’environnement existantes > `.env` > valeurs par défaut. Une installation locale en mode éditable lit le `.env` à la racine du projet `example/`. `--env-file D:\config\x2.env` désigne un fichier explicite ; son absence déclenche une erreur.

| Variable | Requise pour Doubao | Valeur par défaut / rôle |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | Oui | Clé Speech commune à ASR et TTS |
| `ARK_API_KEY` | Oui | Clé LLM Ark |
| `DOUBAO_LLM_MODEL` | Non | `doubao-seed-2-0-mini-260428` |
| `DOUBAO_TTS_SPEAKER` | Non | `zh_female_wanqudashu_moon_bigtts`, compatible avec `seed-tts-1.0` |
| `DOUBAO_ASR_RESOURCE_ID` | Non | `volc.bigasr.sauc.duration` |

Ne versionnez pas `.env`, les enregistrements, les environnements virtuels ou les diagnostics locaux.

## Paramètres utiles

Depuis le dossier `example/` du dépôt, avec le Python de votre environnement virtuel :

```powershell
.\.venv\Scripts\python.exe -m x2_agent --help
.\.venv\Scripts\python.exe -m x2_agent --system-prompt "Tu es un guide robot qui répond brièvement."
.\.venv\Scripts\python.exe -m x2_agent --save-audio reply.wav --save-input input.wav
.\.venv\Scripts\python.exe -m x2_agent --asr-after-commit
.\.venv\Scripts\python.exe -m x2_agent --llm-model doubao-seed-2-1-turbo-260628
.\.venv\Scripts\python.exe -m x2_agent --tts-mode sentence
.\.venv\Scripts\python.exe -m x2_agent --greeting "你好"
.\.venv\Scripts\python.exe -m x2_agent --greeting=
.\.venv\Scripts\python.exe -m x2_agent.demo --reply "Bonjour"
.\.venv\Scripts\python.exe -m x2_agent.demo --skill gesture/wave_hands
.\.venv\Scripts\python.exe -m x2_agent.demo --interrupt chat
```

Par défaut, l’audio ASR est envoyé pendant l’enregistrement, les connexions LLM sont réutilisées et le texte alimente directement une session TTS bidirectionnelle. `--asr-after-commit` retarde l’envoi à des fins de comparaison ; `--tts-mode sentence` active le mode par phrase. Mini privilégie la rapidité ; son raisonnement peut différer de Turbo.

## Dépannage

| Symptôme | Vérification |
|---|---|
| Connexion refusée | Lancer Unity ; vérifier hôte, port et adresse d’écoute |
| HTTP 401 | Identifiants, signature et horodatage si l’authentification stricte est activée |
| HTTP 503 | Un autre agent occupe l’unique session |
| Aucune transcription | Microphone coupé, volume d’entrée et durée de l’enregistrement |
| Attente après la parole | Distinguer start→commit, ASR après commit, premier jeton LLM et premier audio TTS ; le silence détecté n’est pas du calcul ASR |
| LLM 404 | Identifiant complet du modèle, avec date, ou point d’accès `ep-...` |
| TTS 403 | Autorisation et compatibilité voix/ressource ; une voix 2.0 ne correspond pas à `seed-tts-1.0` |
| Réponse inaudible | Périphérique et volume de sortie Windows ; journaux TTS et fichier `--save-audio` |

## Tests et développement

```powershell
.\.venv\Scripts\python.exe -B -m unittest discover -s tests -v
```

Les tests utilisent des services simulés locaux, sans Unity ni API cloud. Les dépendances sont définies dans `pyproject.toml` ; réinstallez avec `pip install -e .` après modification. Lancez l’agent localement avec `python -m x2_agent` ou `python -m x2_agent.demo`. Les métadonnées d’installation générées et les caches sont ignorés.

Voir le [protocole](../../docs/interface.fr.md) et le [guide de développement](../../docs/development.fr.md).
