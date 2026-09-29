# Console graphique Python

[中文](../zh-CN/console.md) | [English](../en/console.md) | **Français**

La console permet de commander le robot, gérer les priorités des sessions et consulter les journaux. Les conversations vocales utilisent l’[agent](../../example/x2_agent/docs/fr/README.md) séparé.

## Installation et lancement

Depuis la racine du dépôt :

```powershell
python -m pip install -r example/x2_console/requirements.txt
python example/x2_console/main.py
```

Le code et les tests sont dans `example/x2_console/`. Depuis ce dossier, utilisez `python main.py` ou `python -m x2_console`. Le dossier fonctionne indépendamment ; sa dépendance tierce est `websockets`. L’interface utilise Tkinter. Si votre distribution Python n’inclut pas Tk, installez son support Tk.

Le bouton de lancement utilise `exe/x2模拟器.exe` dans le dépôt. Téléchargez et vérifiez l’EXE selon le [guide du simulateur](simulator.md), puis placez-le à cet emplacement. Si vous copiez uniquement le dossier de la console, démarrez le simulateur manuellement.

## Connexion et commandes

Démarrez le simulateur, puis connectez-vous à `127.0.0.1:9002`. Les paramètres avancés contiennent le chemin et les identifiants de signature, conservés uniquement en mémoire. La console ne lit pas `.env`.

Après prise de contrôle explicite, l’onglet robot commande les gestes, la marche, les rotations,
expressions et l’interruption. L’onglet sessions affiche les clients et l’attribution du contrôle
et de la voix. Sélectionnez un client non-console, saisissez un entier **0–999**, puis appliquez
et attendez la valeur confirmée par le serveur. La console conserve **1000**, non modifiable.
Huit connexions sont permises par défaut. Les priorités sont réinitialisées à la reconnexion ;
à égalité, le plus ancien client est prioritaire.

La connexion démarre en surveillance, sans prendre les actions ni le microphone de l’Agent.
La console conserve la priorité 1000, les journaux et la gestion des priorités. Activez la prise de
contrôle pour une opération manuelle, puis cédez-la à l’Agent. Le bouton d’arrêt de la console
gestionnaire reprend le contrôle avant d’interrompre. Si la case n’est pas prise en charge, mettez à jour et redémarrez le simulateur selon le message affiché.
Si une commande vocale ne produit aucune action, vérifiez le détenteur du contrôle et `4091` (droits insuffisants).

Les conversations vocales et clés cloud appartiennent uniquement à `example/x2_agent`.
La console désactive la réception du microphone lors de la connexion.
Les clients moins prioritaires ne peuvent pas exécuter d’actions lorsque la console prend explicitement le contrôle.

## Journaux et raccourcis

Les journaux proposent filtres de niveau/source, recherche dans le texte et les piles, pause de
l’affichage, effacement et export JSONL du filtre courant. Les 1200 dernières entrées sont conservées.
La pause n’interrompt pas la réception. Le panneau de surveillance affiche alimentation, réseau
et dernière action rapportés par la passerelle. `Ctrl+L` ouvre la recherche ; `Ctrl+.` arrête voix
et mouvement. Fermer la fenêtre déconnecte et arrête le thread réseau.

## Tests et thème

Depuis la racine du dépôt :

```powershell
python -B -m unittest discover -s example/x2_console/tests -v
```

Le thème utilise **Catppuccin Mocha** (MIT). Les couleurs et leur provenance figurent dans [`theme.py`](../../example/x2_console/x2_console/theme.py).

Consultez le [protocole](interface.md) pour l’arbitrage et le
[guide de développement](development.md) pour les tests Python locaux et Unity réels.
