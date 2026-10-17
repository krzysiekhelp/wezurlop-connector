using System.Security.Cryptography;
using System.Text.Json;

namespace WuConnector.Core.Biblioteki;

/// <param name="WersjaBazy">Wersja schematu bazy R2 (BIN.VERSION), np. "10.10.33".</param>
/// <param name="WersjaPliku">FileVersion biblioteki, np. "10.1.0.33".</param>
/// <param name="Plik">Sciezka wzgledem adresu manifestu, np. "10.1.0.33/R2PApi10.dll".</param>
public sealed record PozycjaBiblioteki(string WersjaBazy, string WersjaPliku, string Sha256, long Rozmiar, string Plik);

/// <summary>
/// Lista bibliotek R2 hostowanych przez nas (wezurlop.pl) — w niezmienionej postaci, z podpisem producenta.
/// Manifest podpisujemy MY (ECDSA P-256): podpis producenta chroni przed obcym kodem, a nasz manifest —
/// przed podsunieciem innej, choc prawdziwej wersji biblioteki niz pasujaca do bazy klienta.
/// </summary>
public sealed record ManifestBibliotek(int Format, DateTimeOffset Wydano, List<PozycjaBiblioteki> Biblioteki)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public PozycjaBiblioteki? DlaBazy(string wersjaBazy)
        => Biblioteki.FirstOrDefault(b => b.WersjaBazy == wersjaBazy);

    /// <summary>
    /// Zwraca manifest tylko, gdy podpis sie zgadza. Podpis liczony z DOKLADNYCH bajtow pliku —
    /// bez ponownej serializacji, zeby zmiana bialych znakow nie psula weryfikacji.
    /// </summary>
    public static ManifestBibliotek? Zweryfikuj(byte[] tresc, string podpisBase64, string kluczPublicznyPem)
    {
        byte[] podpis;
        try { podpis = Convert.FromBase64String(podpisBase64.Trim()); } catch (FormatException) { return null; }
        using var ec = ECDsa.Create();
        ec.ImportFromPem(kluczPublicznyPem);
        if (!ec.VerifyData(tresc, podpis, HashAlgorithmName.SHA256)) return null;
        var m = JsonSerializer.Deserialize<ManifestBibliotek>(tresc, Json);
        return m is { Format: 1 } ? m : null;
    }

    public static string Podpisz(byte[] tresc, string kluczPrywatnyPem)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(kluczPrywatnyPem);
        return Convert.ToBase64String(ec.SignData(tresc, HashAlgorithmName.SHA256));
    }

    /// <summary>
    /// Heurystyka dla biblioteki juz lezacej w BIN (bez manifestu): baza 10.10.33 ↔ plik 10.1.0.33
    /// — zgodny numer glowny i numer kompilacji. Zaobserwowane na .32 i .33; wiazacy jest manifest.
    /// </summary>
    public static bool PasujeDoBazy(string wersjaPliku, string wersjaBazy)
    {
        if (!Version.TryParse(wersjaPliku, out var p)) return false;
        var b = wersjaBazy.Split('.');
        return b.Length == 3 && int.TryParse(b[0], out int maj) && int.TryParse(b[2], out int komp)
               && p.Major == maj && p.Revision == komp;
    }
}
