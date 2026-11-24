# BT Battery Tray – Conception et plan de travail

## Objectif
Afficher en permanence le niveau de batterie du casque Bluetooth **P9** dans la barre des tâches
(zone de notification, à côté de l'horloge), sans augmenter la consommation du PC ni du casque.

## Constat technique (vérifié le 24/11/2026)
- Windows connaît déjà le niveau de batterie : le casque l'envoie lui-même via le profil
  Hands-Free (HFP). Aucune communication Bluetooth supplémentaire n'est nécessaire.
- La valeur est exposée comme propriété de périphérique PnP :
  - Clé : `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2` (DEVPKEY_Bluetooth_Battery)
  - Périphérique : `P9 Hands-Free AG`
    `BTHENUM\{0000111E-0000-1000-8000-00805F9B34FB}_VID&00010642_PID&0220\7&35A2E243&0&4142584A6007_C00000000`
  - Valeur lue : **90 %** (identique aux Paramètres Windows)
- Bonus : la souris `Logi M196` (BLE) expose la même clé (84 %).

Commande de test PowerShell :
```powershell
Get-PnpDevice -PresentOnly | % { $b = Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2' -EA SilentlyContinue; if ($b.Data -ne $null) { "$($_.FriendlyName) : $($b.Data)%" } }
```

## Choix de conception
| Sujet | Choix | Raison |
|---|---|---|
| Emplacement | Icône dans la zone de notification (system tray) | Le panneau « Paramètres rapides » de Windows 11 n'accepte pas d'extensions tierces. Le tray est visible en permanence. |
| Technologie | C# / .NET 8, WinForms (`NotifyIcon`) | Léger (~15-25 Mo RAM), pas de dépendance externe, runtime .NET 8 déjà présent. |
| Lecture | API Windows `CM_Get_DevNode_Property` (cfgmgr32) | Lecture d'une valeur en cache du système : pas de radio, coût CPU négligeable. |
| Rafraîchissement | Événementiel via `DeviceWatcher` WinRT (batterie + connexion des nœuds Bluetooth) + filet de sécurité toutes les 5 min | CPU au repos ≈ 0 %, aucune requête au casque. |
| Connexion | Propriété non documentée `{83DA6326-97A6-4088-9453-A1923F573B29} 15` sur le nœud `BTHENUM\DEV_…` / `BTHLE\DEV_…` (TRUE = connecté) | Les nœuds restent « présents » quand le casque est éteint ; la batterie garde sa dernière valeur. |
| Affichage | **Une icône par appareil** (casque, souris…). Le chiffre (ex. « 90 ») dessiné dans l'icône, couleur selon le niveau (vert > 50, orange > 20, rouge ≤ 20), info-bulle « P9 : 90 % » | Lisible d'un coup d'œil, tout est visible sans clic. |
| Appareil déconnecté | Son icône disparaît. Si aucun appareil : une seule icône grise « -- » (pour garder l'accès au menu Quitter) | |
| Démarrage | Raccourci dans le dossier Démarrage (`shell:startup`) | Simple, sans droits admin. |

## Prérequis à installer (à faire par toi)
Seul le **SDK .NET 8** manque (le runtime est déjà là) :
```powershell
winget install --id Microsoft.DotNet.SDK.8 -e
```
Puis fermer/rouvrir le terminal et vérifier : `dotnet --list-sdks`

## Plan de travail par étapes

### Étape 0 – Faisabilité ✅ (session 1)
- [x] Identifier où Windows stocke la batterie du casque
- [x] Vérifier la valeur (90 %)
- [x] Rédiger ce plan

### Étape 1 – Prototype console ✅ (session 2)
- [x] Installer le SDK .NET 8
- [x] Créer le projet `src/BtBatteryTray` (net8.0-windows)
- [x] Lister les périphériques BT exposant une batterie et afficher le niveau via cfgmgr32
- [x] Afficher le nom de l'appareil physique (« P9 ») plutôt que celui du service (« P9 Hands-Free AG »),
      via le `ContainerId` commun aux nœuds d'un même appareil
- **Critère de fin** : la console affiche « P9 : 80 % » ✅ (lecture complète ≈ 70 ms)
- Lancement : `dotnet run --project src/BtBatteryTray -- --verbose`

### Étape 2 – Icônes dans la barre des tâches (session 2)
- [x] Passer en application WinForms sans fenêtre (une seule instance à la fois)
- [x] Une `NotifyIcon` par appareil, chiffre dessiné dynamiquement + couleur
- [x] Info-bulle et menu clic droit (Actualiser, Quitter)
- [x] Rafraîchissement provisoire toutes les minutes (remplacé à l'étape 3)
- [x] Mode texte conservé : `BtBatteryTray.exe --list [--verbose]`
- [x] Validation visuelle par l'utilisateur (après correction de la troncature : texte vectoriel mis à l'échelle)
- **Critère de fin** : chaque icône affiche le bon niveau ✅
- Mesure au lancement : ~10 Mo de RAM privée

### Étape 3 – Rafraîchissement économe ✅ (session 2)
- [x] Abonnement aux notifications Windows (`DeviceWatcher` WinRT, filtre AQS sur les nœuds `BTHENUM\` / `BTHLE\` présents)
- [x] Regroupement des notifications (1 s) pour une seule lecture par rafale
- [x] Timer de secours 5 min + redémarrage automatique de la surveillance si Windows l'interrompt
- [x] Gestion connexion/déconnexion : l'icône d'un appareil déconnecté disparaît
- [x] Journal de diagnostic optionnel : `BtBatteryTray.exe --log` → `%TEMP%\BtBatteryTray.log`
- [x] Mesures : ~15 Mo de RAM privée, ~0,3 s CPU cumulé en 4 min (démarrage inclus), aucune activité hors notifications
- **Critère de fin** : ✅ testé en éteignant/rallumant le casque : icône retirée/rétablie en ~1 s
- Notes techniques :
  - L'API native `DevCreateObjectQuery` (cfgmgr32) renvoie `0x8007007E` sur ce PC (build 26300),
    alors que `DevGetObjects` et la surcouche WinRT fonctionnent : d'où le choix de WinRT.
  - Coût : `Microsoft.Windows.SDK.NET.dll` (~24 Mo sur disque, chargé en partie seulement en mémoire).
  - Dans un filtre AQS, les clés `{GUID} pid` ne sont pas acceptées : seuls les noms canoniques le sont
    (`System.Devices.DeviceInstanceId`, `System.Devices.Present`…).

### Étape 4 – Confort (session 4)
- [ ] Possibilité de masquer un appareil dans le menu (ex. ne garder que le casque)
- [ ] Petit fichier de config (`settings.json`) pour mémoriser le choix
- [ ] Option : notification Windows quand la batterie passe sous 20 %
- [ ] Option « Lancer au démarrage » dans le menu

### Étape 5 – Livraison (session 4)
- [ ] Publication en exe unique (`dotnet publish -c Release`)
- [ ] Installation dans `%LOCALAPPDATA%\BtBatteryTray` + raccourci de démarrage
- [ ] README d'utilisation
- [ ] Penser à épingler l'icône pour qu'elle reste visible (Paramètres > Personnalisation > Barre des tâches > Autres icônes)

## Journal des sessions
- **Session 1 (24/11/2026)** : faisabilité validée, plan rédigé. Prochaine action : installer le SDK .NET 8.
- **Session 2 (24/11/2026)** : dépôt GitHub public créé, prototype console fonctionnel. Prochaine action : étape 2 (icône dans la barre des tâches).
  Choix validé : une icône par appareil. Étapes 2 et 3 terminées et validées. Licence MIT ajoutée.
  Prochaine action : étape 4 (confort).
