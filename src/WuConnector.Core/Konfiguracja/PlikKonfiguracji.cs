using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WuConnector.Core.Konfiguracja;

/// <summary>
/// Plik konfiguracji connectora (JSON). Sekrety — klucz API, hasla, sekret osobaId — zapisane jako "dpapi:&lt;base64&gt;"
/// (DPAPI, zakres MASZYNY: zapisuje okno w sesji uzytkownika, czyta usluga jako LocalSystem).
/// Wartosc bez prefiksu jest przyjmowana, ale usluga ostrzega w dzienniku.
/// Domyslnie: C:\ProgramData\WU_Connector\konfiguracja.json, ACL: SYSTEM + Administratorzy.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class PlikKonfiguracji
{
    public static string DomyslnyKatalog =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WU_Connector");

    public required string AdresSerwera { get; set; }
    public required string KluczApi { get; set; }
    public required string SekretOsoby { get; set; }
    public string? SciezkaWorkera { get; set; }
    public string? SciezkaStanu { get; set; }
    public int LimitZadaniaSekund { get; set; } = 60;

    /// <summary>Kiedy i jak czesto synchronizowac. Brak w pliku = domyslny (Pn–Pt 06–20 co 5 min, poza tym co 60 min).</summary>
    public Harmonogram? Harmonogram { get; set; }
    public string? KatalogBibliotek { get; set; }
    public string? AdresManifestuBibliotek { get; set; }
    public required List<InstalacjaWPliku> Instalacje { get; set; }

    public sealed class InstalacjaWPliku
    {
        public required string Nazwa { get; set; }
        public required string KatalogBin { get; set; }
        public string? KatalogDll { get; set; }
        public required string SerwerSql { get; set; }
        public required string BazaBin { get; set; }
        public string? LoginSql { get; set; }
        public string? HasloSql { get; set; }
        public required string LoginR2 { get; set; }
        public required string HasloR2 { get; set; }
        public int? UzytkownikR2Id { get; set; }
        public string? WersjaR2 { get; set; }
        public required List<FirmaR2> Firmy { get; set; }
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public void Zapisz(string sciezka)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sciezka))!);
        string tmp = sciezka + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json), new UTF8Encoding(false));
        File.Move(tmp, sciezka, overwrite: true);   // atomowo — usluga nigdy nie przeczyta polowy pliku
    }

    /// <summary>Odszyfrowuje wartosc "dpapi:…" (albo zwraca jawna) — dla okna, ktore pokazuje np. adres i testuje logowanie.</summary>
    public static string Jawny(string wartosc) => wartosc.StartsWith("dpapi:", StringComparison.Ordinal) ? Odszyfruj(wartosc) : wartosc;

    public static PlikKonfiguracji Wczytaj(string sciezka)
        => JsonSerializer.Deserialize<PlikKonfiguracji>(File.ReadAllText(sciezka, Encoding.UTF8), Json)
           ?? throw new InvalidDataException("Pusty plik konfiguracji: " + sciezka);

    /// <summary>Odszyfrowana konfiguracja dla Core. Ostrzezenia o sekretach niezaszyfrowanych — do dziennika.</summary>
    public KonfiguracjaConnectora Rozwin(string katalogPliku, List<string> ostrzezenia)
    {
        string Sekret(string wartosc, string nazwa)
        {
            if (wartosc == "") return "";
            if (wartosc.StartsWith("dpapi:", StringComparison.Ordinal))
                try { return Odszyfruj(wartosc); }
                catch (Exception e) when (e is CryptographicException or FormatException)
                {
                    // Sekrety szyfrujemy kluczem maszyny — plik przeniesiony z innego komputera jest tu nieczytelny.
                    throw new InvalidOperationException(
                        $"sekret '{nazwa}' zostal zaszyfrowany na innej maszynie (albo jest uszkodzony). " +
                        (nazwa == "sekretOsoby"
                            ? "Przenies go z poprzedniej maszyny: --sekret-eksport tam, --sekret-import tutaj. " +
                              "Nowy sekret zmieniłby identyfikatory osob i zalozyl je w aplikacji po raz drugi."
                            : "Wpisz go ponownie w oknie connectora albo przez --zaszyfruj."), e);
                }
            ostrzezenia.Add($"sekret '{nazwa}' zapisany w pliku bez szyfrowania — zaszyfruj go (--zaszyfruj)");
            return wartosc;
        }

        byte[] sekretOsoby = Convert.FromBase64String(Sekret(SekretOsoby, "sekretOsoby"));
        return new KonfiguracjaConnectora
        {
            Serwer = new SerwerTenanta { Adres = new Uri(AdresSerwera), Klucz = Sekret(KluczApi, "kluczApi") },
            SekretOsoby = sekretOsoby,
            SciezkaWorkera = SciezkaWorkera ?? Path.Combine(AppContext.BaseDirectory, "worker", "WuConnector.Worker.exe"),
            SciezkaStanu = SciezkaStanu ?? Path.Combine(katalogPliku, "stan.db"),
            LimitZadania = TimeSpan.FromSeconds(LimitZadaniaSekund),
            Harmonogram = Harmonogram ?? Konfiguracja.Harmonogram.Domyslny(),
            KatalogBibliotek = KatalogBibliotek ?? Path.Combine(katalogPliku, "r2api"),
            AdresManifestuBibliotek = string.IsNullOrWhiteSpace(AdresManifestuBibliotek) ? null : new Uri(AdresManifestuBibliotek),
            // Okno zapisuje sekcjami — instalacja moze byc zapisana niekompletna. Taka jest pomijana, reszta dziala.
            Instalacje = Instalacje.Where(i =>
            {
                // Konfiguracja sprzed usuniecia logowania zintegrowanego: usluga laczy sie wtedy jako SYSTEM,
                // a to konto do baz R2 dostepu nie ma. Dzialac moze (jesli ktos mu je nadal), ale warto wiedziec.
                if (i.Firmy.Any(f => f.Aktywna) && string.IsNullOrWhiteSpace(i.LoginSql))
                    ostrzezenia.Add($"instalacja {i.Nazwa} czyta bazę kontem usługi (SYSTEM) — załóż konto SQL w oknie konfiguracji");
                if (Braki(i) is not string b) return true;
                ostrzezenia.Add($"instalacja {i.Nazwa} niekompletna (brak: {b}) — pominięta do czasu uzupełnienia");
                return false;
            }).Select(i => new InstalacjaR2
            {
                Nazwa = i.Nazwa,
                KatalogBin = i.KatalogBin,
                KatalogDll = i.KatalogDll,
                SerwerSql = i.SerwerSql,
                BazaBin = i.BazaBin,
                LoginSql = i.LoginSql,
                HasloSql = i.HasloSql is null ? null : Sekret(i.HasloSql, $"{i.Nazwa}.hasloSql"),
                LoginR2 = i.LoginR2,
                HasloR2 = Sekret(i.HasloR2, $"{i.Nazwa}.hasloR2"),
                UzytkownikR2Id = i.UzytkownikR2Id,
                WersjaR2 = i.WersjaR2,
                Firmy = i.Firmy,
            }).ToList(),
        };
    }

    /// <summary>Czego brakuje aktywnej instalacji do pracy (null = kompletna albo bez aktywnych firm).</summary>
    public static string? Braki(InstalacjaWPliku i)
    {
        if (!i.Firmy.Any(f => f.Aktywna)) return null;
        var b = new List<string>();
        if (string.IsNullOrWhiteSpace(i.LoginR2)) b.Add("login R2");
        if (string.IsNullOrEmpty(i.HasloR2)) b.Add("hasło R2");
        if (i.LoginSql is not null && string.IsNullOrEmpty(i.HasloSql)) b.Add("hasło SQL");
        return b.Count == 0 ? null : string.Join(", ", b);
    }

    public static string Zaszyfruj(string jawny)
        => "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(jawny), Entropia, DataProtectionScope.LocalMachine));

    static string Odszyfruj(string wartosc)
        => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(wartosc[6..]), Entropia, DataProtectionScope.LocalMachine));

    /// <summary>Dodatkowa entropia — inny program na tej maszynie nie odszyfruje sekretu samym DPAPI maszyny.</summary>
    // Stala wewnetrzna — NIE zmieniac przy zmianie nazwy produktu: uniewazniloby to zaszyfrowane hasla w konfiguracjach.
    static readonly byte[] Entropia = Encoding.UTF8.GetBytes("wezurlop-connector/v1");

    /// <summary>Nowy losowy sekret osobaId (32 bajty, base64) — raz, przy wdrozeniu.</summary>
    public static string NowySekretOsoby() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Odcisk sekretu osobaId: 8 znakow szesnastkowych z SHA-256. Pozwala porownac sekret miedzy maszynami
    /// (np. po przenosinach) bez pokazywania go — te same osoby dostana te same osobaId tylko przy tym samym odcisku.
    /// </summary>
    public static string OdciskSekretu(string sekretBase64) => OdciskSekretu(Convert.FromBase64String(Jawny(sekretBase64)));

    /// <summary>Ten sam odcisk liczony z samych bajtow sekretu — wysylany tenantowi w `start` (odciskTozsamosci).</summary>
    public static string OdciskSekretu(byte[] sekret) => Odcisk.Sekretu(sekret);
}

/// <summary>
/// Odcisk sekretu z samych bajtow: SHA-256, nic wiecej. Osobno od <see cref="PlikKonfiguracji"/>, bo ta
/// klasa siega po DPAPI i jest windowsowa — a odcisk liczy takze cykl, ktory o platformie nic nie zaklada.
/// </summary>
public static class Odcisk
{
    public static string Sekretu(byte[] sekret) => Convert.ToHexString(SHA256.HashData(sekret)).ToLowerInvariant()[..8];
}


