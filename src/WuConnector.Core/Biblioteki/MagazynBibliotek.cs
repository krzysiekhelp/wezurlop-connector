using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace WuConnector.Core.Biblioteki;

/// <param name="Katalog">Katalog z R2PApi10.dll do SetDllDirectory; null = brak biblioteki.</param>
/// <param name="Zrodlo">konfiguracja | BIN | magazyn | pobrana | BIN (niezgodna wersja)</param>
public sealed record WybranaBiblioteka(string? Katalog, string Zrodlo, string? WersjaPliku, string? Uwaga);

public interface IWyborBiblioteki
{
    Task<WybranaBiblioteka> WybierzAsync(string katalogBin, string? katalogZKonfiguracji, string? wersjaBazy, CancellationToken ct);
}

/// <summary>
/// Dobiera R2PApi10.dll do wersji bazy (ARCHITEKTURA §7). Kolejnosc:
///   1. katalog wskazany recznie przez administratora,
///   2. BIN instalacji, jesli ma pasujaca wersje,
///   3. magazyn connectora (ProgramData\…\r2api\&lt;wersja&gt;\),
///   4. pobranie z serwera bibliotek: podpisany manifest → SHA-256 → podpis producenta → magazyn,
///   5. biblioteka z BIN w niepasujacej wersji — z ostrzezeniem (biblioteka .32 poprawnie zapisywala do bazy .33).
/// Pliki .ini, .lic, .prt zawsze z instalacji R2 — magazyn trzyma wylacznie biblioteke.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MagazynBibliotek : IWyborBiblioteki
{
    public const string NazwaPliku = "R2PApi10.dll";

    readonly string _katalog;
    readonly Uri? _adresManifestu;
    readonly string _kluczPubliczny;
    readonly HttpClient _http;
    readonly ILogger _log;
    readonly Func<string, WynikPodpisu> _podpis;
    readonly SemaphoreSlim _zamek = new(1, 1);

    public MagazynBibliotek(string katalog, Uri? adresManifestu, ILogger log, string? kluczPubliczny = null,
                            HttpMessageHandler? http = null, Func<string, WynikPodpisu>? sprawdzPodpis = null)
    {
        _katalog = katalog;
        _adresManifestu = adresManifestu;
        _kluczPubliczny = kluczPubliczny ?? KluczManifestu.Publiczny;
        _http = http is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(5) } : new HttpClient(http, false) { Timeout = TimeSpan.FromMinutes(5) };
        _log = log;
        _podpis = sprawdzPodpis ?? PodpisProducenta.Sprawdz;
    }

    public async Task<WybranaBiblioteka> WybierzAsync(string katalogBin, string? katalogZKonfiguracji, string? wersjaBazy, CancellationToken ct)
    {
        // 1. Reczne wskazanie wygrywa — administrator wie, co robi (np. biblioteka z innej instalacji).
        if (!string.IsNullOrWhiteSpace(katalogZKonfiguracji))
            return new(katalogZKonfiguracji, "konfiguracja", Wersja(Path.Combine(katalogZKonfiguracji, NazwaPliku)), null);

        string wBin = Path.Combine(katalogBin, NazwaPliku);
        string? wersjaBin = Wersja(wBin);

        if (wersjaBazy is null)   // nie znamy wersji bazy — bierzemy, co jest
            return wersjaBin is not null ? new(katalogBin, "BIN", wersjaBin, null) : Brak(null);

        // 2. BIN z pasujaca wersja — nic nie pobieramy.
        if (wersjaBin is not null && ManifestBibliotek.PasujeDoBazy(wersjaBin, wersjaBazy))
            return new(katalogBin, "BIN", wersjaBin, null);

        await _zamek.WaitAsync(ct);
        try
        {
            // 3. Magazyn.
            var zMagazynu = ZMagazynu(wersjaBazy);
            if (zMagazynu is not null) return zMagazynu;

            // 4. Pobranie.
            string? blad = null;
            if (_adresManifestu is not null)
            {
                try
                {
                    var pobrana = await PobierzAsync(wersjaBazy, ct);
                    if (pobrana is not null) return pobrana;
                    blad = $"serwer bibliotek nie ma wersji dla bazy {wersjaBazy}";
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    blad = "pobranie biblioteki nieudane: " + e.Message;
                    _log.LogWarning("Biblioteka R2 dla bazy {W}: {Blad}", wersjaBazy, blad);
                }
            }

            // 5. Biblioteka z BIN w innej wersji — lepiej zapisywac z ostrzezeniem niz wcale.
            if (wersjaBin is not null)
                return new(katalogBin, "BIN (niezgodna wersja)", wersjaBin,
                           $"biblioteka {wersjaBin} nie odpowiada bazie {wersjaBazy}" + (blad is null ? "" : $"; {blad}"));
            return Brak($"brak biblioteki API dla bazy {wersjaBazy}" + (blad is null ? "" : $"; {blad}"));
        }
        finally { _zamek.Release(); }
    }

    WybranaBiblioteka? ZMagazynu(string wersjaBazy)
    {
        if (!Directory.Exists(_katalog)) return null;
        foreach (var k in Directory.GetDirectories(_katalog))
        {
            string plik = Path.Combine(k, NazwaPliku);
            if (Wersja(plik) is string w && ManifestBibliotek.PasujeDoBazy(w, wersjaBazy))
            {
                // Podpis sprawdzamy przy kazdym uzyciu — plik w ProgramData moglby zostac podmieniony.
                var p = _podpis(plik);
                if (p.Poprawny) return new(k, "magazyn", w, null);
                _log.LogError("Biblioteka w magazynie {K} odrzucona: {B}", k, p.Blad);
            }
        }
        return null;
    }

    async Task<WybranaBiblioteka?> PobierzAsync(string wersjaBazy, CancellationToken ct)
    {
        byte[] tresc = await _http.GetByteArrayAsync(_adresManifestu, ct);
        string podpis = await _http.GetStringAsync(new Uri(_adresManifestu + ".sig"), ct);
        var m = ManifestBibliotek.Zweryfikuj(tresc, podpis, _kluczPubliczny)
                ?? throw new InvalidDataException("manifest bibliotek ma nieprawidlowy podpis");

        // Ochrona przed cofnieciem: manifest starszy niz juz widziany jest odrzucany.
        string plikDaty = Path.Combine(_katalog, "manifest-wydano.txt");
        if (File.Exists(plikDaty) && DateTimeOffset.TryParse(await File.ReadAllTextAsync(plikDaty, ct), out var widziany) && m.Wydano < widziany)
            throw new InvalidDataException($"manifest z {m.Wydano:yyyy-MM-dd} starszy niz juz widziany ({widziany:yyyy-MM-dd})");

        var poz = m.DlaBazy(wersjaBazy);
        if (poz is null) return null;

        Directory.CreateDirectory(_katalog);
        string docelowy = Path.Combine(_katalog, poz.WersjaPliku);
        string tymczasowy = Path.Combine(_katalog, $".pobieranie-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tymczasowy);
        try
        {
            string plik = Path.Combine(tymczasowy, NazwaPliku);
            await using (var s = await _http.GetStreamAsync(new Uri(_adresManifestu!, poz.Plik), ct))
            await using (var f = File.Create(plik))
                await s.CopyToAsync(f, ct);

            string sha;
            await using (var odczyt = File.OpenRead(plik)) sha = Convert.ToHexString(await SHA256.HashDataAsync(odczyt, ct));
            if (!sha.Equals(poz.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"suma SHA-256 pobranego pliku nie zgadza sie z manifestem");
            var p = _podpis(plik);
            if (!p.Poprawny) throw new InvalidDataException("pobrany plik odrzucony: " + p.Blad);
            if (Wersja(plik) != poz.WersjaPliku) throw new InvalidDataException("wersja pobranego pliku inna niz w manifescie");

            if (Directory.Exists(docelowy)) Directory.Delete(docelowy, true);
            Directory.Move(tymczasowy, docelowy);
            await File.WriteAllTextAsync(plikDaty, m.Wydano.ToString("O"), ct);

            _log.LogInformation("Pobrano biblioteke R2 {W} dla bazy {B} (SHA-256 {Sha}, podpis: {Podmiot})",
                                poz.WersjaPliku, wersjaBazy, sha[..16], p.Podmiot);
            return new(docelowy, "pobrana", poz.WersjaPliku, null);
        }
        finally
        {
            if (Directory.Exists(tymczasowy)) try { Directory.Delete(tymczasowy, true); } catch (IOException) { }
        }
    }

    static WybranaBiblioteka Brak(string? uwaga) => new(null, "brak", null, uwaga ?? "brak biblioteki API");

    static string? Wersja(string plik) => File.Exists(plik) ? FileVersionInfo.GetVersionInfo(plik).FileVersion : null;
}
