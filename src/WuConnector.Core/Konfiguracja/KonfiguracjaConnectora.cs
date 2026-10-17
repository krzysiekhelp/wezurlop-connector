namespace WuConnector.Core.Konfiguracja;

/// <summary>
/// Konfiguracja connectora (ARCHITEKTURA-agenta.md §3): jeden serwer i klucz, pod nim instalacje R2 i firmy.
/// Sekrety (klucz, hasla, sekret osobaId) w pliku sa szyfrowane DPAPI — tu juz odszyfrowane.
/// </summary>
public sealed class KonfiguracjaConnectora
{
    public required SerwerTenanta Serwer { get; init; }
    public required List<InstalacjaR2> Instalacje { get; init; }

    /// <summary>Sekret instalacji do osobaId (min. 16 bajtow). Staly przez cale zycie connectora u klienta.</summary>
    public required byte[] SekretOsoby { get; init; }

    public required string SciezkaWorkera { get; init; }
    public required string SciezkaStanu { get; init; }

    /// <summary>Magazyn bibliotek R2 (ProgramData\…\r2api). Null = bez magazynu, biblioteka tylko z BIN/konfiguracji.</summary>
    public string? KatalogBibliotek { get; init; }

    /// <summary>Podpisany manifest bibliotek na naszym serwerze (np. https://wezurlop.pl/r2api/manifest.json). Null = bez pobierania.</summary>
    public Uri? AdresManifestuBibliotek { get; init; }

    /// <summary>Twardy limit czasu jednego zadania zapisu (proces roboczy).</summary>
    public TimeSpan LimitZadania { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Harmonogram synchronizacji (min. co 5 min). Serwer moze odstep wydluzyc, nigdy skrocic.</summary>
    public Harmonogram Harmonogram { get; init; } = Harmonogram.Domyslny();

    /// <summary>Twarde minimum niezaleznie od harmonogramu — limit hostingu klienta (API-TENANTA §4).</summary>
    public TimeSpan MinimalnyInterwal { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class SerwerTenanta
{
    public required Uri Adres { get; init; }
    public required string Klucz { get; init; }
}

public sealed class InstalacjaR2
{
    public required string Nazwa { get; init; }
    public required string KatalogBin { get; init; }

    /// <summary>Katalog biblioteki z magazynu wersji; null = biblioteka z KatalogBin.</summary>
    public string? KatalogDll { get; init; }

    public required string SerwerSql { get; init; }
    public required string BazaBin { get; init; }
    public string? LoginSql { get; init; }
    public string? HasloSql { get; init; }

    public required string LoginR2 { get; init; }
    public required string HasloR2 { get; init; }

    /// <summary>USERS.X_I konta connectora — do rozpoznawania wlasnych wpisow (X_F7). Z testu logowania.</summary>
    public int? UzytkownikR2Id { get; init; }

    public string? WersjaR2 { get; init; }

    public required List<FirmaR2> Firmy { get; init; }
}

public sealed class FirmaR2
{
    public required int FirmIde { get; init; }
    public required string BazaDane { get; init; }
    public bool Aktywna { get; init; } = true;
}
