using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace KeyCodeSetup;

/// <summary>
/// Assistant d'installation graphique autonome pour KeyCode IDE.
/// </summary>
public partial class MainWindow : Window
{
    private bool _installe;
    private bool _enCours;
    private string? _dossierInstalle;

    public MainWindow()
    {
        InitializeComponent();

        var defaut = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "KeyCode");
        ChampDossier.Text = defaut;

        var aPayload = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Contains("KeyCodeSetup.Payload.zip");
        TexteInfoPayload.Text = aPayload
            ? "Paquet complet embarqué prêt à être installé sans connexion Internet."
            : "Mode test / dev : aucun paquet ZIP embarqué — sélectionnez ou générez d'abord le livrable via installer/construire-setup.ps1.";
    }

    private void Parcourir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choisir le dossier d'installation de KeyCode",
            InitialDirectory = Directory.Exists(ChampDossier.Text) ? ChampDossier.Text : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (dlg.ShowDialog() == true)
            ChampDossier.Text = Path.Combine(dlg.FolderName, "KeyCode");
    }

    private void Annuler_Click(object sender, RoutedEventArgs e)
    {
        if (_enCours) return;
        Close();
    }

    private async void ActionPrincipale_Click(object sender, RoutedEventArgs e)
    {
        if (_installe)
        {
            if (OptLancer.IsChecked == true && !string.IsNullOrEmpty(_dossierInstalle))
            {
                var exe = Path.Combine(_dossierInstalle, "KeyCode.exe");
                if (File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = _dossierInstalle });
            }
            Close();
            return;
        }

        var cible = ChampDossier.Text.Trim();
        if (string.IsNullOrWhiteSpace(cible))
        {
            MessageBox.Show("Veuillez choisir un dossier d'installation valide.", "KeyCode Setup",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _enCours = true;
        BoutonPrincipal.IsEnabled = false;
        BoutonAnnuler.IsEnabled = false;
        PanneauConfig.Visibility = Visibility.Collapsed;
        PanneauProgression.Visibility = Visibility.Visible;

        try
        {
            var creerBureau = OptBureau.IsChecked == true;
            var creerMenuDemarrer = OptMenuDemarrer.IsChecked == true;
            var ajouterContextuel = OptMenuContextuel.IsChecked == true;

            await Task.Run(() => InstallerApplication(cible, creerBureau, creerMenuDemarrer, ajouterContextuel));

            _dossierInstalle = cible;
            _installe = true;
            PanneauProgression.Visibility = Visibility.Collapsed;
            PanneauTermine.Visibility = Visibility.Visible;
            TexteResume.Text = $"KeyCode a été installé dans :\n{cible}\n\nVous pouvez également le désinstaller à tout moment depuis les Paramètres Windows > Applications.";
            BoutonAnnuler.Visibility = Visibility.Collapsed;
            BoutonPrincipal.Content = "Terminer";
            BoutonPrincipal.IsEnabled = true;
        }
        catch (Exception ex)
        {
            PanneauProgression.Visibility = Visibility.Collapsed;
            PanneauConfig.Visibility = Visibility.Visible;
            BoutonPrincipal.IsEnabled = true;
            BoutonAnnuler.IsEnabled = true;
            MessageBox.Show($"Erreur pendant l'installation :\n\n{ex.Message}", "KeyCode Setup",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _enCours = false;
        }
    }

    private void InstallerApplication(string cible, bool bureau, bool menuDemarrer, bool contextuel)
    {
        Dispatcher.Invoke(() =>
        {
            BarreProgression.Value = 15;
            TexteEtape.Text = "Extraction des fichiers de KeyCode...";
            TexteJournal.Text = cible;
        });

        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("KeyCodeSetup.Payload.zip")
            ?? throw new InvalidOperationException("Le paquet d'installation embarqué est introuvable.");

        Directory.CreateDirectory(cible);

        using (var archive = new ZipArchive(flux, ZipArchiveMode.Read))
        {
            int total = Math.Max(1, archive.Entries.Count);
            int fait = 0;

            foreach (var entree in archive.Entries)
            {
                fait++;
                var dest = Path.GetFullPath(Path.Combine(cible, entree.FullName));
                if (!dest.StartsWith(Path.GetFullPath(cible), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entree.Name))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entree.ExtractToFile(dest, overwrite: true);

                if (fait % 5 == 0 || fait == total)
                {
                    int pct = 15 + (int)(60.0 * fait / total);
                    Dispatcher.Invoke(() =>
                    {
                        BarreProgression.Value = pct;
                        TexteJournal.Text = entree.FullName;
                    });
                }
            }
        }

        var exe = Path.Combine(cible, "KeyCode.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException("KeyCode.exe introuvable après extraction.", exe);

        Dispatcher.Invoke(() =>
        {
            BarreProgression.Value = 82;
            TexteEtape.Text = "Création des raccourcis et du désinstallateur...";
        });

        if (bureau)
        {
            var lienBureau = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "KeyCode.lnk");
            CreerRaccourci(lienBureau, exe, cible, "Éditeur KeyCode IDE");
        }

        if (menuDemarrer)
        {
            var dossierStart = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "KeyCode");
            Directory.CreateDirectory(dossierStart);
            CreerRaccourci(Path.Combine(dossierStart, "KeyCode.lnk"), exe, cible, "Éditeur KeyCode IDE");
        }

        if (contextuel)
        {
            EnregistrerMenuContextuel(exe);
        }

        var scriptDesinstall = EcrireDesinstallateur(cible);
        EnregistrerAjoutSuppression(cible, exe, scriptDesinstall);

        Dispatcher.Invoke(() => BarreProgression.Value = 100);
    }

    private static void CreerRaccourci(string cheminLnk, string cibleExe, string dossierTravail, string description)
    {
        var typeShell = Type.GetTypeFromProgID("WScript.Shell");
        if (typeShell == null) return;

        dynamic shell = Activator.CreateInstance(typeShell)!;
        try
        {
            dynamic raccourci = shell.CreateShortcut(cheminLnk);
            raccourci.TargetPath = cibleExe;
            raccourci.WorkingDirectory = dossierTravail;
            raccourci.IconLocation = $"{cibleExe},0";
            raccourci.Description = description;
            raccourci.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void EnregistrerMenuContextuel(string exe)
    {
        using (var kFile = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\KeyCode"))
        {
            kFile.SetValue("", "Ouvrir avec KeyCode");
            kFile.SetValue("Icon", $"\"{exe}\"");
            using var cmd = kFile.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }

        using (var kDir = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\KeyCode"))
        {
            kDir.SetValue("", "Ouvrir le dossier dans KeyCode");
            kDir.SetValue("Icon", $"\"{exe}\"");
            using var cmd = kDir.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" \"%V\"");
        }
    }

    private static string EcrireDesinstallateur(string cible)
    {
        var cheminPs1 = Path.Combine(cible, "Desinstaller-KeyCode.ps1");
        var contenuPs1 = $@"
$ErrorActionPreference = 'SilentlyContinue'
Get-Process KeyCode | Stop-Process -Force
Remove-Item -Force ""$([Environment]::GetFolderPath('DesktopDirectory'))\KeyCode.lnk""
Remove-Item -Recurse -Force ""$([Environment]::GetFolderPath('Programs'))\KeyCode""
Remove-Item -Recurse -Force 'HKCU:\Software\Classes\*\shell\KeyCode'
Remove-Item -Recurse -Force 'HKCU:\Software\Classes\Directory\shell\KeyCode'
Remove-Item -Recurse -Force 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyCode'
Start-Sleep -Milliseconds 300
$cible = '{cible.Replace("'", "''")}'
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList ""/c timeout /t 2 /nobreak >nul & rmdir /s /q `""$cible`""""
";
        File.WriteAllText(cheminPs1, contenuPs1, Encoding.UTF8);

        var cheminCmd = Path.Combine(cible, "Desinstaller-KeyCode.cmd");
        File.WriteAllText(cheminCmd, $"@echo off\r\npowershell -ExecutionPolicy Bypass -File \"{cheminPs1}\"\r\n", Encoding.ASCII);
        return cheminCmd;
    }

    private static void EnregistrerAjoutSuppression(string cible, string exe, string desinstallerCmd)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyCode");
        k.SetValue("DisplayName", "KeyCode IDE");
        k.SetValue("DisplayVersion", "2.0.0");
        k.SetValue("Publisher", "Équipe KeyCode");
        k.SetValue("DisplayIcon", exe);
        k.SetValue("InstallLocation", cible);
        k.SetValue("UninstallString", $"\"{desinstallerCmd}\"");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
