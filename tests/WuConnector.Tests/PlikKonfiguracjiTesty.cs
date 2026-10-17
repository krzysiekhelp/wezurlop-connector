using WuConnector.Core.Konfiguracja;

namespace WuConnector.Tests;

/// <summary>Okno zapisuje sekcjami — plik moze zawierac instalacje niekompletne; usluga je pomija, reszta dziala.</summary>
public class PlikKonfiguracjiTesty
{
    static PlikKonfiguracji.InstalacjaWPliku Instalacja(string nazwa, string loginR2, string hasloR2, string? loginSql = "wu_x", string? hasloSql = "h", bool aktywna = true) => new()
    {
        Nazwa = nazwa, KatalogBin = $@"C:\{nazwa}\BIN\", SerwerSql = ".", BazaBin = "b",
        LoginSql = loginSql, HasloSql = hasloSql, LoginR2 = loginR2, HasloR2 = hasloR2,
        Firmy = new() { new FirmaR2 { FirmIde = 1, BazaDane = "d", Aktywna = aktywna } },
    };

    [Fact]
    public void Braki_wskazuja_czego_brakuje()
    {
        Assert.Null(PlikKonfiguracji.Braki(Instalacja("a", "WNIOSKI_DEMO", "x")));
        Assert.Equal("hasło R2", PlikKonfiguracji.Braki(Instalacja("a", "WNIOSKI_DEMO", "")));
        Assert.Equal("login R2, hasło R2, hasło SQL", PlikKonfiguracji.Braki(Instalacja("a", "", "", hasloSql: "")));
        Assert.Null(PlikKonfiguracji.Braki(Instalacja("a", "", "", loginSql: null, hasloSql: null, aktywna: false)));   // wylaczona
        Assert.Null(PlikKonfiguracji.Braki(Instalacja("a", "L", "x", loginSql: null, hasloSql: null)));                  // logowanie Windows
    }

    [Fact]
    public void Niekompletna_instalacja_pominieta_z_ostrzezeniem_reszta_rozwinieta()
    {
        var plik = new PlikKonfiguracji
        {
            AdresSerwera = "https://urlopy.example.pl", KluczApi = new string('a', 64),
            SekretOsoby = Convert.ToBase64String(new byte[32]),
            Instalacje = new() { Instalacja("pelna", "WNIOSKI_DEMO", "x"), Instalacja("bez_hasla", "WNIOSKI_DEMO", "") },
        };
        var ostrzezenia = new List<string>();
        var k = plik.Rozwin(Path.GetTempPath(), ostrzezenia);

        Assert.Equal("pelna", Assert.Single(k.Instalacje).Nazwa);
        Assert.Contains(ostrzezenia, o => o.Contains("bez_hasla") && o.Contains("hasło R2"));
    }
}

/// <summary>Przenosiny na inna maszyne: sekret osobaId musi przejsc bez zmiany, inaczej te same osoby dostana nowe identyfikatory.</summary>
public class SekretOsobyTesty
{
    const string Sekret = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    [Fact]
    public void Odcisk_ten_sam_dla_jawnego_i_zaszyfrowanego()
        => Assert.Equal(PlikKonfiguracji.OdciskSekretu(Sekret), PlikKonfiguracji.OdciskSekretu(PlikKonfiguracji.Zaszyfruj(Sekret)));

    [Fact]
    public void Odcisk_rozny_dla_innego_sekretu()
        => Assert.NotEqual(PlikKonfiguracji.OdciskSekretu(Sekret), PlikKonfiguracji.OdciskSekretu(PlikKonfiguracji.NowySekretOsoby()));

    [Fact]
    public void Sekret_z_innej_maszyny_daje_czytelny_blad_zamiast_nowego_sekretu()
    {
        var plik = new PlikKonfiguracji
        {
            AdresSerwera = "https://x.test", KluczApi = new string('a', 64),
            SekretOsoby = "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAZZZZ", Instalacje = new(),
        };
        var e = Assert.Throws<InvalidOperationException>(() => plik.Rozwin(Path.GetTempPath(), new List<string>()));
        Assert.Contains("sekret 'sekretOsoby'", e.Message);
        Assert.Contains("--sekret-eksport", e.Message);
    }
}
