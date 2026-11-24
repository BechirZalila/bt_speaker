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
| Rafraîchissement | Événementiel (notification de changement de périphérique) + filet de sécurité toutes les 5 min | CPU au repos ≈ 0 %, aucune requête au casque. |
| Affichage | Le chiffre (ex. « 90 ») dessiné dans l'icône, couleur selon le niveau (vert > 50, orange > 20, rouge ≤ 20), info-bulle « P9 : 90 % » | Lisible d'un coup d'œil. |
| Casque déconnecté | Icône grisée « -- » | |
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

### Étape 2 – Icône dans la barre des tâches (session 2 ou 3)
- [ ] Passer en application WinForms sans fenêtre
- [ ] `NotifyIcon` avec le chiffre dessiné dynamiquement + couleur
- [ ] Info-bulle et menu clic droit (Actualiser, Quitter)
- **Critère de fin** : l'icône affiche le bon niveau

### Étape 3 – Rafraîchissement économe (session 3)
- [ ] Abonnement aux notifications de changement de propriété/périphérique
- [ ] Timer de secours 5 min
- [ ] Gestion connexion/déconnexion du casque (icône grisée)
- [ ] Mesurer : CPU ≈ 0 %, RAM < 30 Mo (Gestionnaire des tâches)
- **Critère de fin** : le niveau se met à jour seul quand il baisse ; pas d'impact mesurable

### Étape 4 – Confort (session 4)
- [ ] Choix du périphérique dans le menu (casque, souris…)
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
