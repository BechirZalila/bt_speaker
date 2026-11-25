# BT Battery Tray

Petit utilitaire Windows qui affiche en permanence, dans la zone de notification de la barre des tâches,
le niveau de batterie de tes appareils Bluetooth (casque, souris…) : une icône par appareil connecté,
avec le pourcentage en couleur.

- 🟢 au-dessus de 50 %, 🟠 de 21 à 50 %, 🔴 à 20 % et moins
- Au survol : nom de l'appareil et niveau (« P9 : 80 % »)
- L'icône disparaît quand l'appareil est éteint et revient à sa reconnexion

**Aucun impact sur la batterie** : l'application lit la valeur que Windows connaît déjà (celle des
Paramètres Bluetooth) et ne se réveille que lorsque Windows signale un changement. Elle n'envoie aucune
requête aux appareils. Environ 15 Mo de mémoire.

**Mises à jour** : une fois par jour, l'application demande à GitHub si une nouvelle version est publiée
(une seule requête, sans aucune donnée personnelle). Si c'est le cas, une notification le signale et
l'installation se fait en un clic. Cette recherche peut être désactivée dans le menu.

## Prérequis
- Windows 10 (2004) ou Windows 11, 64 bits
- Un appareil Bluetooth qui communique son niveau de batterie à Windows
  (s'il apparaît avec un pourcentage dans *Paramètres > Bluetooth et appareils*, c'est bon)

## Installation
1. Télécharger `BtBatteryTray-Setup-<version>.exe` depuis la
   [dernière release](https://github.com/BechirZalila/bt_speaker/releases/latest).
2. Le lancer et suivre l'assistant. Aucun droit administrateur n'est nécessaire :
   l'application s'installe dans `%LOCALAPPDATA%\Programs\BT Battery Tray`.
   - Option « Lancer au démarrage de Windows » (cochée par défaut)
   - Si le runtime .NET 8 Desktop est absent, l'assistant le télécharge et l'installe
     (Windows demande alors une autorisation administrateur pour cette seule étape).
   - L'assistant n'est pas signé : Windows SmartScreen peut afficher un avertissement
     (*Informations complémentaires > Exécuter quand même*).
3. Mises à jour : à partir de la version 1.1.0, l'application les propose elle-même (voir *Utilisation*).
   On peut aussi lancer l'assistant de la nouvelle version (l'application est fermée automatiquement).

Désinstallation : *Paramètres > Applications > Applications installées > BT Battery Tray*.

## Installation depuis les sources (développeurs)
Nécessite le SDK .NET 8 : `winget install --id Microsoft.DotNet.SDK.8 -e`

Dans PowerShell, depuis le dossier du projet :
```powershell
.\install.ps1
```
L'application est compilée, installée dans `%LOCALAPPDATA%\BtBatteryTray`, configurée pour se lancer
au démarrage de Windows, puis démarrée. Aucun droit administrateur n'est nécessaire.

- Sans lancement automatique : `.\install.ps1 -NoStartup`
- Pour mettre à jour après une modification du code : relancer `.\install.ps1`
- Si PowerShell refuse d'exécuter le script :
  `powershell -ExecutionPolicy Bypass -File .\install.ps1`

**Icônes invisibles ?** Windows 11 range les nouvelles icônes derrière la flèche **^** près de l'horloge.
Fais-les glisser dans la barre des tâches, ou active-les dans
*Paramètres > Personnalisation > Barre des tâches > Autres icônes de la barre d'état système*.

## Utilisation
Clic droit sur une icône :

| Menu | Effet |
|---|---|
| **Installer la version X…** | Affiché seulement si une nouvelle version existe : la télécharge, l'installe et relance l'application |
| Appareils affichés | Cocher/décocher les appareils à afficher |
| Alerte batterie faible | Notification Windows quand un appareil passe sous le seuil (20 % par défaut), une fois par décharge |
| Lancer au démarrage de Windows | Active/désactive le lancement automatique |
| Mises à jour | *Rechercher maintenant*, *Rechercher automatiquement* (une fois par jour, activé par défaut), version installée |
| Actualiser | Relit immédiatement les niveaux |
| Quitter | Ferme l'application |

Les préférences sont enregistrées dans `%APPDATA%\BtBatteryTray\settings.json`. Le seuil d'alerte
peut y être modifié (`"LowBatteryThreshold": 20`), puis relancer l'application.

### Ligne de commande
| Commande | Effet |
|---|---|
| `BtBatteryTray.exe --list` | Affiche les niveaux dans le terminal |
| `BtBatteryTray.exe --list --verbose` | Idem, avec l'identifiant Windows de chaque appareil |
| `BtBatteryTray.exe --log` | Lance l'application avec un journal de diagnostic dans `%TEMP%\BtBatteryTray.log` |
| `BtBatteryTray.exe --check-update [--download]` | Indique si une nouvelle version existe ; `--download` télécharge et vérifie l'assistant sans l'installer |

La mise à jour intégrée télécharge l'assistant dans `%TEMP%\BtBatteryTray-Update`, vérifie son empreinte
SHA-256 (fournie par GitHub), puis le lance en mode silencieux. Pour une copie installée avec `install.ps1`,
l'application ouvre seulement la page de la release.

## Désinstallation
```powershell
.\uninstall.ps1                 # conserve les préférences
.\uninstall.ps1 -RemoveSettings # supprime aussi les préférences
```

## Développement
```powershell
dotnet run --project src/BtBatteryTray            # lancer en mode développement
dotnet run --project src/BtBatteryTray -- --list  # mode texte
```
L'icône de l'application (`src/BtBatteryTray/app.ico`) est générée par `tools/make-icon.ps1`.

### Construire l'assistant d'installation
Nécessite [Inno Setup 6](https://jrsoftware.org/isinfo.php) : `winget install --id JRSoftware.InnoSetup -e`
```powershell
.\tools\build-installer.ps1   # → artifacts\BtBatteryTray-Setup-<version>.exe
```
La version est lue dans `src/BtBatteryTray/BtBatteryTray.csproj` ; le script de l'assistant est
`installer/BtBatteryTray.iss`.

La conception et l'historique du projet sont décrits dans [PLAN.md](PLAN.md).

## Licence
[MIT](LICENSE)
