using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace KeyCodeSetup;

/// <summary>
/// Assistant d'installation graphique autonome pour KeyceCode IDE.
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
            "Programs", "KeyceCode");
        ChampDossier.Text = defaut;

        var aPayload = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Contains("KeyCodeSetup.Payload.zip");
        TexteInfoPayload.Text = aPayload
            ? "Paquet complet KeyceCode IDE 2.0 embarqué prêt à être installé sans connexion Internet."
            : "Mode test / dev : aucun paquet ZIP embarqué — sélectionnez ou générez d'abord le livrable via installer/construire-setup.ps1.";
    }

    private void Parcourir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choisir le dossier d'installation de KeyceCode",
            InitialDirectory = Directory.Exists(ChampDossier.Text) ? ChampDossier.Text : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (dlg.ShowDialog() == true)
            ChampDossier.Text = Path.Combine(dlg.FolderName, "KeyceCode");
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
            MessageBox.Show("Veuillez choisir un dossier d'installation valide.", "KeyceCode Setup",
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
            TexteResume.Text = $"KeyceCode IDE a été installé dans :\n{cible}\n\nVous pouvez également le désinstaller à tout moment depuis les Paramètres Windows > Applications.";
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
            MessageBox.Show($"Erreur pendant l'installation :\n\n{ex.Message}", "KeyceCode Setup",
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
            TexteEtape.Text = "Extraction des fichiers de KeyceCode IDE...";
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
            var bureauDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var ancienLien = Path.Combine(bureauDir, "KeyCode.lnk");
            try { if (File.Exists(ancienLien)) File.Delete(ancienLien); } catch { }

            var lienBureau = Path.Combine(bureauDir, "KeyceCode.lnk");
            CreerRaccourci(lienBureau, exe, cible, "Éditeur KeyceCode IDE");
        }

        if (menuDemarrer)
        {
            var progsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var dossierStart = Path.Combine(progsDir, "KeyceCode");
            Directory.CreateDirectory(dossierStart);
            CreerRaccourci(Path.Combine(dossierStart, "KeyceCode.lnk"), exe, cible, "Éditeur KeyceCode IDE");
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
        using (var kFile = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\KeyceCode"))
        {
            kFile.SetValue("", "Ouvrir avec KeyceCode");
            kFile.SetValue("Icon", $"\"{exe}\"");
            using var cmd = kFile.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }

        using (var kDir = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\KeyceCode"))
        {
            kDir.SetValue("", "Ouvrir le dossier dans KeyceCode");
            kDir.SetValue("Icon", $"\"{exe}\"");
            using var cmd = kDir.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" \"%V\"");
        }
    }

    private static string EcrireDesinstallateur(string cible)
    {
        var cheminPs1 = Path.Combine(cible, "Desinstaller-KeyceCode.ps1");
        var contenuPs1 = $@"
$ErrorActionPreference = 'SilentlyContinue'
Get-Process KeyCode | Stop-Process -Force
Remove-Item -Force ""$([Environment]::GetFolderPath('DesktopDirectory'))\KeyceCode.lnk""
Remove-Item -Force ""$([Environment]::GetFolderPath('DesktopDirectory'))\KeyCode.lnk""
Remove-Item -Recurse -Force ""$([Environment]::GetFolderPath('Programs'))\KeyceCode""
Remove-Item -Recurse -Force ""$([Environment]::GetFolderPath('Programs'))\KeyCode""
Remove-Item -Recurse -Force 'HKCU:\Software\Classes\*\shell\KeyceCode'
Remove-Item -Recurse -Force 'HKCU:\Software\Classes\Directory\shell\KeyceCode'
Remove-Item -Recurse -Force 'HKCU:\Software\Classes\*\shell\KeyCode'
Remove-Item -Recurse -Force 'HKCU:\Software\Classes\Directory\shell\KeyCode'
Remove-Item -Recurse -Force 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyceCode'
Remove-Item -Recurse -Force 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyCode'
Start-Sleep -Milliseconds 300
$cible = '{cible.Replace("'", "''")}'
Start-Process cmd.exe -WindowStyle Hidden -ArgumentList ""/c timeout /t 2 /nobreak >nul & rmdir /s /q `""$cible`""""
";
        File.WriteAllText(cheminPs1, contenuPs1, Encoding.UTF8);

        var cheminCmd = Path.Combine(cible, "Desinstaller-KeyceCode.cmd");
        File.WriteAllText(cheminCmd, $"@echo off\r\npowershell -ExecutionPolicy Bypass -File \"{cheminPs1}\"\r\n", Encoding.ASCII);
        return cheminCmd;
    }

    private static void EnregistrerAjoutSuppression(string cible, string exe, string desinstallerCmd)
    {
        using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\KeyceCode");
        k.SetValue("DisplayName", "KeyceCode IDE");
        k.SetValue("DisplayVersion", "2.0.0");
        k.SetValue("Publisher", "Équipe KeyceCode");
        k.SetValue("DisplayIcon", exe);
        k.SetValue("InstallLocation", cible);
        k.SetValue("UninstallString", $"\"{desinstallerCmd}\"");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
