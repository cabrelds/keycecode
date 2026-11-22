# Onglets et restauration de session (DeboraQueen237)

## Rôle du module
`OngletsControl` (`KeyCode.Controls.OngletsControl`) gère les onglets d'éditeurs de texte du cœur de KeyCode ainsi que la restauration automatique de la session au démarrage.

Chaque onglet embarque sa propre instance d'`ICSharpCode.AvalonEdit.TextEditor` configurée exclusivement via `EditeurService.Configurer(editeur, chemin)`. Le contrôle communique avec le reste de l'application uniquement via les contrats partagés `AppEvents`, `Contexte` et `SettingsService`.

## Fonctionnalités principales
- **Un éditeur par onglet** : chaque fichier ou onglet vierge conserve son historique d'annulation (`Ctrl+Z` / `Ctrl+Y`), sa position de curseur, son défilement et sa coloration syntaxique.
- **Badges de langage par extension** : chaque onglet affiche un badge coloré selon l'extension (`PY`, `C#`, `JS`, `C++`, `JAVA`, `MD`, etc.) afin d'identifier immédiatement les fichiers ouverts.
- **Anti-doublon à l'ouverture** : ouvrir un fichier déjà présent dans les onglets (y compris via un chemin relatif ou une casse différente sous Windows) active l'onglet existant sans perdre les modifications en cours.
- **Indicateur de modification (`*`)** : le titre de l'onglet affiche `*` dès que `TextEditor.IsModified` passe à `true`, et retire automatiquement `*` lors de l'enregistrement ou d'un retour à l'état d'origine via `Ctrl+Z`.
- **Fermeture sécurisée et réouverture** : le bouton `✕`, le clic du milieu, `Ctrl+W` ou `PeutFermer()` demandent confirmation (`Oui / Non / Annuler`) avant de fermer un onglet modifié. Les derniers onglets fermés (jusqu'à 20) peuvent être rouverts via `Ctrl+Maj+T` ou le menu contextuel.
- **Réorganisation et accès rapide** : réordonnancement des onglets par glisser-déposer (sans perturber le glisser-déposer de fichiers externes), accès direct par `Ctrl+1` à `Ctrl+9` (`Ctrl+9` vers le dernier onglet), défilement horizontal à la molette et bouton `▾` affichant la liste des onglets ouverts.
- **Menu contextuel enrichi** : clic droit sur un onglet proposant *Enregistrer*, *Enregistrer sous...*, *Fermer*, *Fermer les autres*, *Fermer les onglets enregistrés*, *Fermer tout*, *Rouvrir l'onglet fermé*, *Copier le chemin* et *Ouvrir le dossier contenant*.
- **Détection des modifications externes** : à l'activation de la fenêtre ou au changement d'onglet, détecte si le fichier a été modifié ou supprimé hors de KeyCode (rechargement automatique si non modifié localement, confirmation utilisateur si des modifications locales existent, et passage à l'état modifié en cas de suppression externe).
- **Adaptation au thème** : écoute `AppEvents.ThemeChange` et bascule automatiquement la palette de la barre d'onglets entre les thèmes **Clair** et **Sombre**.
- **Restauration de session** : au chargement initial, rouvre automatiquement les fichiers existants listés dans `SettingsService.DerniersFichiers` (en ignorant sans erreur les fichiers supprimés) ou ouvre un onglet vierge `Sans titre` si aucun fichier n'est à restaurer.

## Méthodes publiques (contrat et ajouts)

| Méthode | Rôle |
|---|---|
| `OuvrirFichier(string chemin)` | Ouvre le fichier dans un nouvel onglet, ou active son onglet s'il est déjà ouvert. |
| `OuvrirFichierALaLigne(string chemin, int ligne)` | Ouvre le fichier puis place le curseur et défile jusqu'à la ligne demandée (indexée à partir de `1`). |
| `NouvelOnglet()` | Crée et active un nouvel onglet vide (`Sans titre`, `Sans titre 2`, etc.). |
| `FermerOngletActif()` | Ferme l'onglet actif après confirmation d'enregistrement s'il est modifié. |
| `bool EnregistrerActif()` | Enregistre l'onglet actif (ouvre `SaveFileDialog` si sans titre) et retourne `false` si l'utilisateur annule. |
| `EnregistrerTout()` | Enregistre tous les onglets modifiés en proposant un emplacement pour les onglets sans titre. |
| `bool PeutFermer()` | Vérifie tous les onglets modifiés avant fermeture de l'application ; retourne `false` si l'utilisateur annule. |
| `List<string> ObtenirCheminsOuverts()` | Retourne la liste ordonnée des chemins des fichiers actuellement ouverts. |
| `RouvrirDernierOngletFerme()` | Rouvre le dernier onglet fermé encore présent sur disque (`Ctrl+Maj+T`). |

## Interactions avec les services partagés
- **`EditeurService.Configurer(editeur, chemin)`** : appelée systématiquement à la création de chaque `TextEditor` et lorsqu'un onglet change de chemin via `Enregistrer` ou `Enregistrer sous...`.
- **`AppEvents.FichierDemande`** : écouté pour ouvrir tout fichier demandé par l'explorateur ou par glisser-déposer.
- **`AppEvents.FichierEnregistre`** : déclenché après chaque sauvegarde réussie (`EnregistrerActif`, `EnregistrerTout` ou menu contextuel).
- **`AppEvents.ThemeChange`** : écouté pour synchroniser les couleurs de la barre d'onglets avec `ThemeManager.ThemeActuel`.
- **`Contexte.CheminActif` et `Contexte.EditeurActif`** : mis à jour à chaque changement d'onglet (ou après enregistrement sous un nouveau chemin), `CheminActif` étant toujours actualisé avant `EditeurActif`.
- **`SettingsService.DerniersFichiers`** : lu au démarrage pour restaurer la session et mis à jour (10 fichiers maximum) à l'ouverture et à l'enregistrement.

## Raccourcis et gestes pris en charge
| Raccourci / Geste | Action |
|---|---|
| `Ctrl+Tab` / `Ctrl+Maj+Tab` | Passer à l'onglet suivant / précédent (boucle) |
| `Ctrl+1` à `Ctrl+8` / `Ctrl+9` | Basculer vers l'onglet 1..8 / dernier onglet |
| `Ctrl+Maj+T` | Rouvrir le dernier onglet fermé |
| Glisser-déposer sur un onglet | Réorganiser l'ordre des onglets |
| Clic sur `+` ou double-clic sur la barre libre | Ouvrir un nouvel onglet vierge |
| Clic sur `▾` | Afficher la liste rapide des onglets ouverts |
| Clic sur `✕` ou clic du milieu sur un onglet | Fermer l'onglet ciblé |
| Clic droit sur un onglet | Menu contextuel complet |
| Molette sur la barre d'onglets | Faire défiler horizontalement les onglets |

## Capture d'écran
![Aperçu de KeyCode avec OngletsControl](images/onglets-apercu.png)
