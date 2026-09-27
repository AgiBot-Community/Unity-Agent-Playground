# Console graphique Python

[中文](console.md) | [English](console.en.md) | **Français**

Depuis la racine du dépôt :

```powershell
python -m pip install -r example/x2_console/requirements.txt
python example/x2_console/main.py
```

Le code et les tests de la console sont dans `example/x2_console/`. Depuis ce dossier,
utilisez `python main.py` ou `python -m x2_console`. Ce client est dédié à la gestion et à la surveillance,
avec WebSocket comme seule dépendance tierce. Il ne lit pas `.env` et ne contient aucun moteur vocal.
Depuis la racine, lancez les tests avec
`python -B -m unittest discover -s example/x2_console/tests -v`.

L’interface native utilise Tkinter, sans Qt ni compilation frontend. Si votre distribution
Python n’inclut pas Tk, installez son support Tk. Lancez le simulateur avec le bouton dédié ou
démarrez Unity manuellement, puis connectez-vous à `127.0.0.1:9002`. Les paramètres avancés
contiennent le chemin et les identifiants de signature, conservés uniquement en mémoire.

Après prise de contrôle explicite, l’onglet robot commande les gestes, la marche, les rotations,
expressions et l’interruption. L’onglet sessions affiche les clients et l’attribution du contrôle
et de la voix. Sélectionnez un client non-console, saisissez un entier **0–999**, puis appliquez
et attendez la valeur confirmée par le serveur. La console conserve **1000**, non modifiable.
Huit connexions sont permises par défaut. Les priorités sont réinitialisées à la reconnexion ;
à égalité, le plus ancien client est prioritaire.

La connexion démarre en surveillance, sans prendre les actions ni le microphone de l’Agent.
La console conserve la priorité 1000, les journaux et la gestion des priorités. Activez la prise de
contrôle pour une opération manuelle, puis cédez-la à l’Agent. Le bouton d’arrêt de la console
gestionnaire reprend le contrôle avant d’interrompre. Redémarrez le simulateur mis à jour si la case
est indisponible. Si une commande vocale ne produit aucun geste, vérifiez le détenteur du contrôle et `4091`.

Les conversations vocales et clés cloud appartiennent uniquement à `example/x2_agent`.
La console ne contient ni ASR, LLM, TTS, accueil ni enregistrements, et refuse la réception audio.
Les clients moins prioritaires ne peuvent pas exécuter d’actions lorsque la console prend explicitement le contrôle.

Les journaux proposent filtres de niveau/source, recherche dans le texte et les piles, pause de
l’affichage, effacement et export JSONL du filtre courant. Les 1200 dernières entrées sont conservées.
La pause n’interrompt pas la réception. Le panneau de surveillance affiche alimentation, réseau
et dernière action rapportés par la passerelle. `Ctrl+L` ouvre la recherche ; `Ctrl+.` arrête voix
et mouvement. Fermer la fenêtre déconnecte et arrête le thread réseau.

Les couleurs s’appuient sur **Catppuccin Mocha** (`catppuccin/palette`, MIT) : surfaces sombres,
Teal pour l’action principale, Yellow pour les avertissements, Red pour les erreurs.
Les références et couleurs sont dans [`theme.py`](../example/x2_console/x2_console/theme.py).
Tk applique ces couleurs aux contrôles natifs, sans charger de CSS ni de polices distantes.
Le focus clavier et les écrans haute densité sont pris en charge.

Consultez le [protocole](interface.fr.md) pour l’arbitrage et le
[guide de développement](development.fr.md) pour les tests Python locaux et Unity réels.
