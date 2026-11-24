namespace KeyCode.Services;

/// <summary>Fiche d'une extension du catalogue KeyCode (données d'affichage de la Place de marché).</summary>
public sealed class ExtensionInfo
{
    public string Id { get; init; } = "";
    public string Nom { get; init; } = "";
    public string Editeur { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public long Telechargements { get; init; }
    public double Note { get; init; }
    public int NombreAvis { get; init; }
    public int Commanditaires { get; init; }
    public string Categorie { get; init; } = "Other";
    public string Taille { get; init; } = "";
    public string Publie { get; init; } = "";
    public string DernierePublication { get; init; } = "";
    public string DerniereMiseAJour { get; init; } = "";
    public string Depot { get; init; } = "";

    /// <summary>Texte de l'onglet DÉTAILS. Préfixes : "# " titre, "## " sous-titre, "- " puce, "> " code, sinon paragraphe.</summary>
    public string[] Details { get; init; } = [];

    /// <summary>Puces de l'onglet FONCTIONNALITÉS.</summary>
    public string[] Fonctionnalites { get; init; } = [];

    /// <summary>Commandes au format "commande|description|raccourci".</summary>
    public string[] Commandes { get; init; } = [];

    /// <summary>Journal au format "version|texte".</summary>
    public string[] Journal { get; init; } = [];
}