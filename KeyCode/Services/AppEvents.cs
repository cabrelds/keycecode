namespace KeyCode.Services;

/// <summary>
/// Événements globaux : c'est le seul moyen de communication entre les contrôles.
/// Propriétaire : cabrelds. Ne pas renommer, ne pas supprimer.
/// </summary>
public static class AppEvents
{
    // Un fichier doit être ouvert (paramètre : chemin complet)
    public static event Action<string>? FichierDemande;
    public static void DemanderFichier(string chemin) => FichierDemande?.Invoke(chemin);

    // Un fichier doit être ouvert à une ligne précise (chemin complet, numéro de ligne à partir de 1)
    public static event Action<string, int>? FichierDemandeALaLigne;
    public static void DemanderFichierALaLigne(string chemin, int ligne) => FichierDemandeALaLigne?.Invoke(chemin, ligne);

    // Un dossier a été ouvert dans l'explorateur (paramètre : chemin du dossier)
    public static event Action<string>? DossierOuvert;
    public static void SignalerDossierOuvert(string chemin) => DossierOuvert?.Invoke(chemin);

    // Le thème a changé (paramètre : "Clair" ou "Sombre")
    public static event Action<string>? ThemeChange;
    public static void SignalerThemeChange(string theme) => ThemeChange?.Invoke(theme);

    // L'éditeur actif a changé (changement d'onglet)
    public static event Action? EditeurActifChange;
    public static void SignalerEditeurActifChange() => EditeurActifChange?.Invoke();

    // Un fichier vient d'être enregistré (paramètre : chemin complet)
    public static event Action<string>? FichierEnregistre;
    public static void SignalerFichierEnregistre(string chemin) => FichierEnregistre?.Invoke(chemin);
}
