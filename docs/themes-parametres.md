# Thèmes, paramètres et services de l'éditeur

> Module prévu pour nyangono, repris et écrit par cabrelds (nyangono était indisponible).
> Fichiers concernés : `Services/LangagesService.cs`, `SettingsService.cs`, `ColorationService.cs`, `ThemeManager.cs`, `EditeurService.cs`.
> Aucune signature publique du README n'a été modifiée ; seuls des éléments ont été ajoutés (listés plus bas).

## Rôle du module
Ces cinq services sont des classes statiques utilisées par tous les autres modules :

| Service | À quoi il sert |
|---|---|
| `LangagesService` | Dit au `Runner` comment compiler et lancer chaque langage. |
| `SettingsService` | Mémorise thème, taille de police, derniers fichiers et dernier dossier dans un fichier JSON. |
| `ColorationService` | Colore le code selon le langage et selon le thème. |
| `ThemeManager` | Change le thème Clair / Sombre à chaud et prévient les autres modules. |
| `EditeurService` | Configure chaque éditeur : pliage, auto-indentation, fermeture automatique, auto-complétion, zoom. |

## LangagesService
`LangagesService.Obtenir(string extension)` retourne un `LangageInfo?` (null si le langage est inconnu).
`LangageInfo` contient `Nom`, `Extension`, `Executable`, `Arguments` (`{f}` = chemin du fichier) et `CommandeCompilation` (null si le langage n'a pas besoin d'être compilé).

| Langage | Extensions | Exécution | Compilation |
|---|---|---|---|
| Python | `.py` | `python -u "{f}"` | non |
| JavaScript | `.js` | `node "{f}"` | non |
| C# | `.cs` | `dotnet run "{f}"` (.NET 10) | non |
| Java | `.java` | `java "{f}"` (JDK 11 ou plus, mode fichier source) | non |
| C | `.c` | exécutable temporaire | `gcc` |
| C++ | `.cpp` `.cc` `.cxx` | exécutable temporaire | `g++` |
| PHP | `.php` | `php "{f}"` | non |
| PowerShell | `.ps1` | `powershell -File "{f}"` | non |
| Batch | `.bat` | `cmd /c "{f}"` | non |
| Go | `.go` | `go run "{f}"` | non |
| Rust | `.rs` | exécutable temporaire | `rustc` |

Pour C, C++ et Rust, le programme compilé est écrit dans `%TEMP%` (`keycode_c.exe`, `keycode_cpp.exe`, `keycode_rs.exe`) : le dossier de l'utilisateur reste propre. Le Runner exécute la compilation avec `cmd /c` ; le programme est lancé de la même façon.

**À savoir** : les compilateurs (`gcc`, `g++`, `rustc`, `go`, `php`...) doivent être installés et accessibles dans le `PATH`. Sinon le Runner affiche un message clair (« Échec de la compilation. C est-il installé ? » ou « ... n'est pas installé »).
En C, un `printf` sans retour à la ligne n'apparaît pas avant un `scanf` quand la sortie est redirigée : ajouter `fflush(stdout);` (ou `setvbuf(stdout, NULL, _IONBF, 0);`).

## SettingsService
Propriétés : `TaillePolice` (double, 14 par défaut), `Theme` (`"Clair"` par défaut), `DerniersFichiers` (`List<string>`), `DernierDossier` (string).

- `Charger()` lit `%AppData%\KeyCode\settings.json`. Si le fichier n'existe pas, est vide, corrompu ou contient des valeurs invalides, les **valeurs par défaut** sont utilisées : aucune exception ne sort. La taille de police est limitée entre 8 et 48, un thème inconnu devient `"Clair"`.
- `Enregistrer()` écrit le JSON dans un fichier temporaire puis le remplace, pour ne jamais laisser un fichier à moitié écrit. Une erreur d'écriture est ignorée (l'application ne plante pas).

Exemple de fichier :
```json
{
  "TaillePolice": 16,
  "Theme": "Sombre",
  "DerniersFichiers": [ "C:\\projets\\test.py" ],
  "DernierDossier": "C:\\projets"
}
```
Test : supprimer le fichier, ou y écrire n'importe quoi, puis relancer KeyCode : il démarre normalement avec les valeurs par défaut.

## ThemeManager
- `ThemeManager.Appliquer(string theme)` : accepte `"Clair"` ou `"Sombre"` (autre valeur = Clair), met à jour `ThemeActuel`, remplit le `ResourceDictionary` des couleurs, puis appelle `AppEvents.SignalerThemeChange(theme)`.
- `ThemeManager.ThemeActuel` : thème en cours.
- Ajout : `ThemeManager.Pinceau(string cle)` retourne le pinceau du thème actuel.

Les couleurs sont dans un `ResourceDictionary` ajouté aux ressources de l'application et remplacé à chaud. Tout contrôle peut s'y brancher avec `{DynamicResource ThemeFond}` : il change tout seul avec le thème.
Clés : `ThemeFond`, `ThemeTexte`, `ThemeFondSecondaire`, `ThemeBordure`, `ThemeAccent`, `ThemeNumerosLigne`, `ThemeSelection`, `ThemeLigneCourante`, `ThemeTexteSecondaire`.

## ColorationService
`ColorationService.Appliquer(TextEditor editeur, string chemin)` colore l'éditeur selon l'extension du fichier **et** selon le thème actuel.

AvalonEdit ne connaît ni Python, ni Go, ni Rust, ni Batch, ni JSON, et ses couleurs fixes sont illisibles sur fond sombre. Les définitions de coloration sont donc fabriquées dans le code (format XSHD, en mémoire, aucun fichier supplémentaire ni changement du `.csproj`), avec une palette claire et une palette sombre. Langages colorés : Python, JavaScript, C#, Java, C, C++, PHP, PowerShell, Batch, Go, Rust, JSON, HTML, XML (+ `.xaml`, `.csproj`...). Pour les autres extensions (Markdown...), on utilise la coloration d'origine d'AvalonEdit ; sans définition, le texte reste brut.
Ajout : `ColorationService.ObtenirMotsCles(string? chemin)` donne les mots-clés d'un langage (utilisé par l'auto-complétion).

## EditeurService
`EditeurService.Configurer(TextEditor editeur, string? chemin)` est appelée par `OngletsControl` pour **chaque** éditeur (et de nouveau après « Enregistrer sous »). L'appel est répétable sans rien dupliquer. Elle applique :

| Fonction | Détail |
|---|---|
| Apparence | Consolas, taille = `SettingsService.TaillePolice`, couleurs du thème, numéros de ligne, ligne courante surlignée, coloration. |
| Pliage du code | Langages à accolades : un bloc par paire `{ }` ; Python : un bloc par indentation ; XML/XAML : par balise. Recalculé 0,7 s après la dernière frappe. |
| Auto-indentation | Python : retour à la ligne après `:` = un niveau de plus. Langages à accolades : indentation automatique du C#. Les autres fichiers reprennent l'indentation de la ligne précédente. |
| Fermeture automatique | `(` `[` `{` `"` `'` ajoutent le caractère fermant, le curseur reste au milieu ; taper le caractère fermant le « saute ». Désactivé pour `.txt`, `.md`, `.csv`, `.log`. |
| Auto-complétion | Après 2 lettres, liste des mots-clés du langage et des mots déjà présents dans le fichier. `Tab` ou `Entrée` valide, `Échap` ferme. `Ctrl+Espace` force l'affichage. |
| Zoom | `Ctrl` + molette : ± 1 point (8 à 48), mémorisé dans `SettingsService.TaillePolice`. |

`EditeurService.ReconfigurerTous()` réapplique thème, police et coloration à tous les éditeurs ouverts. Elle est appelée automatiquement sur `AppEvents.ThemeChange`, et par la `MainWindow` après un zoom du menu **Affichage → Zoom**. Les éditeurs sont mémorisés par références faibles : un onglet fermé n'est pas retenu en mémoire.

## Cohérence du zoom
La taille de police a une seule source : `SettingsService.TaillePolice`. Le menu Affichage → Zoom de la `MainWindow` et le zoom `Ctrl`+molette de `EditeurService` la modifient puis appellent `ReconfigurerTous()` : les deux restent toujours cohérents et la taille est restaurée au prochain démarrage.

## Limites connues
- Le thème s'applique à l'éditeur, à la fenêtre d'auto-complétion et aux onglets (qui écoutent `ThemeChange` eux-mêmes). Les menus, la barre de statut, l'explorateur, le terminal et la barre de recherche gardent leurs couleurs actuelles : le terminal et la barre de recherche sont sombres dans les deux thèmes. Ils pourraient suivre le thème en utilisant les clés `ThemeFond`, `ThemeTexte`... avec `DynamicResource`.
- Les compilateurs (gcc, g++, rustc, go, php) ne sont pas fournis avec KeyCode.
- Le pliage des langages à accolades ne comprend pas les chaînes brutes de C++ (`R"(...)"`) ; un pliage manquant n'a aucune conséquence sur le code.

## Captures d'écran
Thème Clair :

![Thème clair](images/theme-clair.png)

Thème Sombre :

![Thème sombre](images/theme-sombre.png)
