using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WuConnector.Core.Cykl;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;
using WuConnector.Core.Wymiana;

namespace WuConnector.Tests;

/// <summary>Zmiany protokolu z 20.09.2026 wieczorem: operacja `stan`, odciskTozsamosci, nowe pola slownika i kalendarza.</summary>
public class Tura10Testy : IClassFixture<AtrapaFixture>, IDisposable
{
    readonly AtrapaFixture _f;
    readonly string _stan = Path.Combine(Path.GetTempPath(), $"wu-t10-{Guid.NewGuid():N}.db");
    public Tura10Testy(AtrapaFixture f) { _f = f; }

    public void Dispose()
    {
        foreach (var p in new[] { _stan, _stan + "-wal", _stan + "-shm" }) try { File.Delete(p); } catch (IOException) { }
    }

    [Fact]
    public async Task Stan_bez_nip_sprawdza_sam_klucz()
    {
        using var k = _f.Klient();
        var s = await k.StanAsync();
        Assert.Equal(1, s.Protokol);
        Assert.Equal("aktywny", s.Klucz);
        Assert.Null(s.Firma);   // bez NIP-u nie ma czego mowic o firmie
    }

    [Fact]
    public async Task Stan_rozroznia_firme_znana_zgloszona_i_nieznana()
    {
        const string nowa = "5252525006";
        lock (_f.Stan.Zamek) { _f.Stan.DozwoloneNip.Remove(nowa); _f.Stan.Zgloszenia.Remove(nowa); }
        using var k = _f.Klient();

        Assert.Equal("ok", (await k.StanAsync(AtrapaFixture.Nip)).Firma!.Stan);
        Assert.Equal("firma_nieznana", (await k.StanAsync(nowa)).Firma!.Stan);

        await k.ZgloszenieFirmyAsync(nowa, "Firma C", Guid.NewGuid().ToString());
        Assert.Equal("firma_zgloszona", (await k.StanAsync(nowa)).Firma!.Stan);
    }

    [Fact]
    public async Task Zly_klucz_w_stanie_to_401()
    {
        using var k = _f.Klient(new string('f', 64));
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.StanAsync(AtrapaFixture.Nip));
        Assert.Equal(401, e.Status);
    }

    // ---------------------------------------------------------------- odcisk tozsamosci

    static SuroweDane Dane(string nip) => new(
        new FirmaWiersz(nip, "FIRMA T10"),
        new[] { new PracownikWiersz(207, "KOWALSKI", "JAN", null, false, null, false, true, false, false, "90010112345") },
        new[] { new ZatrudnienieWiersz(207, 207, true, true, 2, 1, 1, new(2020, 1, 1), null) },
        new[] { new UrlopWiersz(9000, 207, true, 0, new(2026, 1, 1), null, null, null, 12480, 18600, false, null, null, 3, null, null) },
        Array.Empty<KadroweWiersz>(), Array.Empty<ZasilekWiersz>(),
        new[] { new SlownikWiersz("urlop", 0, "urlop wypoczynkowy"), new SlownikWiersz("zasilek", 0, "choroba") },
        new[] { new KalendarzWiersz(2, "Główny", false, 480, 720) },
        new[] { new LataKalWiersz(2, 2026, new byte[]?[12]) },
        DateTime.Now);

    Connector Utworz(string nip, byte[] sekret) => new(
        new KonfiguracjaConnectora
        {
            Serwer = new SerwerTenanta { Adres = _f.Adres, Klucz = AtrapaFixture.Klucz },
            Instalacje = new()
            {
                new InstalacjaR2
                {
                    Nazwa = "t10", KatalogBin = @"C:\R2T10\BIN\", SerwerSql = ".", BazaBin = "b", LoginR2 = "x", HasloR2 = "x",
                    Firmy = new() { new FirmaR2 { FirmIde = 1, BazaDane = "d" } },
                },
            },
            SekretOsoby = sekret, SciezkaWorkera = "x.exe", SciezkaStanu = _stan,
        },
        "t", NullLogger.Instance, new WykonawcaTestowy(), (_, _) => new ZrodloTestowe(Dane(nip)), czekaj: (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Inny_odcisk_tozsamosci_zatrzymuje_firme_przed_pierwsza_paczka()
    {
        const string nip = "1234567009";
        var sekretA = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var sekretB = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

        using (var c = Utworz(nip, sekretA))
            Assert.Equal(WynikFirmy.Ok, Assert.Single((await c.WykonajCyklAsync(default)).Firmy).Wynik);

        int pracownicyPrzed;
        lock (_f.Stan.Zamek) pracownicyPrzed = _f.Stan.Firma(nip).Zbior("pracownik").Count;

        // Ten sam connector po utracie sekretu (np. konfiguracja postawiona od zera na nowej maszynie).
        using (var c = Utworz(nip, sekretB))
        {
            var w = Assert.Single((await c.WykonajCyklAsync(default)).Firmy);
            Assert.Equal(WynikFirmy.ZatrzymanaFirma, w.Wynik);
        }

        lock (_f.Stan.Zamek)
        {
            Assert.Equal(pracownicyPrzed, _f.Stan.Firma(nip).Zbior("pracownik").Count);   // zadnych dubli osob
            Assert.Equal(PlikKonfiguracji.OdciskSekretu(sekretA), _f.Stan.Firma(nip).OdciskTozsamosci);
        }
    }

    [Fact]
    public void Slownik_i_kalendarz_niosa_nowe_pola()
    {
        var w = BudowniczyRekordow.Zbuduj(Dane("1234567009"), new UstawieniaBudowy(6, new byte[32], new DateTime(2026, 9, 20)));

        var urlop = w.Zbiory[Zbiory.Slownik].Single(r => r.Id == "urlop:0").Tresc;
        Assert.Equal("wypoczynkowy", (string?)urlop["obciazaLimit"]);
        Assert.Equal("domyslne", (string?)urlop["obciazaLimitZrodlo"]);   // jeden wiersz limitu, brak okresow = brak dowodu

        var zasilek = w.Zbiory[Zbiory.Slownik].Single(r => r.Id == "zasilek:0").Tresc;
        Assert.Null((string?)zasilek["obciazaLimit"]);                     // zasilki nie obciazaja limitow karty urlopowej
        Assert.Null((string?)zasilek["obciazaLimitZrodlo"]);

        var kal = w.Zbiory[Zbiory.Kalendarz].First().Tresc;
        Assert.Equal(480, (int?)kal["normaUrlopowaMin"]);
        Assert.Equal(720, (int?)kal["normaDobowaMin"]);
    }
}

/// <summary>Pole `powod` dokladane przez tenanta przy 401 i 409 (wymiana klucza bez licznika, 20.09.2026).</summary>
public class PowodBleduTesty
{
    sealed class Odpowiadacz : HttpMessageHandler
    {
        readonly string _tresc;
        readonly System.Net.HttpStatusCode _status;
        public Odpowiadacz(System.Net.HttpStatusCode status, string tresc) { _status = status; _tresc = tresc; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_tresc, System.Text.Encoding.UTF8, "application/json") });
    }

    static KlientTenanta Klient(System.Net.HttpStatusCode status, string tresc)
        => new(new Uri("https://przyklad.test"), new string('a', 64), "test", new Odpowiadacz(status, tresc));

    [Fact]
    public async Task Powod_przy_401_jest_czytany()
    {
        using var k = Klient(System.Net.HttpStatusCode.Unauthorized,
            """{"blad":{"kod":"KLUCZ_WYGASL","komunikat":"Klucz wygasl","zdarzenieId":"a1b2c3d4","powod":"zastapiony_nowym_kluczem"}}""");
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.StanAsync("1234567009"));
        Assert.Equal("KLUCZ_WYGASL", e.Kod);
        Assert.Equal("zastapiony_nowym_kluczem", e.Powod);
        Assert.True(e.ZatrzymujeConnector);
    }

    [Fact]
    public async Task Brak_powodu_nie_przeszkadza()
    {
        using var k = Klient(System.Net.HttpStatusCode.Conflict,
            """{"blad":{"kod":"PRZEBIEG_NIEZNANY","komunikat":"Nieznany przebieg","zdarzenieId":"b2c3d4e5"}}""");
        var e = await Assert.ThrowsAsync<BladProtokolu>(() => k.StanAsync("1234567009"));
        Assert.Null(e.Powod);
        Assert.Equal(409, e.Status);
    }
}
