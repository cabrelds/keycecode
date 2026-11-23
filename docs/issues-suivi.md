# Suivi structuré des issues — Évolution KeyCode IDE 2.0

Ce document regroupe les issues structurées liées à la stabilisation complète de **KeyCode**, à son alignement ergonomique et technique sur **Visual Studio Code**, à l'intégration de l'assistant IA **Google Gemini**, et à la création de l'installateur Windows.

---

## Issue #1 — [CRITIQUE / BUG] Correction des plantages silencieux à l'ouverture de fichiers et à l'exécution (`CLR20r3`)

- **Labels** : `bug`, `critique`, `stabilite`
- **Symptôme signalé** : Double-cliquer sur un fichier depuis l'explorateur latéral (`.cs`, `.rs`, `.c`, `.cpp`, images ou binaires) ou lancer une exécution faisait planter et fermer `KeyCode.exe` sans message.
- **Causes racines identifiées** :
  1. `ICSharpCode.AvalonEdit.Highlighting.HighlightingEngine.HighlightNonSpans` levait `InvalidOperationException` (*A highlighting rule matched 0 characters*) dans `ColorationService.cs` car AvalonEdit compile les balises `<Rule>` XSHD avec `RegexOptions.IgnorePatternWhitespace` : les caractères `#` non échappés (directives C#, attributs Rust, `#include`, couleurs hexadécimales CSS) commentaient la fin du motif regex.
  2. Absence de filets de sécurité globaux dans `App.xaml.cs` (`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`).
  3. Race conditions dans `Runner.cs` lors de l'arrêt/accès concurrent à `_process`, encodage console Windows non forcé en UTF-8, et détection manquante entre projet `.csproj` et fichier `.cs` autonome.
  4. Tentatives d'ouverture de fichiers binaires/lourds comme texte brut ou d'éléments non-fichiers lors du double-clic dans `ExplorateurControl`.
- **Corrections implémentées** :
  - `KeyCode/Services/ColorationService.cs` : échappement systématique `\#`, validation XSHD sécurisée, support étendu (TypeScript, CSS, SQL, Markdown, YAML, Batch, PowerShell, Rust, C/C++, Python, JS, HTML, XML, JSON, Java, PHP, Go).
  - `KeyCode/App.xaml.cs` : interception globale des exceptions et journalisation dans `%AppData%\KeyCode\crash.log`.
  - `KeyCode/Runner.cs` & `KeyCode/Services/LangagesService.cs` : verrou thread-safe `_verrou`, gestion `.csproj` vs script C# autonome, sortie UTF-8, exécution console/graphique (Tkinter, GUI) et aperçu web navigateur (`.html`, `.htm`, `.svg`).
  - `KeyCode/Controls/ExplorateurControl.xaml.cs` & `KeyCode/Controls/OngletsControl.xaml.cs` : visionneuse d'images intégrée, garde-fou fichiers binaires/volumineux, filtrage strict du double-clic sur `TreeViewItem`.

---

## Issue #2 — [FEAT] Diagnostics en temps réel, soulignages ondulés et erreurs cliquables dans le terminal

- **Labels** : `enhancement`, `diagnostique`, `editeur`
- **Objectif** : Reproduire le comportement de VS Code pour la détection et la navigation d'erreurs.
- **Implémentation** :
  - `KeyCode/Services/DiagnosticService.cs` : vérification instantanée de l'équilibre des délimiteurs, validation JSON (`JsonDocument`) et XML/XAML (`XmlReader`), vérification syntaxique asynchrone Python (`py -m py_compile` / `python`), Node.js (`node --check`), PHP (`php -l`), et extraction des traces d'erreurs depuis les sorties terminal (Python, C#/MSBuild, GCC/G++/Rust, Node.js, PHP, Go).
  - `KeyCode/Services/MarqueurService.cs` : moteur de rendu `IBackgroundRenderer` pour AvalonEdit dessinant les soulignages ondulés (`Erreur` rouge `#F14C4C`, `Avertissement` ambre `#CCA700`, `Information` bleu `#3794FF`) + infobulles au survol de la souris.
  - `KeyCode/Controls/TerminalControl.xaml(.cs)` : panneau inférieur à 3 onglets (**PROBLÈMES**, **EXÉCUTION** colorisée avec liens hypertextes `fichier:ligne` cliquables, **TERMINAL** PowerShell interactif) + bouton d'envoi direct vers Gemini AI.

---

## Issue #3 — [FEAT] Design et ergonomie VS Code (Thèmes Sombre+/Clair+, Fenêtre personnalisée, Fil d'Ariane, Palette)

- **Labels** : `enhancement`, `ui/ux`, `vscode`
- **Objectif** : Offrir une interface fluide, cohérente et fidèle à VS Code sur 100 % des contrôles WPF.
- **Implémentation** :
  - `KeyCode/MainWindow.xaml(.cs)` : barre de titre personnalisée `WindowChrome`, barre d'activité latérale, barre de statut interactive (dossier, diagnostics `⊗ / ⚠`, position curseur, indentation, langage, encodage, zoom, bouton thème, accès Gemini), gestion des arguments CLI et glisser-déposer.
  - `KeyCode/Themes/VsCodeStyles.xaml` & `KeyCode/Services/ThemeManager.cs` : thèmes unifiés **Sombre+** et **Clair+** appliqués aux menus, menus contextuels, scrollbars, arbres, listes, champs, boutons et onglets.
  - `KeyCode/Controls/PaletteCommandesControl.xaml(.cs)` : Quick Open (`Ctrl+P`) et Palette de commandes (`Ctrl+Maj+P`).
  - `KeyCode/Controls/OngletsControl.xaml(.cs)` : fil d'Ariane (breadcrumb), badges colorés par extension, restauration de session (`SettingsService`), synchronisation renommage/suppression.
  - `KeyCode/Controls/RechercheControl.xaml(.cs)` & `RechercheFichiersControl.xaml(.cs)` : rechercher/remplacer avec support Regex (`.*`), aller à la ligne (`Ctrl+G`) et recherche globale filtrée dans l'espace de travail.

---

## Issue #4 — [FEAT] Assistant IA Google Gemini intégré

- **Labels** : `enhancement`, `ai`, `gemini`
- **Objectif** : Intégrer Google Gemini directement dans la barre latérale droite comme Copilot dans VS Code, selon les préférences utilisateur.
- **Implémentation** :
  - `KeyCode/Services/GeminiService.cs` : client HTTP asynchrone vers `generativelanguage.googleapis.com/v1beta/models/{modele}:generateContent`.
  - `KeyCode/Controls/AssistantIAControl.xaml(.cs)` : chat latéral (`Ctrl+Alt+I`), choix du modèle (`gemini-2.5-flash`, `gemini-2.5-pro`, `gemini-2.0-flash`), stockage sécurisé dans `SettingsService`, injection optionnelle du fichier actif / sélection / erreurs courantes, actions rapides (**Expliquer**, **Corriger**, **Optimiser**, **Tests**), et boutons **Copier / Insérer / Remplacer** sur chaque bloc de code proposé.

---

## Issue #5 — [BUILD / DOCS] Installateur Windows autonome et guide d'utilisation/tests

- **Labels** : `build`, `packaging`, `documentation`
- **Objectif** : Permettre l'installation en un clic sur Windows et documenter l'utilisation ainsi que la recette complète.
- **Implémentation** :
  - `installer/` (`KeyCodeSetup.csproj`, `MainWindow.xaml(.cs)`) : assistant d'installation graphique 100 % C# WPF générant `KeyCodeSetup.exe` avec raccourcis Bureau/Menu Démarrer, entrée « Ouvrir avec KeyCode » dans le menu contextuel Explorer et désinstallateur Windows.
  - `installer/KeyCode.iss` & `installer/construire-setup.ps1` : automatisation du build Release `win-x64` et génération de l'installateur.
  - `docs/guide-utilisation-tests.md` : guide complet d'installation, d'utilisation et cahier de tests manuels.
