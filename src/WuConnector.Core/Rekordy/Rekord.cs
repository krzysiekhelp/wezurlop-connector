using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WuConnector.Core.Rekordy;

public static class Zbiory
{
    public const string Firma = "firma";
    public const string Pracownik = "pracownik";
    public const string Zatrudnienie = "zatrudnienie";
    public const string Limit = "limit";
    public const string Nieobecnosc = "nieobecnosc";
    public const string Slownik = "slownik";
    public const string Kalendarz = "kalendarz";

    /// <summary>Kolejnosc wysylki: najpierw to, na co wskazuja inne rekordy.</summary>
    public static readonly string[] Wszystkie = { Firma, Slownik, Kalendarz, Pracownik, Zatrudnienie, Limit, Nieobecnosc };
}

/// <summary>
/// Rekord wymiany: tresc JSON w ustalonej kolejnosci pol + suma SHA-256 tej tresci.
/// Suma liczona z kanonicznego JSON bez pola "suma" — ta sama tresc zawsze daje ta sama sume.
/// </summary>
public sealed class Rekord
{
    static readonly JsonSerializerOptions Zapis = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // polskie znaki bez \uXXXX
        WriteIndented = false,
    };

    public string Zbior { get; }
    public string Id { get; }
    public string Suma { get; }

    /// <summary>Tresc do wysylki — z "id" na poczatku i "suma" na drugim miejscu.</summary>
    public JsonObject Tresc { get; }

    /// <summary>
    /// Metadane dla bezpiecznika usuniec, poza trescia i suma: id pracownika, do ktorego nalezy rekord.
    /// Rekord znikajacy razem ze swoim pracownikiem (zwolnienie) to usuniecie wyjasnione.
    /// </summary>
    public string? Grupa { get; }

    /// <summary>Metadane: koniec okresu (nieobecnosc). Rekord konczacy sie przed oknem wysylki znika legalnie.</summary>
    public DateTime? Koniec { get; }

    public Rekord(string zbior, string id, IEnumerable<KeyValuePair<string, JsonNode?>> pola,
                  string? grupa = null, DateTime? koniec = null)
    {
        Zbior = zbior;
        Id = id;
        Grupa = grupa;
        Koniec = koniec;

        var bezSumy = new JsonObject { ["id"] = id };
        foreach (var (k, v) in pola) bezSumy[k] = v;
        Suma = "sha256:" + Hex(SHA256.HashData(Encoding.UTF8.GetBytes(bezSumy.ToJsonString(Zapis))));

        Tresc = new JsonObject { ["id"] = id, ["suma"] = Suma };
        foreach (var (k, v) in bezSumy)
            if (k != "id") Tresc[k] = v?.DeepClone();
    }

    public string Json() => Tresc.ToJsonString(Zapis);

    static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();
}
