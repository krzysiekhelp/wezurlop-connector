using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WuConnector.Contracts;
using WuConnector.Core.Cykl;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Odczyt;

namespace WuConnector.Tests;

public class NipTesty
{
    [Theory]
    [InlineData("9876543210", true)]    // druga firma z przykladow tenanta
    [InlineData("1234567009", true)]    // przyklady tenanta od tury 8
    [InlineData("9876543003", true)]
    [InlineData("5252525006", true)]
    [InlineData("1234567890", false)]   // stary przyklad tenanta — bledna suma
    [InlineData("123-456-70-09", true)] // normalizacja jak na serwerze
    [InlineData("PL1234567009", true)]
    [InlineData("123456700", false)]
    public void Suma_kontrolna(string nip, bool poprawny) => Assert.Equal(poprawny, Nip.CzyPoprawny(nip));

    [Theory]
    [InlineData("987-654-32-10", "9876543210")]   // NIP z myslnikami — tak bywa wpisany w bazie
    [InlineData("PL 1234567009", "1234567009")]
    [InlineData(" 1234567009\t", "1234567009")]
    public void Do_serwera_ida_same_cyfry(string zBazy, string wyslany) => Assert.Equal(wyslany, Nip.Normalizuj(zBazy));
}

/// <summary>Zmiany protokolu z tury 8: NIP z suma kontrolna, 503 PRZERWA_TECHNICZNA, trzy stany zgloszenia firmy.</summary>
public class Tura8CyklTesty : IClassFixture<AtrapaFixture>, IDisposable
{
    const string NipZgloszony = "9876543003";
    readonly AtrapaFixture _f;
    readonly string _stan = Path.Combine(Path.GetTempPath(), $"wu-t8-{Guid.NewGuid():N}.db");
    public Tura8CyklTesty(AtrapaFixture f) { _f = f; }

    public void Dispose()
    {
        foreach (var p in new[] { _stan, _stan + "-wal", _stan + "-shm" }) try { File.Delete(p); } catch (IOException) { }
    }

    static SuroweDane Dane(string nip) => new(
        new FirmaWiersz(nip, "FIRMA B"),
        new[] { new PracownikWiersz(207, "KOWALSKI", "JAN", null, false, null, false, true, false, false, "90010112345") },
        new[] { new ZatrudnienieWiersz(207, 207, true, true, 2, 1, 1, new(2020, 1, 1), null) },
        new[] { new UrlopWiersz(9000, 207, true, 0, new(2026, 1, 1), null, null, null, 12480, 18600, false, null, null, 3, null, null) },
        Array.Empty<KadroweWiersz>(), Array.Empty<ZasilekWiersz>(),
        new[] { new SlownikWiersz("urlop", 0, "urlop wypoczynkowy") },
        Array.Empty<KalendarzWiersz>(), Array.Empty<LataKalWiersz>(), DateTime.Now);

    Connector Utworz(string nip) => new(
        new KonfiguracjaConnectora
        {
            Serwer = new SerwerTenanta { Adres = _f.Adres, Klucz = AtrapaFixture.Klucz },
            Instalacje = new()
            {
                new InstalacjaR2
                {
                    Nazwa = "t8", KatalogBin = @"C:\R2T8\BIN\", SerwerSql = ".", BazaBin = "b", LoginR2 = "x", HasloR2 = "x",
                    Firmy = new() { new FirmaR2 { FirmIde = 1, BazaDane = "d" } },
                },
            },
            SekretOsoby = new byte[32], SciezkaWorkera = "x.exe", SciezkaStanu = _stan,
        },
        "t", NullLogger.Instance, new WykonawcaTestowy(), (_, _) => new ZrodloTestowe(Dane(nip)), czekaj: (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Zgloszenie_zatwierdzone_start_w_tym_samym_cyklu()
    {
        using var c = Utworz(NipZgloszony);
        var w1 = await c.WykonajCyklAsync(default);
        Assert.Equal(WynikFirmy.ZatrzymanaFirma, Assert.Single(w1.Firmy).Wynik);   // oczekuje

        await _f.Sterowanie.PostAsJsonAsync("/_atrapa/zatwierdz", new { nip = NipZgloszony });
        using var c2 = Utworz(NipZgloszony);   // nowy proces — zgloszenie nie jest wstrzymane odstepem 30 min
        var w2 = await c2.WykonajCyklAsync(default);
        Assert.Equal(WynikFirmy.Ok, Assert.Single(w2.Firmy).Wynik);                  // start od razu po zatwierdzeniu
        lock (_f.Stan.Zamek) _f.Stan.DozwoloneNip.Remove(NipZgloszony);
    }

    [Fact]
    public async Task Zgloszenie_odrzucone_zapamietane_i_nieponawiane()
    {
        const string nip = "1234567009";
        lock (_f.Stan.Zamek) _f.Stan.OdrzuconeNip.Add(nip);
        using (var c = Utworz(nip)) await c.WykonajCyklAsync(default);

        int zgloszenPrzed;
        lock (_f.Stan.Zamek) zgloszenPrzed = _f.Stan.Dziennik.Count(d => d.EndsWith("zgloszenie-firmy"));
        using (var c = Utworz(nip)) await c.WykonajCyklAsync(default);                // nowy proces, ten sam plik stanu
        lock (_f.Stan.Zamek) Assert.Equal(zgloszenPrzed, _f.Stan.Dziennik.Count(d => d.EndsWith("zgloszenie-firmy")));
    }

    const string KluczT8 = @"c:\r2t8\bin|1";   // klucz firmy: katalog BIN instalacji (male litery) + FIRM.X_I

    int Zgloszenia(string nip) { lock (_f.Stan.Zamek) return _f.Stan.Dziennik.Count(d => d.EndsWith("zgloszenie-firmy")); }

    [Fact]
    public async Task Odrzucona_sprawdzana_ponownie_po_dobie_a_cofniecie_wykryte()
    {
        const string nip = "9876543210";
        lock (_f.Stan.Zamek) _f.Stan.OdrzuconeNip.Add(nip);
        using (var c = Utworz(nip)) await c.WykonajCyklAsync(default);
        lock (_f.Stan.Zamek) _f.Stan.OdrzuconeNip.Remove(nip);                  // administrator cofa odrzucenie (zgloszenie znow oczekuje)

        int przed = Zgloszenia(nip);
        using (var c = Utworz(nip)) await c.WykonajCyklAsync(default);           // w ciagu doby — nie zglaszamy
        Assert.Equal(przed, Zgloszenia(nip));

        using (var c = Utworz(nip))
        {
            c.Stan.UstawOdrzucenie(KluczT8, nip, true, DateTimeOffset.Now.AddHours(-25));   // minela doba
            await c.WykonajCyklAsync(default);
            Assert.Equal(przed + 1, Zgloszenia(nip));
            Assert.False(c.Stan.CzyOdrzucona(KluczT8));                          // "oczekuje" — odrzucenie cofniete
        }
    }

    [Fact]
    public async Task Udany_start_kasuje_odrzucenie()
    {
        using var c = Utworz(AtrapaFixture.Nip);
        c.Stan.UstawOdrzucenie(KluczT8, AtrapaFixture.Nip, true);
        Assert.Equal(WynikFirmy.Ok, Assert.Single((await c.WykonajCyklAsync(default)).Firmy).Wynik);
        Assert.False(c.Stan.CzyOdrzucona(KluczT8));
    }

    [Fact]
    public async Task Nip_z_bledna_suma_nie_idzie_do_serwera()
    {
        int przed;
        lock (_f.Stan.Zamek) przed = _f.Stan.Dziennik.Count;
        using var c = Utworz("1234567890");
        var w = await c.WykonajCyklAsync(default);
        Assert.Equal(WynikFirmy.ZatrzymanaFirma, Assert.Single(w.Firmy).Wynik);
        lock (_f.Stan.Zamek) Assert.Equal(przed, _f.Stan.Dziennik.Count);
    }

    [Fact]
    public async Task Ta_sama_baza_w_dwoch_instalacjach_synchronizowana_raz()
    {
        InstalacjaR2 Inst(string nazwa) => new()
        {
            Nazwa = nazwa, KatalogBin = $@"C:{nazwa}BIN\", SerwerSql = ".", BazaBin = "b", LoginR2 = "x", HasloR2 = "x",
            Firmy = new() { new FirmaR2 { FirmIde = 1, BazaDane = "d" } },
        };
        using var c = new Connector(
            new KonfiguracjaConnectora
            {
                Serwer = new SerwerTenanta { Adres = _f.Adres, Klucz = AtrapaFixture.Klucz },
                Instalacje = new() { Inst("nowa"), Inst("stara") },
                SekretOsoby = new byte[32], SciezkaWorkera = "x.exe", SciezkaStanu = _stan,
            },
            "t", NullLogger.Instance, new WykonawcaTestowy(), (_, _) => new ZrodloTestowe(Dane(AtrapaFixture.Nip)), czekaj: (_, _) => Task.CompletedTask);

        var w = await c.WykonajCyklAsync(default);
        Assert.Single(w.Firmy);
    }

    [Fact]
    public async Task Przerwa_techniczna_503_ponowiona_po_Retry_After()
    {
        var czekania = new List<TimeSpan>();
        var c = new Connector(
            new KonfiguracjaConnectora
            {
                Serwer = new SerwerTenanta { Adres = _f.Adres, Klucz = AtrapaFixture.Klucz },
                Instalacje = new() { new InstalacjaR2 { Nazwa = "t8b", KatalogBin = @"C:\R2T8B\BIN\", SerwerSql = ".", BazaBin = "b", LoginR2 = "x", HasloR2 = "x", Firmy = new() { new FirmaR2 { FirmIde = 1, BazaDane = "d" } } } },
                SekretOsoby = new byte[32], SciezkaWorkera = "x.exe", SciezkaStanu = _stan,
            },
            "t", NullLogger.Instance, new WykonawcaTestowy(), (_, _) => new ZrodloTestowe(Dane(AtrapaFixture.Nip)),
            czekaj: (t, _) => { czekania.Add(t); return Task.CompletedTask; });
        await _f.Sterowanie.PostAsJsonAsync("/_atrapa/awaria", new { status = 503, ile = 1, retryAfter = 30 });

        var w = await c.WykonajCyklAsync(default);
        c.Dispose();

        Assert.Equal(WynikFirmy.Ok, Assert.Single(w.Firmy).Wynik);
        Assert.Contains(TimeSpan.FromSeconds(30), czekania);
    }
}
