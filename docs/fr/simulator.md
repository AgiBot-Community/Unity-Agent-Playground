# Simulateur X2 · exécutable portable

[中文](../zh-CN/simulator.md) | [English](../en/simulator.md) | **Français**

Le simulateur Windows est distribué sous forme d’un EXE unique. Il affiche le robot et écoute sur `127.0.0.1:9002`. **F1** ouvre les boutons de test des actions. Les conversations vocales nécessitent [l’agent Python](../../example/x2_agent/docs/fr/README.md), lancé séparément.

Au premier lancement, les ressources Unity sont extraites dans `%LOCALAPPDATA%\UnityPortable` ; les lancements suivants réutilisent le cache. Le simulateur fonctionne sans installation de Unity ou Python. Configurez séparément les dépendances et identifiants de l’agent.

## Exécution

| Élément | Valeur |
|---|---|
| Système | Windows 10/11 x64, .NET Framework 4.x du système |
| Équipement vocal | Microphone et haut-parleurs ; les boutons d’action ne nécessitent pas de microphone |
| Adresse locale | `ws://127.0.0.1:9002/api/V1/open-portal/app/wss/agent-sdk` |
| Authentification | Les clients envoient une signature HMAC ; son contrôle dépend du build Unity |
| Sessions | Huit par défaut ; priorité maximale fixe pour la console, autres priorités réglables |

Gardez la fenêtre ouverte pendant les essais vocaux. Si sa réduction perturbe l’audio ou le réseau, restaurez-la avant de poursuivre le diagnostic. Les boutons de débogage déclenchent des actions sans agent. La chaîne vocale actuelle est semi-duplex.

## Démarrer depuis l’EXE

1. Téléchargez l’EXE portable et son empreinte de la même version depuis [GitHub Releases](https://github.com/AgiBot-Community/Unity-Agent-Playground/releases). Les binaires ne sont pas inclus dans le dépôt source. Unity et Python ne sont pas nécessaires pour le simulateur seul.
2. Pour le lanceur de la console, placez l’EXE dans `exe/x2模拟器.exe`. À la racine du dépôt, `Get-FileHash -LiteralPath 'exe/x2模拟器.exe' -Algorithm SHA256` permet de le comparer avec l’empreinte téléchargée. L’EXE peut aussi être lancé depuis un autre dossier.
3. Ouvrez l’EXE, attendez la fenêtre du robot et appuyez sur **F1** pour consulter l’état ou tester les actions.
4. Pour connecter l’agent d’exemple, exécutez depuis la racine du dépôt :

```powershell
python -m pip install -r example/x2_agent/requirements.txt
python example/x2_agent/demo.py
```

`state=online` confirme la connexion. La démo utilise du texte fixe et les fichiers audio fournis, sans service cloud. Pour les conversations réelles, configurez les clés à partir de `.env.example` selon le [guide Agent](../../example/x2_agent/docs/fr/README.md), arrêtez la démo puis lancez `python example/x2_agent/agent.py`. Fermez la fenêtre pour arrêter le simulateur ; Ctrl+C arrête l’agent.

## Points de vue

Appuyez sur **C** pour changer de vue, ou utilisez les boutons du panneau :

| Touche | Vue | Comportement |
|---|---|---|
| F2 | Vue générale | Garde le départ et la position actuelle dans le cadre, avec recul automatique |
| F3 | Suivi de face (par défaut) | Laisse une petite liberté de mouvement avant de suivre ; conserve une orientation fixe dans le monde |
| F4 | Suivi latéral | Montre la marche, le déplacement et les rotations de côté |
| F5 | Orbite libre | Maintenez le bouton droit dans la scène pour tourner ; utilisez la molette pour régler la distance |

Chaque vue cadre le corps entier et réserve de la place au panneau. Le zoom orbital ne peut pas couper le robot. Les vues de suivi conservent les repères du sol et une petite zone de mouvement libre. F1 replie le panneau détaillé pour libérer de la place dans une petite fenêtre.

## Modification et compilation

Consultez le [guide Unity](unity.md) pour modifier scènes, caméras, actions ou passerelle. Le mode Play utilise les mêmes raccourcis et commandes Agent. Fermez l’EXE portable avant de passer en mode Play pour éviter un conflit de port.

Unity Build produit un dossier complet contenant l’application et ses ressources. L’[outil d’empaquetage](../../tools/unity-packager/README.md) transforme ce dossier en un EXE unique.
