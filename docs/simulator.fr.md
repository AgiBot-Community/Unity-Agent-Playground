# Simulateur X2 · exécutable portable

[中文](simulator.md) | [English](simulator.en.md) | **Français**

Ouvrez [x2模拟器.exe](../exe/x2模拟器.exe) pour lancer Unity et la passerelle du robot. **F1** affiche ou masque le panneau de débogage. Lancez [l’agent Python](../example/docs/README.fr.md) séparément ; c’est lui qui envoie l’accueil.

Le fichier fait **58 287 104 octets (58,29 Mo)**. Cet EXE suffit pour distribuer le simulateur, sans dossier Data, Python, clés ou dépôt Playground. Les conversations réelles nécessitent toujours l’agent séparé. Voir l’[empreinte SHA-256](../exe/x2模拟器.sha256).

## Exécution

| Élément | Valeur |
|---|---|
| Système | Windows 10/11 x64, .NET Framework 4.x du système |
| Audio | Microphone et haut-parleurs |
| Adresse locale | `ws://127.0.0.1:9002/api/V1/open-portal/app/wss/agent-sdk` |
| Authentification | Les clients envoient une signature HMAC ; son contrôle dépend du build Unity |
| Sessions | Un agent à la fois |

Gardez la fenêtre ouverte pendant les essais vocaux. Si sa réduction perturbe l’audio ou le réseau, restaurez-la avant de poursuivre le diagnostic. Les boutons de débogage déclenchent des actions sans agent. La chaîne vocale actuelle est semi-duplex.

## Démarrer depuis l’EXE

1. Téléchargez l’[EXE](../exe/x2模拟器.exe) et son [empreinte](../exe/x2模拟器.sha256). Unity et Python ne sont pas nécessaires pour le simulateur seul.
2. À la racine du dépôt, `Get-FileHash -LiteralPath 'exe/x2模拟器.exe' -Algorithm SHA256` permet de comparer le fichier téléchargé avec l’empreinte fournie.
3. Ouvrez l’EXE, attendez la fenêtre du robot et appuyez sur **F1** pour consulter l’état ou tester les actions.
4. Pour connecter l’agent d’exemple, exécutez depuis la racine du dépôt :

```powershell
python -m pip install -r example/requirements.txt
python example/demo.py
```

`state=online` confirme la connexion. La démo utilise du texte fixe et les fichiers audio fournis, sans service cloud. Pour les conversations réelles, configurez les clés à partir de `.env.example` selon le [guide Agent](../example/docs/README.fr.md), arrêtez la démo puis lancez `python example/agent.py`. Fermez la fenêtre pour arrêter le simulateur ; Ctrl+C arrête l’agent.

## Points de vue

Appuyez sur **C** pour changer de vue, ou utilisez les boutons du panneau :

| Touche | Vue | Comportement |
|---|---|---|
| F2 | Vue générale | Garde le départ et la position actuelle dans le cadre, avec recul automatique |
| F3 | Suivi de face (par défaut) | Laisse une petite liberté de mouvement avant de suivre ; conserve une orientation fixe dans le monde |
| F4 | Suivi latéral | Montre la marche, le déplacement et les rotations de côté |
| F5 | Orbite libre | Maintenez le bouton droit dans la scène pour tourner ; utilisez la molette pour régler la distance |

Chaque vue cadre le corps entier et réserve de la place au panneau. Le zoom orbital ne peut pas couper le robot. Les vues de suivi conservent les repères du sol et une petite zone de mouvement libre. F1 replie le panneau détaillé pour libérer de la place dans une petite fenêtre.

## Démarrer depuis le projet Unity

Utilisez le [projet Unity](../unity-agent-playground/) du dépôt pour modifier les scènes, caméras, actions ou la passerelle :

1. Installez Unity Hub et **2022.3.62f3c1**, puis ajoutez `unity-agent-playground/` avec **Add project from disk**.
2. Après l’import, ouvrez `Assets/X02Competition/Scenes/scene.unity`, cliquez sur **Play** et activez la fenêtre Game.
3. Les raccourcis et commandes Agent ci-dessus restent les mêmes. Fermez l’EXE avant Play pour éviter un conflit sur le port 9002.
4. Suivez le [guide de compilation Unity](unity.fr.md) avec **File → Build Settings** pour produire une application Windows ; conservez tout le dossier obtenu.

L’EXE unique dans Git est un livrable prêt à l’emploi. Un Build Unity normal produit un dossier avec l’application et ses ressources, sans réempaqueter ni remplacer automatiquement cet EXE. Ce guide prend comme points de départ le projet et les livrables du dépôt.
