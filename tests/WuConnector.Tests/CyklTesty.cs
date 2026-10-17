using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WuConnector.Contracts;
using WuConnector.Core.Cykl;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Odczyt;
using WuConnector.Core.Zadania;

namespace WuConnector.Tests;

sealed class ZrodloTestowe : IZrodloDanych
{
    public SuroweDane Dane { get; set; }
    public bool Awaria { get; set; }
    public ZrodloTestowe(SuroweDane d) { Dane = d; }
    public Task<SuroweDane> CzytajAsync(int rokOd, int rokDo, CancellationToken ct)
        => Awaria ? throw new InvalidOperationException("SQL niedostepny") : Task.FromResult(Dane);
}

sealed class WykonawcaTestowy : IWykonawcaZadan
{
    public List<ZadanieWorkera> Wywolania { get; } = new();
    public Func<ZadanieWorkera, WynikWorkera> Odpowiedz { get; set; } = z => new WynikWorkera
    {
        Status = Statusy.Zapisano,
        UzytkownikId = 6,
        Zapisy = { new ZapisR2 { Id = 12330, DataOd = z.DataOd, DataDo = z.DataDo, Dni = 2, Minuty = 960 } },
    };
    public Task<WynikWorkera> WykonajAsync(ZadanieWorkera z, CancellationToken ct) { Wywolania.Add(z); return Task.FromResult(Odpowiedz(z)); }
}

public class CyklTesty : IClassFixture<AtrapaFixture>, IDisposable
{
    readonly AtrapaFixture _f;
    readonly string _stan = Path.Combine(Path.GetTempPath(), $"wu-cykl-{Guid.NewGuid():N}.db");
    readonly string _nip = LosowyNip();

    /// <summary>Losowy NIP z poprawna suma kontrolna — kazdy test z osobna firma w atrapie.</summary>
    static string LosowyNip()
    {
        int[] w = { 6, 5, 7, 2, 3, 4, 5, 6, 7 };
        while (true)
        {
            var c = Enumerable.Range(0, 9).Select(i => i == 0 ? Random.Shared.Next(1, 10) : Random.Shared.Next(10)).ToArray();
            int k = c.Select((d, i) => d * w[i]).Sum() % 11;
            if (k < 10) return string.Concat(c) + k;
        }
    }
    readonly WykonawcaTestowy _wyk = new();
    readonly ZrodloTestowe _zr;

    public CyklTesty(AtrapaFixture f)
    {
        _f = f;
        lock (_f.Stan.Zamek) _f.Stan.DozwoloneNip.Add(_nip);
        _zr = new ZrodloTestowe(Dane(_nip));
    }

    public void Dispose()
    {
        foreach (var p in new[] { _stan, _stan + "-wal", _stan + "-shm" }) try { File.Delete(p); } catch (IOException) { }
    }

    static SuroweDane Dane(string nip, int urlopow = 3) => new(
        new FirmaWiersz(nip, "FIRMA TESTOWA"),
        new[]
        {
            new PracownikWiersz(207, "KOWALSKI", "JAN", "12", false, null, false, true, false, false, "90010112345"),
            new PracownikWiersz(300, "NOWAK", "ANNA", null, true, new DateTime(2020, 1, 31), false, true, false, false, "85020254321"),
        },
        new[]
        {
            new ZatrudnienieWiersz(207, 207, true, true, 2, 1, 1, new(2020, 1, 1), null),
            new ZatrudnienieWiersz(300, 300, true, true, 2, 1, 1, new(2015, 1, 1), new(2020, 1, 31)),
        },
        Enumerable.Range(1, urlopow).Select(i => new UrlopWiersz(10000 + i, 207, false, 0, new DateTime(2026, 2, 2).AddDays(i * 7),
                    new DateTime(2026, 2, 3).AddDays(i * 7), 2, 960, null, 18600 - i * 960, false, null, null, 3, new(2026, 1, 1), null))
            .Append(new UrlopWiersz(9000, 207, true, 0, new(2026, 1, 1), null, null, null, 12480, 18600, false, null, null, 3, null, null))
            .ToList(),
        Array.Empty<KadroweWiersz>(), Array.Empty<ZasilekWiersz>(),
        new[] { new SlownikWiersz("urlop", 0, "urlop wypoczynkowy"), new SlownikWiersz("urlop", 5, "urlop wyp.na żądanie") },
        new[] { new KalendarzWiersz(2, "Główny", true, 480, 480) },
        new[] { new LataKalWiersz(2, 2026, new byte[]?[12]) },
        DateTime.Now);

    Connector Connector(string? klucz = null) => new(
        new KonfiguracjaConnectora
        {
            Serwer = new SerwerTenanta { Adres = _f.Adres, Klucz = klucz ?? AtrapaFixture.Klucz },
            Instalacje = new()
            {
                new InstalacjaR2
                {
                    Nazwa = "test", KatalogBin = @"C:\R2\BIN\", SerwerSql = ".", BazaBin = "b", LoginR2 = "WNIOSKI_DEMO", HasloR2 = "x",
                    WersjaR2 = "10.10.33", Firmy = new() { new FirmaR2 { FirmIde = 1, BazaDane = "d" } },
                },
            },
            SekretOsoby = new byte[32],
            SciezkaWorkera = "nieuzywany.exe",
            SciezkaStanu = _stan,
        },
        "0.1.0-test", NullLogger.Instance, _wyk, (_, _) => _zr, czekaj: (_, _) => Task.CompletedTask, dzis: () => new DateTime(2026, 9, 19));

    async Task<JsonObject> StanAtrapy()
        => (await _f.Sterowanie.GetFromJsonAsync<JsonObject>($"/_atrapa/stan?nip={_nip}"))!;

    Task DodajZadanie(object z) => _f.Sterowanie.PostAsJsonAsync("/_atrapa/zadanie", new { nip = _nip, zadanie = z });

    static object Zapis(string id) => new
    {
        requestId = id, operacja = "zapis", zatrudnienieId = "207", rodzaj = new { zrodlo = "urlop", kod = 0 },
        dataOd = "2026-12-14", dataDo = "2026-12-15", zapisy = Array.Empty<object>(),
    };

    [Fact]
    public async Task Pierwszy_cykl_wysyla_wszystko_drugi_nic()
    {
        using var c = Connector();
        var w1 = await c.WykonajCyklAsync(default);
        var f1 = Assert.Single(w1.Firmy);
        Assert.Equal(WynikFirmy.Ok, f1.Wynik);
        Assert.Equal(1, f1.Wyslano["pracownik"]);         // Nowak zwolniony — poza zakresem
        Assert.Equal(3, f1.Wyslano["nieobecnosc"]);

        var s = await StanAtrapy();
        Assert.Equal(3, (int)s["rekordy"]!["nieobecnosc"]!);
        Assert.Equal(0, (int)s["pominietePola"]!);

        var w2 = await c.WykonajCyklAsync(default);
        Assert.Empty(Assert.Single(w2.Firmy).Wyslano);
        Assert.True(w2.Nastepny >= TimeSpan.FromMinutes(Harmonogram.MinimalnyOdstepMinut));   // harmonogram: min. 5 min
    }

    [Fact]
    public async Task Usuniecie_nieobecnosci_w_R2_usuwa_ja_u_tenanta()
    {
        using var c = Connector();
        await c.WykonajCyklAsync(default);
        _zr.Dane = Dane(_nip, urlopow: 2);   // trzeci urlop usuniety przez kadry

        var w = Assert.Single((await c.WykonajCyklAsync(default)).Firmy);
        Assert.Equal(1, w.Usuniete["nieobecnosc"]);
        Assert.Equal(1, w.Wyslano["limit"]);   // saldo sie zmienilo
        Assert.Equal(2, (int)(await StanAtrapy())["rekordy"]!["nieobecnosc"]!);
    }

    [Fact]
    public async Task Zadanie_zapisu_wykonane_i_wynik_odeslany()
    {
        await DodajZadanie(Zapis("wu-C-1"));
        using var c = Connector();
        var w = Assert.Single((await c.WykonajCyklAsync(default)).Firmy);

        var zw = Assert.Single(_wyk.Wywolania);
        Assert.Equal(207, zw.PracownikId);
        Assert.Equal("wu-C-1", zw.RequestId);
        Assert.Equal("WNIOSKI_DEMO", zw.Login);

        var z = Assert.Single((JsonArray)(await StanAtrapy())["zadania"]!)!;
        Assert.Equal("Zamkniete", (string)z["stan"]!);
        var wynik = JsonNode.Parse((string)z["wynik"]!)!;
        Assert.Equal("zapisano", (string)wynik["status"]!);
        Assert.Equal("urlop:12330", (string)wynik["zapisy"]![0]!["id"]!);
    }

    [Fact]
    public async Task Zadanie_wydane_ponownie_nie_jest_wykonane_drugi_raz()
    {
        await DodajZadanie(Zapis("wu-C-2"));
        using var c = Connector();
        await c.WykonajCyklAsync(default);
        // Symulacja: serwer zgubil potwierdzenie i wydaje to samo zadanie jeszcze raz.
        await DodajZadanie(Zapis("wu-C-2"));
        await c.WykonajCyklAsync(default);

        Assert.Single(_wyk.Wywolania);
    }


    [Fact]
    public async Task Kolizja_terminu_odsyla_wpisy_blokujace()
    {
        // Termin nachodzi na istniejacy wpis 10001 (09–10.02.2026) z danych testowych.
        await DodajZadanie(new
        {
            requestId = "wu-C-K1", operacja = "zapis", zatrudnienieId = "207", rodzaj = new { zrodlo = "urlop", kod = 0 },
            dataOd = "2026-02-09", dataDo = "2026-02-10", zapisy = Array.Empty<object>(),
        });
        _wyk.Odpowiedz = _ => new WynikWorkera
        {
            Status = Statusy.Blad, Kod = Kody.Kolizja, Komunikat = "Urlop pokrywa sie z wczesniejszymi okresami",
        };

        using var c = Connector();
        await c.WykonajCyklAsync(default);

        var wynik = JsonNode.Parse((string)Assert.Single((JsonArray)(await StanAtrapy())["zadania"]!)!["wynik"]!)!;
        Assert.Equal("blad", (string)wynik["status"]!);
        Assert.Equal(Kody.Kolizja, (string)wynik["kod"]!);
        var blok = Assert.Single((JsonArray)wynik["zapisy"]!)!;
        Assert.Equal("urlop:10001", (string)blok["id"]!);
        Assert.Equal("2026-02-09", (string)blok["dataOd"]!);
        Assert.Equal("2026-02-10", (string)blok["dataDo"]!);
        Assert.Equal(2, (int)blok["dni"]!);
    }

    [Fact]
    public async Task Zmiana_odrzucona_bez_wywolania_procesu_roboczego()
    {
        await DodajZadanie(new
        {
            requestId = "wu-C-3", operacja = "zmiana", zatrudnienieId = "207", rodzaj = new { zrodlo = "urlop", kod = 0 },
            dataOd = "2026-12-14", dataDo = "2026-12-16", zapisy = new[] { new { id = "urlop:12330" } },
        });
        using var c = Connector();
        await c.WykonajCyklAsync(default);

        Assert.Empty(_wyk.Wywolania);
        var wynik = JsonNode.Parse((string)((JsonArray)(await StanAtrapy())["zadania"]!)[0]!["wynik"]!)!;
        Assert.Equal("OPERACJA_NIEOBSLUGIWANA", (string)wynik["kod"]!);
        Assert.Equal("urlop:12330", (string)wynik["zapisy"]![0]!["id"]!);   // zapisy = stan faktyczny, nietkniety
    }

    [Fact]
    public async Task Zadanie_dla_zwolnionego_odrzucone()
    {
        await DodajZadanie(new
        {
            requestId = "wu-C-4", operacja = "zapis", zatrudnienieId = "300", rodzaj = new { zrodlo = "urlop", kod = 0 },
            dataOd = "2026-12-14", dataDo = "2026-12-15", zapisy = Array.Empty<object>(),
        });
        using var c = Connector();
        await c.WykonajCyklAsync(default);
        Assert.Empty(_wyk.Wywolania);
        var wynik = JsonNode.Parse((string)((JsonArray)(await StanAtrapy())["zadania"]!)[0]!["wynik"]!)!;
        Assert.Equal("R2_ZATRUDNIENIE_NIEZNALEZIONE", (string)wynik["kod"]!);
    }

    [Fact]
    public async Task Wycofane_przed_wykonaniem_zamkniete_wynikiem_wycofano()
    {
        await DodajZadanie(Zapis("wu-C-5"));
        await _f.Sterowanie.PostAsJsonAsync("/_atrapa/wycofaj", new { nip = _nip, requestId = "wu-C-5" });
        using var c = Connector();
        await c.WykonajCyklAsync(default);

        Assert.Empty(_wyk.Wywolania);
        var z = ((JsonArray)(await StanAtrapy())["zadania"]!)[0]!;
        Assert.Equal("wycofano", (string)JsonNode.Parse((string)z["wynik"]!)!["status"]!);
        Assert.Empty((JsonArray)(await StanAtrapy())["wycofane"]!);
    }

    [Fact]
    public async Task Blad_przejsciowy_zapisu_oddaje_zadanie_i_ustawia_stan_connectora()
    {
        await DodajZadanie(Zapis("wu-C-6"));
        _wyk.Odpowiedz = _ => new WynikWorkera { Status = Statusy.BladPrzejsciowy, Kod = Kody.Niedostepne, Komunikat = "Użytkownik WNIOSKI_DEMO jest zalogowany" };
        using var c = Connector();
        await c.WykonajCyklAsync(default);
        Assert.Equal("Oczekuje", (string)((JsonArray)(await StanAtrapy())["zadania"]!)[0]!["stan"]!);

        // W nastepnym starcie administrator widzi przyczyne — bez tresci komunikatu R2 (moze zawierac nazwiska).
        _wyk.Odpowiedz = new WykonawcaTestowy().Odpowiedz;
        await c.WykonajCyklAsync(default);
        var start = JsonNode.Parse((string)(await StanAtrapy())["ostatniStart"]!)!;
        Assert.Equal("zapisWstrzymany", (string)start["stan"]!);
        Assert.DoesNotContain("WNIOSKI_DEMO", (string)start["stanOpis"]!);
        Assert.Equal(2, _wyk.Wywolania.Count);   // ponowione i tym razem zapisane
    }

    [Fact]
    public async Task Niezatwierdzona_firma_zgloszona_i_zatrzymana()
    {
        lock (_f.Stan.Zamek) _f.Stan.DozwoloneNip.Remove(_nip);
        using var c = Connector();
        var w = await c.WykonajCyklAsync(default);
        Assert.Equal(WynikFirmy.ZatrzymanaFirma, Assert.Single(w.Firmy).Wynik);
        Assert.False(w.ConnectorZatrzymany);
        lock (_f.Stan.Zamek) Assert.Equal("FIRMA TESTOWA", _f.Stan.Zgloszenia[_nip]);
    }

    [Fact]
    public async Task Zly_klucz_zatrzymuje_connector_na_15_minut()
    {
        using var c = Connector(new string('f', 64));
        var w = await c.WykonajCyklAsync(default);
        Assert.True(w.ConnectorZatrzymany);
        Assert.Equal(TimeSpan.FromMinutes(15), w.Nastepny);
    }

    [Fact]
    public async Task Awaria_odczytu_SQL_nie_blokuje_wynikow_i_melduje_odczytWstrzymany()
    {
        using var c = Connector();
        await c.WykonajCyklAsync(default);   // poznaje NIP
        _zr.Awaria = true;
        var w = Assert.Single((await c.WykonajCyklAsync(default)).Firmy);
        Assert.Equal(WynikFirmy.Ok, w.Wynik);
        var start = JsonNode.Parse((string)(await StanAtrapy())["ostatniStart"]!)!;
        Assert.Equal("odczytWstrzymany", (string)start["stan"]!);
    }


    [Fact]
    public async Task Podmieniona_baza_zatrzymuje_firme_przed_wyslaniem()
    {
        _zr.Dane = Dane(_nip, urlopow: 25);
        using var c = Connector();
        Assert.Equal(WynikFirmy.Ok, Assert.Single((await c.WykonajCyklAsync(default)).Firmy).Wynik);
        int nieobecnosciPrzed = (int)(await StanAtrapy())["rekordy"]!["nieobecnosc"]!;

        // Odtworzenie danych zamiast kopii bazy: te same osoby i okresy, ale wszystkie identyfikatory inne.
        var stare = Dane(_nip, urlopow: 25);
        _zr.Dane = stare with { Urlopy = stare.Urlopy.Select(u => u with { Id = u.Id + 500000 }).ToList() };

        var w = Assert.Single((await c.WykonajCyklAsync(default)).Firmy);
        Assert.Equal(WynikFirmy.ZatrzymanaFirma, w.Wynik);
        Assert.Contains(w.Uwagi, u => u.Contains("podmienioną bazę"));
        Assert.Equal(nieobecnosciPrzed, (int)(await StanAtrapy())["rekordy"]!["nieobecnosc"]!);   // nic nie poszlo
    }

    [Fact]
    public async Task Pelny_przeglad_na_zadanie_serwera_domyka_usuniecia()
    {
        using var c = Connector();
        await c.WykonajCyklAsync(default);
        await _f.Sterowanie.PostAsJsonAsync("/_atrapa/przeglad", new { nip = _nip, wlacz = true });
        var w = Assert.Single((await c.WykonajCyklAsync(default)).Firmy);
        Assert.Equal(WynikFirmy.Ok, w.Wynik);
        Assert.Contains("POST /api/v1/sync/przeglad", ((JsonArray)(await StanAtrapy())["dziennik"]!).Select(x => (string)x!));
    }
}
