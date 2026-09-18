# Unity Agent Playground · X2

[中文](../README.md) | [English](README.en.md) | **Français**

Dialoguez avec un robot X2 dans Unity à l’aide d’un agent vocal Python et déclenchez des gestes, des déplacements et des expressions. Ce dépôt contient un simulateur Windows portable, un exemple Python installable et la documentation du protocole de la passerelle.

## Contenu du dépôt

| Chemin | Rôle |
|---|---|
| [exe/](simulator.fr.md) | `x2模拟器.exe`, distribution Unity en un seul fichier de 66,88 Mo |
| [example/](../example/docs/README.fr.md) | Paquet Python `x2_agent`, modèle de configuration et tests locaux |
| [docs/](index.fr.md) | Index, protocole et guide de développement en trois langues |
| `scripts/check_docs.py` | Vérification de la couverture linguistique et des liens locaux |
| `.github/workflows/ci.yml` | Tests Windows et vérification documentaire |

Unity joue le rôle de serveur WebSocket. L’agent Python reçoit le son du microphone, appelle les services ASR, LLM et TTS, puis renvoie texte, audio et commandes au robot. Le simulateur et l’agent se lancent séparément.

Le projet Unity complet n’est pas inclus. Les outils d’empaquetage et les ressources figées sont conservés par le mainteneur dans le dossier voisin `../x2-simulator/`. Ce dossier n’est pas distribué avec le dépôt et n’est pas nécessaire pour utiliser le simulateur.

## Démarrage rapide

Prérequis : Windows 10/11 x64, Python 3.10+, un microphone et des haut-parleurs. Les conversations réelles nécessitent des clés API Volcengine Speech et Ark. La démo hors ligne n’utilise aucun service cloud.

1. Ouvrez [exe/x2模拟器.exe](../exe/x2模拟器.exe). **F1** affiche le panneau de débogage.
2. Ouvrez PowerShell à la racine du dépôt :

```powershell
cd example
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
.\.venv\Scripts\python.exe -m x2_agent.demo
```

La démo renvoie du texte fixe et un signal sonore sinusoïdal pour vérifier la passerelle et la chaîne audio. Elle ne reconnaît pas la parole et ne synthétise pas de réponse parlée.

3. Arrêtez la démo avec Ctrl+C, configurez les clés puis lancez Doubao :

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Renseigner DOUBAO_SPEECH_API_KEY et ARK_API_KEY dans .env
.\.venv\Scripts\python.exe -m x2_agent
```

Attendez la fin de l’accueil avant de parler. Exemples en chinois : « 你好 » (bonjour), « 挥挥手 » (faire un signe de la main), « 往前走一米 » (avancer d’un mètre). Un seul agent peut se connecter. Le fonctionnement est semi-duplex : la détection vocale s’arrête pendant la lecture. Parler n’interrompt donc pas la réponse ; une commande explicite du protocole permet de l’interrompre.

Les trois langues de documentation ne signifient pas que les services vocaux, la voix chinoise par défaut ou l’interface Unity sont adaptés à ces trois langues.

## Distribution en un seul fichier

Seul `exe/x2模拟器.exe` est nécessaire pour le simulateur. Il ne contient ni Python ni clés API. Au premier lancement, les ressources sont extraites silencieusement dans `%LOCALAPPDATA%\x2sim\`, puis réutilisées. Aucune extraction manuelle ni configuration guidée n’est requise. Prévoyez au moins 500 Mo d’espace libre.

Une mesure locale a donné environ 12,6 secondes avant l’ouverture de la fenêtre au premier lancement, puis 1,8 seconde. Les résultats dépendent de l’ordinateur. Le délai vocal dépend aussi du silence détecté, du réseau, du modèle et des ressources vocales ; aucun délai fixe n’est garanti.

## Développement et validation

```powershell
# Depuis la racine du dépôt
python -B scripts/check_docs.py
cd example
# Tests locaux, sans appel cloud
.\.venv\Scripts\python.exe -B -m unittest discover -s tests -v
```

Consultez le [guide de développement](development.fr.md). Les clés, enregistrements, caches et diagnostics locaux sont exclus de Git. L’EXE est volontairement conservé comme livrable.

## Documentation

| Besoin | Guide |
|---|---|
| Modèles, voix, accueil, paramètres et dépannage | [Agent d’exemple](../example/docs/README.fr.md) |
| Démarrage, cache et empaquetage | [Simulateur](simulator.fr.md) |
| Agents personnalisés, authentification et échanges | [Protocole](interface.fr.md) |
| Toutes les langues disponibles | [Index documentaire](index.fr.md) |
