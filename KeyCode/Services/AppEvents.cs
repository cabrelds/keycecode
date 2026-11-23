namespace KeyCode.Services;

/// <summary>
/// Événements globaux : moyen de communication découplé entre les contrôles et services.
/// Tous les contrats historiques sont conservés à l'identique.
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

    // Un fichier a été renommé dans l'explorateur (ancien chemin, nouveau chemin)
    public static event Action<string, string>? FichierRenomme;
    public static void SignalerFichierRenomme(string ancienChemin, string nouveauChemin) =>
        FichierRenomme?.Invoke(ancienChemin, nouveauChemin);

    // Un fichier a été supprimé dans l'explorateur (chemin)
    public static event Action<string>? FichierSupprime;
    public static void SignalerFichierSupprime(string chemin) => FichierSupprime?.Invoke(chemin);

    // Les diagnostics (erreurs/avertissements) ont été mis à jour
    public static event Action? DiagnosticsChanges;
    public static void SignalerDiagnosticsChanges() => DiagnosticsChanges?.Invoke();

    // Demande d'assistance Gemini sur un message d'erreur / diagnostic
    public static event Action<string, string?, int?>? AideIADemandee;
    public static void DemanderAideIA(string messageErreur, string? chemin = null, int? ligne = null) =>
        AideIADemandee?.Invoke(messageErreur, chemin, ligne);
}
