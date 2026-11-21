# Intégration (cabrelds)

## Rôle du module
`MainWindow` assemble les contrôles écrits par les autres membres. Elle ne contient aucune logique d'édition : elle appelle les méthodes publiques des contrôles (voir les contrats du README) et écoute `AppEvents`.

## Disposition
- À gauche : `ExplorateurControl`, puis `RechercheFichiersControl` en dessous (séparés du centre par un `GridSplitter`).
- Au centre, de haut en bas : `RechercheControl`, `OngletsControl`, `TerminalControl` (séparés par un `GridSplitter`).
- En bas de la fenêtre : la barre de statut.

## Menus
- **Fichier** : nouveau, ouvrir un fichier, ouvrir un dossier, enregistrer, tout enregistrer, fermer l'onglet, fichiers récents, quitter.
- **Édition** : annuler, rétablir, couper, copier, coller, tout sélectionner, rechercher, remplacer, rechercher dans les fichiers.
- **Affichage** : thème (Clair / Sombre), zoom, afficher ou masquer l'explorateur et le terminal.
- **Exécuter** : exécuter (F5), arrêter, effacer le terminal.
- **Aide** : raccourcis clavier, à propos.

## Raccourcis
| Raccourci | Action |
|---|---|
| Ctrl+N | Nouvel onglet |
| Ctrl+O | Ouvrir un fichier |
| Ctrl+S | Enregistrer |
| Ctrl+W | Fermer l'onglet |
| Ctrl+F / Ctrl+H | Rechercher / remplacer |
| Ctrl+Maj+F | Rechercher dans les fichiers |
| F5 | Exécuter |
| Ctrl+Tab | Onglet suivant (géré par `OngletsControl`) |

## Barre de statut
Elle lit `Contexte.EditeurActif.TextArea.Caret` et affiche la ligne, la colonne, le langage (`LangagesService.Obtenir`) et l'encodage. Elle se rebranche à chaque `AppEvents.EditeurActifChange`.

## Exécution
`MainWindow` appelle `OngletsControl.EnregistrerActif()`, puis `TerminalControl.Executer(Contexte.CheminActif)`.

## Autres fonctions
- Glisser-déposer : un fichier déposé déclenche `AppEvents.DemanderFichier`, un dossier déposé appelle `ExplorateurControl.OuvrirDossier`.
- Fermeture : `OngletsControl.PeutFermer()` demande d'enregistrer ; si l'utilisateur annule, la fenêtre reste ouverte.
- Fichiers récents : liste `SettingsService.DerniersFichiers` (10 maximum).
- Ouverture à une ligne : `AppEvents.DemanderFichierALaLigne(chemin, ligne)` est écouté par `MainWindow`, qui appelle `OngletsControl.OuvrirFichierALaLigne`.

## Captures d'écran
_À ajouter pour la démo._
