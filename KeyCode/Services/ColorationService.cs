using System.IO;
using System.Security;
using System.Text;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace KeyCode.Services;

/// <summary>
/// Coloration syntaxique selon l'extension du fichier.
/// AvalonEdit ne fournit pas Python, Go, Rust, Batch ni JSON, et ses couleurs fixes sont illisibles sur
/// fond sombre : les définitions (format XSHD) sont donc fabriquées ici, par langage et par thème.
/// Si la fabrication échoue ou si l'extension est inconnue, on retombe sur les définitions d'AvalonEdit.
/// Propriétaire : nyangono (module repris par cabrelds).
/// </summary>
public static class ColorationService
{
    // ------------------------------------------------------------------
    // Palettes de couleurs (inspirées de VS Code Clair+ et Sombre+)
    // ------------------------------------------------------------------

    private sealed record Palette(
        string Commentaire, string Chaine, string MotCle, string Controle,
        string Type, string Nombre, string Prepro, string Variable);

    private static readonly Palette _paletteClair =
        new("#008000", "#A31515", "#0000FF", "#AF00DB", "#267F99", "#098658", "#808080", "#001080");

    private static readonly Palette _paletteSombre =
        new("#6A9955", "#CE9178", "#569CD6", "#C586C0", "#4EC9B0", "#B5CEA8", "#9B9B9B", "#9CDCFE");

    // ------------------------------------------------------------------
    // Description des langages
    // ------------------------------------------------------------------

    // Une chaîne de caractères : délimiteurs (expressions régulières), sur plusieurs lignes ou non, avec \ d'échappement ou non
    private sealed record Chaine(string Debut, string Fin, bool Multiligne = false, bool Echappement = true);

    // Une règle libre : nom de couleur + expression régulière
    private sealed record Regle(string Couleur, string Expression);

    private sealed class Langage
    {
        public required string Nom { get; init; }
        public string[] Extensions { get; init; } = [];
        public string[] Controle { get; init; } = [];      // if, for, return...
        public string[] MotsCles { get; init; } = [];      // class, public...
        public string[] Types { get; init; } = [];         // int, string...
        public string[] CommentairesLigne { get; init; } = [];
        public (string Debut, string Fin)? CommentaireBloc { get; init; }
        public Chaine[] Chaines { get; init; } = [];
        public Regle[] Regles { get; init; } = [];
        public bool IgnorerCasse { get; init; }
        public bool Markup { get; init; }                  // HTML / XML
    }

    private const string TroisGuillemets = "\"\"\"";
    private const string TroisApostrophes = "'''";

    private static readonly Chaine _guillemets = new("\"", "\"");
    private static readonly Chaine _apostrophes = new("'", "'");

    private static string[] Mots(string liste) =>
        liste.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static readonly (string Debut, string Fin) _blocC = (@"/\*", @"\*/");
    private const string DirectivePrepro = @"^[ \t]*#[ \t]*[a-z]+";

    private static readonly Langage[] _langages =
    [
        new()
        {
            Nom = "Python", Extensions = [".py", ".pyw"],
            Controle = Mots("if elif else for while break continue return try except finally raise with yield pass assert import from as await async lambda del global nonlocal"),
            MotsCles = Mots("def class and or not in is None True False self cls"),
            Types = Mots("int str float bool list dict set tuple bytes object print input len range open type enumerate zip map filter sum min max abs sorted reversed isinstance super"),
            CommentairesLigne = ["#"],
            Chaines = [new(TroisGuillemets, TroisGuillemets, true), new(TroisApostrophes, TroisApostrophes, true), _guillemets, _apostrophes],
            Regles = [new("Prepro", @"@[A-Za-z_][\w.]*")],
        },
        new()
        {
            Nom = "JavaScript", Extensions = [".js", ".mjs", ".cjs", ".jsx"],
            Controle = Mots("if else for while do switch case break continue return throw try catch finally yield await default"),
            MotsCles = Mots("async class const debugger delete export extends function import in instanceof let new of static super this typeof var void with null undefined true false get set"),
            Types = Mots("Object Array String Number Boolean Map Set Promise Date Math JSON console"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes, new("`", "`", true)],
        },
        new()
        {
            Nom = "C#", Extensions = [".cs"],
            Controle = Mots("if else for foreach while do switch case break continue return goto throw try catch finally yield await default when"),
            MotsCles = Mots("abstract as async base class const delegate enum event explicit extern false fixed implicit in interface internal is lock namespace new null operator out override params partial private protected public readonly record ref sealed sizeof stackalloc static struct this true typeof unchecked unsafe using virtual volatile where get set init required"),
            Types = Mots("bool byte char decimal double float int long object sbyte short string uint ulong ushort void var dynamic nint nuint"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", DirectivePrepro)],
        },
        new()
        {
            Nom = "Java", Extensions = [".java"],
            Controle = Mots("if else for while do switch case break continue return throw try catch finally yield default assert"),
            MotsCles = Mots("abstract class const enum extends final implements import instanceof interface native new null package private protected public static strictfp super synchronized this throws transient true false var volatile record sealed permits"),
            Types = Mots("boolean byte char double float int long short void String Integer Long Double Boolean Object List Map Set ArrayList HashMap Scanner System"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", @"@[A-Za-z_]\w*")],
        },
        new()
        {
            Nom = "C", Extensions = [".c", ".h"],
            Controle = Mots("if else for while do switch case break continue return goto default"),
            MotsCles = Mots("auto const enum extern inline register restrict sizeof static struct typedef union volatile NULL true false"),
            Types = Mots("char double float int long short signed unsigned void size_t bool FILE"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", DirectivePrepro), new("Chaine", @"(?<=^[ \t]*#[ \t]*include[ \t]*)<[^>]*>")],
        },
        new()
        {
            Nom = "C++", Extensions = [".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx"],
            Controle = Mots("if else for while do switch case break continue return goto default try catch throw"),
            MotsCles = Mots("auto class const constexpr delete enum explicit extern false final friend inline mutable namespace new noexcept nullptr operator override private protected public static struct template this true typedef typename union using virtual volatile"),
            Types = Mots("bool char char16_t char32_t double float int long short signed unsigned void wchar_t size_t string vector map set"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", DirectivePrepro), new("Chaine", @"(?<=^[ \t]*#[ \t]*include[ \t]*)<[^>]*>")],
        },
        new()
        {
            Nom = "PHP", Extensions = [".php"],
            Controle = Mots("if elseif else endif for foreach endforeach while endwhile do switch case break continue return throw try catch finally yield match default"),
            MotsCles = Mots("abstract and as class clone const declare echo empty enum extends final function global implements include include_once instanceof interface isset list namespace new or print private protected public readonly require require_once static trait use var xor true false null"),
            Types = Mots("int float string bool array object void mixed self parent"),
            CommentairesLigne = ["//", "#"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes],
            Regles = [new("Prepro", @"<\?php|<\?=|\?>"), new("Variable", @"\$[A-Za-z_]\w*")],
        },
        new()
        {
            Nom = "PowerShell", Extensions = [".ps1", ".psm1", ".psd1"], IgnorerCasse = true,
            Controle = Mots("if elseif else for foreach while do switch break continue return throw try catch finally exit"),
            MotsCles = Mots("function param begin process end in filter class using"),
            CommentairesLigne = ["#"], CommentaireBloc = (@"<#", @"#>"),
            Chaines = [new("\"", "\"", false, false), new("'", "'", false, false)],
            Regles = [new("Variable", @"\$\{[^}]*\}|\$[\w:]+"), new("Type", @"\b[A-Za-z]+-[A-Za-z]\w+\b")],
        },
        new()
        {
            Nom = "Batch", Extensions = [".bat", ".cmd"], IgnorerCasse = true,
            Controle = Mots("if else goto call exit for do in"),
            MotsCles = Mots("echo set setlocal endlocal enabledelayedexpansion pause cls cd dir copy move del start title type md mkdir rd rmdir ren not exist defined errorlevel equ neq lss leq gtr geq off on"),
            CommentairesLigne = ["::", @"^[ \t]*@?rem\b"],
            Chaines = [new("\"", "\"", false, false)],
            Regles = [new("Prepro", @"^[ \t]*:[A-Za-z_][\w.-]*"), new("Variable", @"%[~\w]+%|%%?~?[0-9A-Za-z*]|![\w]+!")],
        },
        new()
        {
            Nom = "Go", Extensions = [".go"],
            Controle = Mots("if else for switch case break continue return goto default select fallthrough defer go range"),
            MotsCles = Mots("chan const func import interface map package struct type var nil true false iota"),
            Types = Mots("bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr any"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets, _apostrophes, new("`", "`", true, false)],
        },
        new()
        {
            Nom = "Rust", Extensions = [".rs"],
            Controle = Mots("if else for while loop match break continue return in"),
            MotsCles = Mots("as async await const crate dyn enum extern false fn impl let mod move mut pub ref self Self static struct super trait true type unsafe use where"),
            Types = Mots("i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box Some None Ok Err"),
            CommentairesLigne = ["//"], CommentaireBloc = _blocC,
            Chaines = [_guillemets],
            Regles =
            [
                new("Chaine", @"'(\\.|[^'\\])'"),
                new("Type", @"'[A-Za-z_]\w*(?!['\w])"),
                new("Prepro", @"#!?\[[^\]]*\]"),
                new("Prepro", @"\b[a-z_]\w*!(?=[\(\[\{])"),
            ],
        },
        new()
        {
            Nom = "JSON", Extensions = [".json"],
            MotsCles = Mots("true false null"),
            Chaines = [_guillemets],
        },
        new() { Nom = "HTML", Extensions = [".html", ".htm"], Markup = true },
        new() { Nom = "XML", Extensions = [".xml", ".xaml", ".csproj", ".svg", ".config", ".xsd", ".props", ".targets", ".resx"], Markup = true },
    ];

    private static readonly Dictionary<string, Langage> _parExtension = new();
    private static readonly Dictionary<string, IHighlightingDefinition?> _cache = new();

    static ColorationService()
    {
        foreach (var langage in _langages)
            foreach (var extension in langage.Extensions)
                _parExtension[extension] = langage;
    }

    // ------------------------------------------------------------------
    // API publique
    // ------------------------------------------------------------------

    /// <summary>Applique la coloration correspondant à l'extension du fichier et au thème actuel.</summary>
    public static void Appliquer(TextEditor editeur, string chemin)
    {
        var definition = ObtenirDefinition(Path.GetExtension(chemin));
        // On ne remplace pas la définition si c'est déjà la bonne (évite de tout recolorier pour rien)
        if (!ReferenceEquals(editeur.SyntaxHighlighting, definition))
            editeur.SyntaxHighlighting = definition;
    }

    /// <summary>Mots-clés du langage du fichier (pour l'auto-complétion). Liste vide si le langage est inconnu.</summary>
    public static IReadOnlyList<string> ObtenirMotsCles(string? chemin)
    {
        if (string.IsNullOrEmpty(chemin)) return Array.Empty<string>();
        if (!_parExtension.TryGetValue(Path.GetExtension(chemin).ToLowerInvariant(), out var langage))
            return Array.Empty<string>();
        return langage.Controle.Concat(langage.MotsCles).Concat(langage.Types).ToList();
    }

    // ------------------------------------------------------------------
    // Fabrication des définitions
    // ------------------------------------------------------------------

    private static IHighlightingDefinition? ObtenirDefinition(string extension)
    {
        extension = extension.ToLowerInvariant();

        if (!_parExtension.TryGetValue(extension, out var langage))
            return HighlightingManager.Instance.GetDefinitionByExtension(extension);

        var sombre = string.Equals(ThemeManager.ThemeActuel, "Sombre", StringComparison.OrdinalIgnoreCase);
        var cle = langage.Nom + (sombre ? "|sombre" : "|clair");
        if (_cache.TryGetValue(cle, out var enCache)) return enCache;

        IHighlightingDefinition? definition;
        try
        {
            var xshd = ConstruireXshd(langage, sombre ? _paletteSombre : _paletteClair);
            using var lecteur = XmlReader.Create(new StringReader(xshd));
            definition = HighlightingLoader.Load(lecteur, HighlightingManager.Instance);
        }
        catch (Exception)
        {
            // Définition invalide : on garde au moins la coloration d'origine d'AvalonEdit
            definition = HighlightingManager.Instance.GetDefinitionByExtension(extension);
        }

        _cache[cle] = definition;
        return definition;
    }

    private static string Echapper(string texte) => SecurityElement.Escape(texte) ?? "";

    private static string ConstruireXshd(Langage langage, Palette palette)
    {
        var sb = new StringBuilder();
        sb.Append("<SyntaxDefinition name=\"").Append(Echapper(langage.Nom))
          .Append("\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");

        AjouterCouleur(sb, "Commentaire", palette.Commentaire);
        AjouterCouleur(sb, "Chaine", palette.Chaine);
        AjouterCouleur(sb, "MotCle", palette.MotCle);
        AjouterCouleur(sb, "Controle", palette.Controle);
        AjouterCouleur(sb, "Type", palette.Type);
        AjouterCouleur(sb, "Nombre", palette.Nombre);
        AjouterCouleur(sb, "Prepro", palette.Prepro);
        AjouterCouleur(sb, "Variable", palette.Variable);
        AjouterCouleur(sb, "Tag", palette.MotCle);
        AjouterCouleur(sb, "Attribut", palette.Variable);

        sb.Append(langage.IgnorerCasse ? "<RuleSet ignoreCase=\"true\">" : "<RuleSet>");

        if (langage.Markup)
        {
            AjouterMarkup(sb);
        }
        else
        {
            // Les commentaires et les chaînes d'abord : un mot-clé écrit dans une chaîne n'est pas coloré
            if (langage.CommentaireBloc is { } bloc)
                AjouterSpan(sb, "Commentaire", bloc.Debut, bloc.Fin, multiligne: true, echappement: false);

            foreach (var debut in langage.CommentairesLigne)
                sb.Append("<Span color=\"Commentaire\" begin=\"").Append(Echapper(debut)).Append("\"/>");

            foreach (var chaine in langage.Chaines)
                AjouterSpan(sb, "Chaine", chaine.Debut, chaine.Fin, chaine.Multiligne, chaine.Echappement);

            foreach (var regle in langage.Regles)
                sb.Append("<Rule color=\"").Append(regle.Couleur).Append("\">").Append(Echapper(regle.Expression)).Append("</Rule>");

            AjouterMots(sb, "Controle", langage.Controle);
            AjouterMots(sb, "MotCle", langage.MotsCles);
            AjouterMots(sb, "Type", langage.Types);

            sb.Append("<Rule color=\"Nombre\">")
              .Append(Echapper(@"\b0[xX][0-9a-fA-F_]+\b|\b\d[\d_]*(\.\d+)?([eE][+-]?\d+)?[fFdDlLuU]*\b"))
              .Append("</Rule>");
        }

        sb.Append("</RuleSet></SyntaxDefinition>");
        return sb.ToString();
    }

    private static void AjouterCouleur(StringBuilder sb, string nom, string couleur) =>
        sb.Append("<Color name=\"").Append(nom).Append("\" foreground=\"").Append(couleur).Append("\"/>");

    private static void AjouterSpan(StringBuilder sb, string couleur, string debut, string fin, bool multiligne, bool echappement)
    {
        sb.Append("<Span color=\"").Append(couleur).Append('"').Append(multiligne ? " multiline=\"true\"" : "")
          .Append("><Begin>").Append(Echapper(debut)).Append("</Begin><End>").Append(Echapper(fin)).Append("</End>");

        // Un \ dans la chaîne protège le caractère suivant : "a\"b" reste une seule chaîne
        if (echappement)
            sb.Append("<RuleSet><Span begin=\"\\\\\" end=\".\"/></RuleSet>");

        sb.Append("</Span>");
    }

    private static void AjouterMots(StringBuilder sb, string couleur, string[] mots)
    {
        if (mots.Length == 0) return;
        sb.Append("<Keywords color=\"").Append(couleur).Append("\">");
        foreach (var mot in mots)
            sb.Append("<Word>").Append(Echapper(mot)).Append("</Word>");
        sb.Append("</Keywords>");
    }

    // HTML et XML : commentaires, balises avec leurs attributs
    private static void AjouterMarkup(StringBuilder sb)
    {
        AjouterSpan(sb, "Commentaire", "<!--", "-->", multiligne: true, echappement: false);

        sb.Append("<Span color=\"Tag\" multiline=\"true\"><Begin>").Append(Echapper(@"<[/?!]?[\w:.\-]+"))
          .Append("</Begin><End>").Append(Echapper(@"[/?]?>")).Append("</End><RuleSet>");
        AjouterSpan(sb, "Chaine", "\"", "\"", multiligne: false, echappement: false);
        AjouterSpan(sb, "Chaine", "'", "'", multiligne: false, echappement: false);
        sb.Append("<Rule color=\"Attribut\">").Append(Echapper(@"[\w:.\-]+(?=\s*=)")).Append("</Rule>");
        sb.Append("</RuleSet></Span>");
    }
}
