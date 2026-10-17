using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AtrapaTenanta;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;
using WuConnector.Core.Wymiana;

namespace WuConnector.Tests;

/// <summary>Atrapa tenanta na losowym porcie, w procesie testow.</summary>
public sealed class AtrapaFixture : IAsyncLifetime
{
    public const string Klucz = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string Nip = "1234567009";   // tura 8: poprawna suma kontrolna

    public StanAtrapy Stan { get; } = new() { Klucz = Klucz };
    public Uri Adres { get; private set; } = null!;
    public HttpClient Sterowanie { get; private set; } = null!;
    WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        Stan.DozwoloneNip.Add(Nip);
        _app = Serwer.Zbuduj(new[] { "--urls", "http://127.0.0.1:0" }, Stan);
        await _app.StartAsync();
        Adres = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        Sterowanie = new HttpClient { BaseAddress = Adres };
    }

    public async Task DisposeAsync() { Sterowanie.Dispose(); await _app.DisposeAsync(); }

    public KlientTenanta Klient(string? klucz = null) => new(Adres, klucz ?? Klucz, "0.1.0-test");
}

public class KlientTenantaTesty : IClassFixture<AtrapaFixture>
{
    readonly AtrapaFixture _f;
    public KlientTenantaTesty(AtrapaFixture f) { _f = f; }

    static string K() => Guid.NewGuid().ToString();

    static StanConnectora Stan(string nip, Dictionary<string, string>? kursory = null)
        => new(nip, "0.1.0-test", "10.10.33", null, StanyConnectora.Ok, null, kursory ?? new());

    /// <summary>Prawdziwe rekordy z budowniczego — atrapa waliduje je wg KSZTALT-DANYCH-R2 v0.7.</summary>
    static WynikBudowy Rekordy(int nieobecnosci = 3)
    {
        var u = Enumerable.Range(1, nieobecnosci).Select(i =>
            new UrlopWiersz(10000 + i, 207, false, 0, new DateTime(2026, 1, 1).AddDays(i * 3), new DateTime(2026, 1, 1).AddDays(i * 3 + 1),
                            2, 960, null, null, false, null, null, 3, new DateTime(2026, 1, 1), null)).ToList();
        u.Add(new UrlopWiersz(9000, 207, true, 0, new(2026, 1, 1), null, null, null, 12480, 18600, false, null, null, 3, null, null));
        var d = new SuroweDane(
            new FirmaWiersz(AtrapaFixture.Nip, "FIRMA"),
            new[] { new PracownikWiersz(207, "KOWALSKI", "JAN", "12", false, null, false, true, false, false, "90010112345") },
            new[] { new ZatrudnienieWiersz(207, 207, true, true, 2, 1, 1, new(2020, 1, 1), null) },
            u, Array.Empty<KadroweWiersz>(),
            new[] { new ZasilekWiersz(512, 207, 0, new(2026, 3, 2), new(2026, 3, 6), 5, false, null, null) },
            new[] { new SlownikWiersz("urlop", 0, "urlop wypoczynkowy") },
            new[] { new KalendarzWiersz(2, "Główny", true, 480, 480) },
            new[] { new LataKalWiersz(2, 2026, new byte[]?[12]) },
            DateTime.Now);
        return BudowniczyRekordow.Zbuduj(d, new UstawieniaBudowy(6, new byte[32], new DateTime(2026, 9, 19)));
    }

    [Fact]
    public async Task Pierwszy_start_pelny_a_po_wyslaniu_z_kursorami_tryb_zmiany()
    {
        using var k = _f.Klient();
        var s1 = await k.StartAsync(Stan(AtrapaFixture.Nip), K());
        Assert.All(s1.Zbiory.Values, z => Assert.Equal("pelny", z.Tryb));

        var kursory = new Dictionary<string, string>();
        foreach (var (zbior, rek) in Rekordy().Zbiory)
        {
            var o = await k.DaneAsync(AtrapaFixture.Nip, s1.PrzebiegId, zbior, 1, true, rek.Select(r => r.Tresc).ToList(), K());
            Assert.Equal(rek.Count, o.Przyjeto);
            Assert.Equal(0, o.PominietePola);          // zadnych pol spoza uzgodnionej listy
            kursory[zbior] = o.Kursor!;
        }
        var kon = await k.KoniecAsync(new PodsumowanieKonca(AtrapaFixture.Nip, s1.PrzebiegId, new(), new(), new(), 0, 0, 0, 10), K());
        Assert.Equal(60, kon.NastepnyInterwal);

        var s2 = await k.StartAsync(Stan(AtrapaFixture.Nip, kursory), K());
        Assert.All(s2.Zbiory.Values, z => Assert.Equal("zmiany", z.Tryb));
    }

    [Fact]
    public async Task Zly_klucz_401_zatrzymuje_connector()
    {
        using var k = _f.Klient(new string('f', 64));
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.StartAsync(Stan(AtrapaFixture.Nip), K()));
        Assert.Equal(401, e.Status);
        Assert.True(e.ZatrzymujeConnector);
        Assert.False(e.Przejsciowy);
    }

    [Fact]
    public async Task Nieznana_firma_403_potem_zgloszenie_202()
    {
        using var k = _f.Klient();
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.StartAsync(Stan("9876543003"), K()));
        Assert.Equal(403, e.Status);
        Assert.Equal("FIRMA_NIEZATWIERDZONA", e.Kod);
        Assert.True(e.ZatrzymujeFirme);

        var z = await k.ZgloszenieFirmyAsync("9876543003", "Firma B", K());
        Assert.Equal("oczekuje", (string)z["status"]!);
        lock (_f.Stan.Zamek) Assert.True(_f.Stan.Zgloszenia.ContainsKey("9876543003"));
    }

    [Fact]
    public async Task Brak_pola_wymaganego_422_z_pozycjami()
    {
        using var k = _f.Klient();
        var s = await k.StartAsync(Stan(AtrapaFixture.Nip), K());
        var rek = Rekordy().Zbiory[Zbiory.Nieobecnosc].Select(r => (JsonObject)r.Tresc.DeepClone()).ToList();
        rek[1].Remove("dataDo");

        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.DaneAsync(AtrapaFixture.Nip, s.PrzebiegId, Zbiory.Nieobecnosc, 1, true, rek, K()));
        Assert.Equal(422, e.Status);
        var p = Assert.Single(e.Pozycje);
        Assert.Equal((string)rek[1]["id"]!, p.Id);
        Assert.Equal("dataDo", p.Pole);
    }

    [Fact]
    public async Task Ta_sama_paczka_z_tym_samym_kluczem_nie_jest_zapisywana_drugi_raz()
    {
        using var k = _f.Klient();
        var s = await k.StartAsync(Stan(AtrapaFixture.Nip), K());
        var rek = Rekordy().Zbiory[Zbiory.Slownik].Select(r => r.Tresc).ToList();
        string klucz = K();
        var a = await k.DaneAsync(AtrapaFixture.Nip, s.PrzebiegId, Zbiory.Slownik, 1, true, rek, klucz);
        var b = await k.DaneAsync(AtrapaFixture.Nip, s.PrzebiegId, Zbiory.Slownik, 1, true, rek, klucz);
        Assert.Equal(a.Kursor, b.Kursor);   // ta sama odpowiedz, kursor nie przesunal sie
    }

    [Fact]
    public async Task Duza_paczka_idzie_gzipem_i_przechodzi()
    {
        using var k = _f.Klient();
        var s = await k.StartAsync(Stan(AtrapaFixture.Nip), K());
        var rek = Rekordy(400).Zbiory[Zbiory.Nieobecnosc].Select(r => r.Tresc).ToList();
        Assert.True(rek.Sum(r => r.ToJsonString().Length) > KlientTenanta.ProgKompresji);
        var o = await k.DaneAsync(AtrapaFixture.Nip, s.PrzebiegId, Zbiory.Nieobecnosc, 1, true, rek, K());
        Assert.Equal(rek.Count, o.Przyjeto);
    }

    [Fact]
    public async Task Ponad_500_rekordow_413()
    {
        using var k = _f.Klient();
        var s = await k.StartAsync(Stan(AtrapaFixture.Nip), K());
        var rek = Rekordy(501).Zbiory[Zbiory.Nieobecnosc].Select(r => r.Tresc).ToList();
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.DaneAsync(AtrapaFixture.Nip, s.PrzebiegId, Zbiory.Nieobecnosc, 1, true, rek, K()));
        Assert.Equal(413, e.Status);
    }

    [Fact]
    public async Task Za_szybko_429_z_Retry_After()
    {
        using var k = _f.Klient();
        await _f.Sterowanie.PostAsJsonAsync("/_atrapa/awaria", new { status = 429, ile = 1, retryAfter = 45 });
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.StartAsync(Stan(AtrapaFixture.Nip), K()));
        Assert.Equal(429, e.Status);
        Assert.True(e.Przejsciowy);
        Assert.Equal(TimeSpan.FromSeconds(45), e.RetryAfter);
    }

    [Fact]
    public async Task Brak_serwera_to_blad_przejsciowy()
    {
        using var k = new KlientTenanta(new Uri("http://127.0.0.1:1"), AtrapaFixture.Klucz, "t", limitCzasu: TimeSpan.FromSeconds(3));
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.StartAsync(Stan(AtrapaFixture.Nip), K()));
        Assert.Equal(0, e.Status);
        Assert.True(e.Przejsciowy);
    }

    [Fact]
    public async Task Zadania_wyniki_i_powrot_zadania_po_bledzie_przejsciowym()
    {
        const string nip = "5252525006";   // tura 8: trzeci NIP do testow wielofirmowych
        lock (_f.Stan.Zamek) _f.Stan.DozwoloneNip.Add(nip);
        foreach (var id in new[] { "wu-T-1", "wu-T-2" })
            await _f.Sterowanie.PostAsJsonAsync("/_atrapa/zadanie", new
            {
                nip,
                zadanie = new { requestId = id, operacja = "zapis", zatrudnienieId = "207", rodzaj = new { zrodlo = "urlop", kod = 0 },
                                dataOd = "2026-12-14", dataDo = "2026-12-15", zapisy = Array.Empty<object>() },
            });

        using var k = _f.Klient();
        var s = await k.StartAsync(Stan(nip), K());
        var z = await k.ZadaniaAsync(nip, s.PrzebiegId, 20);
        Assert.Equal(2, z.Count);
        Assert.Equal("urlop", z[0].Rodzaj!.Zrodlo);
        Assert.Empty(await k.ZadaniaAsync(nip, s.PrzebiegId, 20));   // zarezerwowane

        await k.WynikiAsync(nip, s.PrzebiegId, new[]
        {
            new WynikZadania("wu-T-1", StatusyWyniku.Zapisano, null, null, new() { new("urlop:12330", "2026-12-14", "2026-12-15", 2, 960) }),
            new WynikZadania("wu-T-2", StatusyWyniku.BladPrzejsciowy, "R2_NIEDOSTEPNE", "konto zajete", new()),
        }, K());

        var ponownie = await k.ZadaniaAsync(nip, s.PrzebiegId, 20);
        Assert.Equal("wu-T-2", Assert.Single(ponownie).RequestId);    // przejsciowy wraca do puli
    }
}
