# WiFiBandSelector

 petit utilitaire Windows (zone de notification) pour choisir la bande Wi-Fi d'un portable en deux
clics : **2,4 GHz**, **5 GHz**, **6 GHz** quand le pilote les expose. Il écrit uniquement les
réglages avancés que le pilote déclare déjà dans le registre ; il ne modifie ni pilote ni profil
Wi-Fi, et n'envoie aucune donnée.

> Public cible : deux HP ProBook 440 G9 utilisés par des étudiants. Développé et vérifié sur une
> carte MediaTek MT7921 ; le classement est générique et fonctionne avec d'autres pilotes, mais les
> ProBook (Realtek) restent à valider avec le rapport de diagnostic.

## Installation (pour les étudiants)

1. Télécharger `WiFiBandSelector-Setup-0.1.0.exe` depuis la page
   [Releases](https://github.com/mperreir/WiFiBandSelector/releases).
2. Si Windows affiche « Marque du web », cliquer sur **Informations complémentaires** puis
   **Exécuter quand même** (le programme n'est pas signé).
3. Exécuter le fichier, répondre **Oui** à la demande UAC, puis **Terminer**.
4. Le .NET 8 Desktop Runtime est téléchargé et installé automatiquement si nécessaire
   (connexion requise). Sinon : `WiFiBandSelector-Setup-0.1.0.exe /RUNTIME=C:\chemin\windowsdesktop-runtime-win-x64.exe`.
5. Une icône Wi-Fi apparaît près de l'horloge ; l'application se lance à chaque ouverture de
   session, pour tous les comptes.

## Utilisation

Cliquer sur l'icône (clic gauche ou droit) ouvre le menu :

- en-tête : carte sélectionnée, réglage courant et bande réellement associée ;
- **Bande préférée** : préférence souple, les deux bandes restent autorisées ;
- **Verrouiller une bande** : réglage strict, disponible seulement si le pilote l'expose ;
- **Itinérance** : agressivité, avec retour à la valeur par défaut du pilote ;
- **Reconnecter maintenant**, **Démarrage avec Windows**, **Annuler la dernière modification** ;
- **Diagnostic…**, **Journal…**, **À propos**, **Quitter**.

Après un verrou, l'application vérifie que la connexion revient dans les 30 secondes ; sinon elle
restaure la valeur précédente et affiche un avertissement. Elle refuse aussi un verrou sur une
bande absente d'un scan non vide.

Si l'icône indique « Relancer en administrateur », le menu est en lecture seule : les écritures dans
le registre exigent les droits élevés fournis par la tâche planifiée.

## Diagnostic

Menu **Diagnostic…** : un rapport est écrit dans
`%LOCALAPPDATA%\WiFiBandSelector\`, copié dans le presse-papiers et ouvert dans l'Explorateur.
Envoyer ce rapport (et, si besoin, `%LOCALAPPDATA%\WiFiBandSelector\log.txt`) au développeur.

Pour un test sans interface :

```powershell
& "C:\Program Files\WiFiBandSelector\WiFiBandSelector.exe" /report "$env:TEMP\rapport.txt"
```

Le rapport indique la carte, les familles reconnues, la bande associée, toutes les valeurs `enum`
du pilote et les motifs de classement. Une carte sans mot-clé de bande est signalée
« non prise en charge » : c'est un résultat normal, pas un bug.

Pour un test d'écriture sans interface (réservé au support, nécessite les droits élevés) :

```powershell
& "C:\Program Files\WiFiBandSelector\WiFiBandSelector.exe" /set preference 1 "$env:TEMP\resultat.txt"
```

La famille est `preference`, `lock` ou `roaming` ; le code est celui indiqué par le rapport.
Le résultat (`Applied`, `Refused`…) est écrit dans le fichier ; code de sortie `0` si l'application
a réussi, `1` sinon.

## Limites connues

- **Realtek RTL8852BE** : le nom exact des mots-clés reste à confirmer sur les ProBook ; le rapport
  permettra d'ajuster le classement si nécessaire.
- **Intel AX211** : pas de verrou exploitable (`Wireless Mode` limiterait le débit) ; seule la
  préférence est proposée.
- **Itinérance** : sans plusieurs points d'accès sur le même SSID, ce réglage n'a aucun effet.
- L'instabilité Wi-Fi des RTL8852BE est souvent liée au pilote ou à l'économie d'énergie ; ces
  paramètres sont volontairement hors périmètre.
- Une mise à jour de pilote peut réinitialiser les valeurs : l'application relit toujours le
  registre au moment où le menu s'ouvre.
- La préférence reste une préférence : le pilote peut choisir une autre bande si le signal est
  faible.
- L'application coupe brièvement la connexion lors d'un changement (quelques secondes en mode
  verrou).
- L'exécutable n'est pas signé : préférer une clé USB ou `Unblock-File` à un transfert OneDrive.
- La tâche planifiée s'exécute avec les droits « les plus élevés disponibles » pour le groupe
  *Utilisateurs* : un compte standard peut donc modifier un réglage réseau via l'application.

## Développement

```powershell
git clone https://github.com/mperreir/WiFiBandSelector.git
cd WiFiBandSelector
dotnet test tests\WiFiBandSelector.Tests\WiFiBandSelector.Tests.csproj
.\build.ps1
```

`build.ps1` : tests, `dotnet publish` x64, téléchargement portable de NSIS 3.11 + plugin `inetc`
si absents, puis `makensis`. Sortie : `dist\WiFiBandSelector-Setup-<version>.exe`.

- `tools\fixtures\*.json` : relevés de registre rejoués par les tests (MediaTek, Intel, Realtek,
  carte exotique, carte sans bande). La fixture Realtek est à confirmer avec l'INF réel.
- `installer\setup.nsi` : génère le XML de la tâche planifiée en UTF-16/BOM avec le chemin réel
  d'installation. Il est écrit ligne par ligne : une variable NSIS plafonne à 1024 caractères et
  tronquerait le document.
- `C:\Users\nous\.opencode\plan\wifi-band-selector.md` : plan de travail (hors dépôt).

## Licence

MIT — 2026 Matthieu Perreira Da Silva.
