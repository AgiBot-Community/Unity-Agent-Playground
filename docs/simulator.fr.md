# Simulateur X2 · exécutable portable

[中文](simulator.md) | [English](simulator.en.md) | **Français**

Ouvrez [x2模拟器.exe](../exe/x2模拟器.exe) pour lancer Unity et la passerelle du robot. **F1** affiche ou masque le panneau de débogage. Lancez [l’agent Python](../example/docs/README.fr.md) séparément ; c’est lui qui envoie l’accueil.

Le fichier fait **66 880 000 octets (66,88 Mo)**. Cet EXE suffit pour distribuer le simulateur, sans dossier Data, Python, clés ou dépôt Playground. Les conversations réelles nécessitent toujours l’agent séparé.

## Exécution

| Élément | Valeur |
|---|---|
| Système | Windows 10/11 x64, .NET Framework 4.x du système |
| Audio | Microphone et haut-parleurs |
| Adresse locale | `ws://127.0.0.1:9002/api/V1/open-portal/app/wss/agent-sdk` |
| Authentification | Les clients envoient une signature HMAC ; son contrôle dépend du build Unity |
| Sessions | Un agent à la fois |

Gardez la fenêtre ouverte pendant les essais vocaux. Si sa réduction perturbe l’audio ou le réseau, restaurez-la avant de poursuivre le diagnostic. Les boutons de débogage déclenchent des actions sans agent. La chaîne vocale actuelle est semi-duplex.

## Premier lancement et cache

Aucune installation Unity, extraction manuelle ou configuration guidée n’est requise. Le lanceur extrait silencieusement les ressources dans `%LOCALAPPDATA%\x2sim\<version>`, puis les réutilise. Il s’agit d’une distribution en un seul fichier, pas d’une exécution entièrement en mémoire.

Le cache occupe environ 328 Mio ; prévoyez au moins 500 Mo pour la préparation initiale. Un essai local a donné environ 12,6 secondes avant l’ouverture de la fenêtre, puis 1,8 seconde aux lancements suivants. Après avoir fermé Unity, vous pouvez supprimer le cache : il sera recréé. Unity conserve son fonctionnement habituel pour les journaux et préférences.

Les 207 fichiers du build original sont conservés, y compris la DLL corrigeant le panneau de débogage.

## Empaquetage

Le dossier voisin `../x2-simulator/` du mainteneur contient le code du lanceur, 7-Zip, `unity-payload.7z`, les empreintes et le script. Il ne fait pas partie de ce dépôt. Depuis ce dossier :

```powershell
.\build-portable.ps1
```

Sortie : `dist/x2模拟器.exe`. Les ressources figées suffisent ; le projet Unity original n’est pas nécessaire pour cette étape. Pour les remplacer, utilisez `-Source 'chemin-du-build-Unity-complet'`. Le script vérifie l’archive et impose une taille inférieure à 100 000 000 octets.

L’empaquetage ne modifie ni l’adresse d’écoute ni l’authentification Unity. Pour les modifier, il faut obtenir un build Unity mis à jour puis l’empaqueter. Les sources sont dans [unity-project/](../unity-project/), avec l’éditeur `2022.3.62f3c1`. Cet EXE portable n’a pas été reconstruit à partir des sources actuelles ; les actions disponibles peuvent différer.
