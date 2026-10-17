using System.Diagnostics;
using System.Text;
using Microsoft.Data.SqlClient;

namespace WuConnector.Core.Konfiguracja;

public sealed record WykrytaFirma(int FirmIde, string Nazwa, string Nip, string BazaDane, bool BazaIstnieje);

public sealed record WykrytaInstalacja(
    string Nazwa,
    string KatalogBin,
    string SerwerSql,
    string PrefiksBazy,
    string BazaBin,
    string? WersjaProgramu,
    string? WersjaBiblioteki,
    string? WersjaBazy,
    IReadOnlyList<WykrytaFirma> Firmy,
    string? Blad);

/// <summary>
/// Konfiguracja przez wykrywanie, nie przez wpisywanie (ARCHITEKTURA §3):
/// katalog → instalacje (BIN\RPlatnik.ini) → serwer i prefiks baz z [SQL] → firmy z FIRM → wersje.
/// Tylko odczyt; niczego nie zmienia ani w plikach R2, ani w bazie.
/// </summary>
public static class WykrywanieInstalacji
{
    /// <summary>Katalogi BIN z RPlatnik.ini pod wskazanym katalogiem (do 3 poziomow w glab).</summary>
    public static List<string> ZnajdzKatalogiBin(string katalog, int glebokosc = 3)
    {
        var wynik = new List<string>();
        void Szukaj(string k, int poziom)
        {
            if (File.Exists(Path.Combine(k, "RPlatnik.ini")) && Path.GetFileName(k).Equals("BIN", StringComparison.OrdinalIgnoreCase))
                wynik.Add(k.TrimEnd('\\') + "\\");
            if (poziom >= glebokosc) return;
            string[] pod;
            try { pod = Directory.GetDirectories(k); } catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return; }
            foreach (var d in pod) Szukaj(d, poziom + 1);
        }
        if (Directory.Exists(katalog)) Szukaj(katalog, 0);
        return wynik.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>[SQL] Serwer i Baza danych z RPlatnik.ini. Plik bywa w UTF-8 albo w ANSI — klucze sa ASCII.</summary>
    public static (string? Serwer, string? Prefiks) CzytajIni(string katalogBin)
    {
        string sciezka = Path.Combine(katalogBin, "RPlatnik.ini");
        string[] linie = File.ReadAllLines(sciezka, Encoding.UTF8);
        string? sekcja = null, serwer = null, baza = null;
        foreach (var surowa in linie)
        {
            var l = surowa.Trim();
            if (l.StartsWith('[') && l.EndsWith(']')) { sekcja = l[1..^1]; continue; }
            if (sekcja != "SQL") continue;
            int r = l.IndexOf('=');
            if (r <= 0) continue;
            string klucz = l[..r].Trim(), wartosc = l[(r + 1)..].Trim();
            if (klucz == "Serwer") serwer = wartosc;
            else if (klucz == "Baza danych") baza = wartosc;
        }
        return (serwer, baza);
    }

    public static async Task<WykrytaInstalacja> WykryjAsync(string katalogBin, CancellationToken ct = default)
    {
        string nazwa = Path.GetFileName(Path.GetDirectoryName(katalogBin.TrimEnd('\\'))) ?? katalogBin;
        string? wProg = Wersja(Path.Combine(katalogBin, "RPlatnik10.exe"));
        string? wBib = Wersja(Path.Combine(katalogBin, "R2PApi10.dll"));
        if (!File.Exists(Path.Combine(katalogBin, "RPlatnik.ini")))
            return new(nazwa, katalogBin, "", "", "", wProg, wBib, null, Array.Empty<WykrytaFirma>(),
                       "Nie ma pliku BIN\\RPlatnik.ini — katalog usunięty, przeniesiony albo niedostępny");

        var (serwer, prefiks) = CzytajIni(katalogBin);
        if (serwer is null || prefiks is null)
            return new(nazwa, katalogBin, serwer ?? "", prefiks ?? "", "", wProg, wBib, null, Array.Empty<WykrytaFirma>(),
                       "W RPlatnik.ini brak sekcji [SQL] z serwerem i baza");

        string bazaBin = prefiks.ToLowerInvariant() + "_bin";
        try
        {
            var cs = new SqlConnectionStringBuilder
            {
                DataSource = serwer, InitialCatalog = bazaBin, IntegratedSecurity = true,
                ConnectTimeout = 8, Encrypt = SqlConnectionEncryptOption.Optional, TrustServerCertificate = true,
                ApplicationName = "WuConnector-konfiguracja",
            }.ConnectionString;
            await using var c = new SqlConnection(cs);
            await c.OpenAsync(ct);

            string? wBazy = null;
            await using (var cmd = new SqlCommand("SELECT TOP 1 CONCAT(VER_MAJOR, '.', VER_MINOR, '.', VER_COMPILE) FROM VERSION ORDER BY X_I DESC", c))
                wBazy = (string?)await cmd.ExecuteScalarAsync(ct);

            var firmy = new List<WykrytaFirma>();
            await using (var cmd = new SqlCommand("""
                SELECT f.X_I, f.Nazwa, REPLACE(REPLACE(LTRIM(RTRIM(f.NIP)), '-', ''), ' ', ''),
                       CASE WHEN DB_ID(@p + '_dane_' + CAST(f.X_I AS varchar(10))) IS NULL THEN 0 ELSE 1 END
                FROM FIRM f ORDER BY f.X_I
                """, c))
            {
                cmd.Parameters.AddWithValue("@p", prefiks.ToLowerInvariant());
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    int id = r.GetInt32(0);
                    firmy.Add(new WykrytaFirma(id, r.IsDBNull(1) ? "" : r.GetString(1).Trim(), r.IsDBNull(2) ? "" : r.GetString(2),
                                               $"{prefiks.ToLowerInvariant()}_dane_{id}", r.GetInt32(3) == 1));
                }
            }
            return new(nazwa, katalogBin, serwer, prefiks, bazaBin, wProg, wBib, wBazy, firmy, null);
        }
        catch (SqlException e)
        {
            return new(nazwa, katalogBin, serwer, prefiks, bazaBin, wProg, wBib, null, Array.Empty<WykrytaFirma>(),
                       "Brak polaczenia z baza: " + e.Message);
        }
    }

    static string? Wersja(string plik) => File.Exists(plik) ? FileVersionInfo.GetVersionInfo(plik).FileVersion : null;
}
