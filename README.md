# keycecode KeyCode

Éditeur de code en C# (WPF, .NET 10, AvalonEdit), inspiré de VS Code.
Projet de groupe : cabrelds, erwan-rk, DeboraQueen237, morelbrad27-lab, nyangono.

## Lancer le projet
```
cd KeyCode
dotnet run
```

## Structure (ne pas changer)
```
KeyCode/
  MainWindow.xaml(.cs)             -> cabrelds UNIQUEMENT
  Runner.cs                        -> morelbrad27-lab
  Services/
    AppEvents.cs                   -> contrat partagé (cabrelds)
    Contexte.cs                    -> contrat partagé (cabrelds)
    EditeurService.cs              -> nyangono
    LangagesService.cs             -> nyangono
    ThemeManager.cs                -> nyangono
    SettingsService.cs             -> nyangono
    ColorationService.cs           -> nyangono
  Controls/
    ExplorateurControl.xaml(.cs)         -> erwan-rk
    RechercheFichiersControl.xaml(.cs)   -> erwan-rk
    OngletsControl.xaml(.cs)             -> DeboraQueen237
    RechercheControl.xaml(.cs)           -> morelbrad27-lab
    TerminalControl.xaml(.cs)            -> morelbrad27-lab
docs/                              -> une section par membre
```
Namespaces : `KeyCode`, `KeyCode.Services`, `KeyCode.Controls`.

## Répartition et fonctionnalités

| Membre | Branche | Fonctionnalités |
|---|---|---|
| cabrelds | feature/integration | MainWindow, menus Fichier / Édition / Affichage / Exécuter / Aide, raccourcis clavier, barre de statut (ligne, colonne, langage, encodage), fichiers récents, glisser-déposer, confirmation à la fermeture de la fenêtre, assemblage, fusion des PR |
| erwan-rk | feature/explorateur | ExplorateurControl (arborescence, nouveau fichier/dossier, renommer, supprimer, actualiser) + RechercheFichiersControl (Ctrl+Shift+F, résultat cliquable qui ouvre le fichier à la bonne ligne) |
| DeboraQueen237 | feature/onglets | OngletsControl (ouvrir, fermer, `*` si modifié, confirmation d'enregistrement, Ctrl+Tab, nouvel onglet) + restauration de session |
| morelbrad27-lab | feature/recherche-terminal | RechercheControl (Ctrl+F, Ctrl+H, aller à la ligne) + TerminalControl + Runner (sortie en direct, saisie, arrêt) |
| nyangono | feature/themes-parametres | ThemeManager, SettingsService, EditeurService, LangagesService, coloration (dont Python), plier le code, auto-complétion, auto-indentation, fermeture auto des parenthèses, zoom Ctrl+molette |
| Tous | | Tests de son module, section dans `docs/`, captures d'écran pour la démo |

## Contrats : NOMS À RESPECTER EXACTEMENT
Interdit de renommer ou supprimer un nom ci-dessous. Besoin d'un nouveau nom partagé : ouvrir une Issue GitHub (label `contrat`) et attendre la validation de cabrelds.

### Services/AppEvents.cs (cabrelds)
- `AppEvents.DemanderFichier(string chemin)` / événement `FichierDemande` : l'explorateur appelle, les onglets écoutent
- `AppEvents.SignalerDossierOuvert(string chemin)` / événement `DossierOuvert`
- `AppEvents.SignalerThemeChange(string theme)` / événement `ThemeChange` ("Clair" ou "Sombre")
- `AppEvents.SignalerEditeurActifChange()` / événement `EditeurActifChange`
- `AppEvents.SignalerFichierEnregistre(string chemin)` / événement `FichierEnregistre`

### Services/Contexte.cs (cabrelds)
- `Contexte.EditeurActif` (TextEditor?) : mis à jour par OngletsControl, lu par RechercheControl et la barre de statut
- `Contexte.CheminActif` (string?) : fichier de l'onglet actif
- `Contexte.DossierRacine` (string?) : dossier ouvert dans l'explorateur

### Services/EditeurService.cs (nyangono, statique)
- `EditeurService.Configurer(TextEditor editeur, string? chemin)` : applique coloration, thème, police, numéros de ligne, pliage, auto-complétion, auto-indentation.
- **OngletsControl DOIT appeler cette méthode pour chaque éditeur créé** et ne configure rien lui-même.
- `EditeurService.ReconfigurerTous()` : réapplique thème et police (appelée sur `ThemeChange`).

### Services/LangagesService.cs (nyangono, statique)
- `LangagesService.Obtenir(string extension)` retourne `LangageInfo?`
- Classe `LangageInfo` : `Nom`, `Extension`, `Executable`, `Arguments` (avec `{f}` pour le fichier), `CommandeCompilation` (null si inutile)
- Le Runner lit cette liste et n'a aucun dictionnaire de langages.
- Langages minimum : Python, JavaScript, C#, Java, C, C++, PHP, PowerShell, Batch.

### Services/ThemeManager.cs (nyangono, statique)
- `ThemeManager.Appliquer(string theme)`, propriété `ThemeActuel`

### Services/SettingsService.cs (nyangono, statique)
- `Charger()`, `Enregistrer()`
- Propriétés : `TaillePolice` (double), `Theme` (string), `DerniersFichiers` (List<string>), `DernierDossier` (string)
- Fichier : `%AppData%\KeyCode\settings.json`

### Services/ColorationService.cs (nyangono, statique)
- `ColorationService.Appliquer(TextEditor editeur, string chemin)`

### Contrôles (méthodes publiques)
- **ExplorateurControl** : `OuvrirDossier(string chemin)`, `Actualiser()`
- **RechercheFichiersControl** : `Rechercher(string texte)`, `Afficher()`, `Masquer()`
- **OngletsControl** : `OuvrirFichier(string chemin)`, `OuvrirFichierALaLigne(string chemin, int ligne)`, `NouvelOnglet()`, `FermerOngletActif()`, `bool EnregistrerActif()`, `EnregistrerTout()`, `bool PeutFermer()` (demande d'enregistrer, `false` si l'utilisateur annule), `List<string> ObtenirCheminsOuverts()`
- **RechercheControl** : `Afficher(bool remplacer)`, `Masquer()`, `TrouverSuivant()`, `TrouverPrecedent()`, `Remplacer()`, `RemplacerTout()`, `AllerALigne(int ligne)`
- **TerminalControl** : `Executer(string chemin)`, `Arreter()`, `Effacer()`
- **Runner** (instance) : `Demarrer(fichier, surSortie, surFin)`, `Envoyer(texte)`, `Arreter()`, propriété `EnCours`

### Règles de collaboration
- La barre de statut lit `Contexte.EditeurActif.TextArea.Caret` : personne d'autre n'y touche.
- Exécution : `MainWindow` appelle `OngletsControl.EnregistrerActif()` puis `TerminalControl.Executer(Contexte.CheminActif)`.
- Le terminal affiche uniquement la sortie du programme, sans dépendance vers les onglets.
- Les contrôles se parlent uniquement via `AppEvents` et `Contexte`, jamais en s'appelant directement.

## Conventions de nommage
- Code et commentaires **en français**, sans accents dans les identifiants
- Classes, méthodes, propriétés : `PascalCase` (`OuvrirFichier`)
- Champs privés : `_camelCase` (`_chemin`)
- Variables locales et paramètres : `camelCase`
- Contrôles WPF : `x:Name` en PascalCase (`Editeur`, `Sortie`, `Saisie`)
- Gestionnaires d'événements : `Element_Evenement` (`Ouvrir_Click`)
- Fichiers : `NomControl.xaml` + `NomControl.xaml.cs`
- Un fichier = une classe

## Règles Git
1. Ne jamais pousser sur `main`. Une branche par module (nom dans le tableau).
2. Avant de coder : `git checkout main`, `git pull`, `git checkout feature/xxx`, `git merge main`.
3. **Ne jamais faire `git add .`** : ajouter uniquement ses fichiers, par exemple
   `git add KeyCode/Controls/OngletsControl.xaml KeyCode/Controls/OngletsControl.xaml.cs`
4. Ne jamais commiter `MainWindow.xaml(.cs)` (sauf cabrelds). Pour tester, modifier en local puis `git restore KeyCode/MainWindow.xaml KeyCode/MainWindow.xaml.cs`.
5. Commits : `feat: ...`, `fix: ...`, `docs: ...`
6. Commits petits et fréquents. Une PR par fonctionnalité qui compile (`dotnet build` sans erreur).
7. Jamais de `bin/`, `obj/`, clés ou mots de passe dans Git.

## Règles pour les IA
Chaque membre colle ce bloc au début de la conversation avec son IA :
> Projet WPF C#, .NET 10, AvalonEdit, namespace `KeyCode`. Je suis responsable uniquement de [MES FICHIERS]. Ne modifie AUCUN autre fichier. Respecte exactement les noms du README (section Contrats). Code et commentaires en français, nommage selon la section Conventions. Pas de nouvelle dépendance NuGet sans me demander. Fournis le code complet des fichiers, pas des extraits.

Chaque membre doit comprendre son code avant de le commiter : le prof peut poser des questions.
## Arborescence finale (ne pas changer)

Chaque fichier a UN seul responsable. Interdit de modifier le fichier d'un autre : demander à son responsable ou ouvrir une Issue.

```
keycecode/
│
├── .gitignore                                   cabrelds   Fichiers ignorés par Git (bin, obj)
├── README.md                                    cabrelds   Ce document : contrats, règles, répartition
│
├── docs/
│   ├── README.md                                cabrelds   Sommaire de la documentation
│   ├── integration.md                           cabrelds   Assemblage, menus, barre de statut
│   ├── explorateur.md                           erwan-rk   Explorateur et recherche dans les fichiers
│   ├── onglets.md                               DeboraQueen237   Onglets et restauration de session
│   ├── recherche-terminal.md                    morelbrad27-lab   Recherche, terminal, exécution
│   └── themes-parametres.md                     nyangono   Thèmes, paramètres, services de l'éditeur
│
└── KeyCode/
    ├── KeyCode.csproj                           cabrelds   Configuration du projet (NuGet, .NET)
    ├── App.xaml                                 cabrelds   Point d'entrée WPF
    ├── App.xaml.cs                              cabrelds   Code du point d'entrée
    ├── AssemblyInfo.cs                          cabrelds   Informations d'assembly (généré)
    ├── MainWindow.xaml                          cabrelds   Fenêtre principale : disposition, menus, barre de statut
    ├── MainWindow.xaml.cs                       cabrelds   Logique de la fenêtre, raccourcis, fermeture
    ├── Runner.cs                                morelbrad27-lab   Lance les programmes (via LangagesService)
    │
    ├── Services/
    │   ├── AppEvents.cs                         cabrelds   Événements globaux (contrat)
    │   ├── Contexte.cs                          cabrelds   État partagé (contrat)
    │   ├── EditeurService.cs                    nyangono   Configuration d'un éditeur (pliage, auto-complétion, zoom)
    │   ├── LangagesService.cs                   nyangono   Liste des langages exécutables
    │   ├── ThemeManager.cs                      nyangono   Thèmes Clair et Sombre
    │   ├── SettingsService.cs                   nyangono   Paramètres sauvegardés en JSON
    │   └── ColorationService.cs                 nyangono   Coloration syntaxique
    │
    └── Controls/
        ├── ExplorateurControl.xaml              erwan-rk   Arborescence du dossier ouvert
        ├── ExplorateurControl.xaml.cs           erwan-rk   Logique de l'explorateur
        ├── RechercheFichiersControl.xaml        erwan-rk   Recherche dans tous les fichiers (Ctrl+Shift+F)
        ├── RechercheFichiersControl.xaml.cs     erwan-rk   Logique de la recherche globale
        ├── OngletsControl.xaml                  DeboraQueen237   Onglets d'éditeurs
        ├── OngletsControl.xaml.cs               DeboraQueen237   Ouverture, fermeture, enregistrement
        ├── RechercheControl.xaml                morelbrad27-lab   Barre Ctrl+F / Ctrl+H
        ├── RechercheControl.xaml.cs             morelbrad27-lab   Rechercher, remplacer, aller à la ligne
        ├── TerminalControl.xaml                 morelbrad27-lab   Panneau de sortie et de saisie
        └── TerminalControl.xaml.cs              morelbrad27-lab   Exécution et affichage en direct
```

`bin/` et `obj/` existent en local mais ne sont jamais envoyés sur GitHub.

Namespaces : `KeyCode`, `KeyCode.Services`, `KeyCode.Controls`.

### Résumé par responsable

| Responsable | Fichiers |
|---|---|
| cabrelds | `.gitignore`, `README.md`, `docs/README.md`, `docs/integration.md`, `KeyCode.csproj`, `App.xaml(.cs)`, `AssemblyInfo.cs`, `MainWindow.xaml(.cs)`, `Services/AppEvents.cs`, `Services/Contexte.cs` |
| erwan-rk | `docs/explorateur.md`, `Controls/ExplorateurControl.*`, `Controls/RechercheFichiersControl.*` |
| DeboraQueen237 | `docs/onglets.md`, `Controls/OngletsControl.*` |
| morelbrad27-lab | `docs/recherche-terminal.md`, `Runner.cs`, `Controls/RechercheControl.*`, `Controls/TerminalControl.*` |
| nyangono | `docs/themes-parametres.md`, `Services/EditeurService.cs`, `Services/LangagesService.cs`, `Services/ThemeManager.cs`, `Services/SettingsService.cs`, `Services/ColorationService.cs` |

Un besoin de modifier le fichier de quelqu'un d'autre : ouvrir une Issue GitHub et attendre sa réponse.