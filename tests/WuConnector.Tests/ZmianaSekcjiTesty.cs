using WuConnector.Core.Konfiguracja;

namespace WuConnector.Tests;

/// <summary>
/// Sekcja konfiguracji przygotowana w oknie jedzie do okna podniesionego przez UAC jako plik —
/// musi przejsc przez JSON bez straty i nanosic sie wylacznie na swoja czesc konfiguracji.
/// </summary>
public class ZmianaSekcjiTesty
{
    static PlikKonfiguracji Plik(params PlikKonfiguracji.InstalacjaWPliku[] instalacje) => new()
    {
        AdresSerwera = "https://stary.example.pl",
        KluczApi = "dpapi:stary",
        SekretOsoby = "dpapi:sekret",
        Instalacje = instalacje.ToList(),
    };

    static PlikKonfiguracji.InstalacjaWPliku Instalacja(string katalogBin, string nazwa = "R2") => new()
    {
        Nazwa = nazwa, KatalogBin = katalogBin, SerwerSql = ".", BazaBin = "b",
        LoginSql = "wu_b", HasloSql = "dpapi:h", LoginR2 = "WNIOSKI_DEMO", HasloR2 = "dpapi:h",
        Firmy = new() { new FirmaR2 { FirmIde = 1, BazaDane = "d", Aktywna = true } },
    };

    [Fact]
    public void Polaczenie_zmienia_tylko_adres_i_klucz()
    {
        var p = Plik(Instalacja(@"C:\R2\BIN\"));
        ZmianaSekcji.DlaPolaczenia("https://nowy.example.pl", "dpapi:nowy").Zastosuj(p);

        Assert.Equal("https://nowy.example.pl", p.AdresSerwera);
        Assert.Equal("dpapi:nowy", p.KluczApi);
        Assert.Equal("dpapi:sekret", p.SekretOsoby);      // sekret osobaId nietkniety
        Assert.Single(p.Instalacje);                      // instalacje nietkniete
    }

    [Fact]
    public void Harmonogram_i_sekret_zmieniaja_tylko_siebie()
    {
        var p = Plik(Instalacja(@"C:\R2\BIN\"));
        ZmianaSekcji.DlaHarmonogramu(new Harmonogram { OdstepMinut = 15, GodzinaOd = "07:00", GodzinaDo = "18:00", Dni = new() { 1, 2 }, OdstepPozaMinut = 120 }).Zastosuj(p);
        ZmianaSekcji.DlaSekretu("dpapi:przeniesiony").Zastosuj(p);

        Assert.Equal(15, p.Harmonogram!.OdstepMinut);
        Assert.Equal("dpapi:przeniesiony", p.SekretOsoby);
        Assert.Equal("https://stary.example.pl", p.AdresSerwera);
    }

    [Fact]
    public void Instalacja_dodaje_podmienia_po_katalogu_i_usuwa()
    {
        var p = Plik();

        ZmianaSekcji.DlaInstalacji(@"C:\R2\BIN\", Instalacja(@"C:\R2\BIN\")).Zastosuj(p);
        Assert.Single(p.Instalacje);

        // ten sam katalog inna wielkoscia liter to ta sama instalacja — podmiana, nie druga pozycja
        ZmianaSekcji.DlaInstalacji(@"c:\r2\bin\", Instalacja(@"C:\R2\BIN\", "R2 po zmianie")).Zastosuj(p);
        Assert.Single(p.Instalacje);
        Assert.Equal("R2 po zmianie", p.Instalacje[0].Nazwa);

        ZmianaSekcji.DlaInstalacji(@"C:\INNA\BIN\", Instalacja(@"C:\INNA\BIN\", "inna")).Zastosuj(p);
        Assert.Equal(2, p.Instalacje.Count);

        // brak instalacji w zmianie = usuniecie jej z konfiguracji
        ZmianaSekcji.DlaInstalacji(@"C:\R2\BIN\", null).Zastosuj(p);
        Assert.Equal("inna", Assert.Single(p.Instalacje).Nazwa);
    }

    [Fact]
    public void Przez_plik_przekazania_sekcja_wraca_bez_straty()
    {
        string sciezka = Path.Combine(Path.GetTempPath(), $"wu-test-{Guid.NewGuid():N}.json");
        try
        {
            ZmianaSekcji.DlaInstalacji(@"C:\R2\BIN\", Instalacja(@"C:\R2\BIN\", "R2 alfa")).Zapisz(sciezka);
            var wczytana = ZmianaSekcji.Wczytaj(sciezka);

            Assert.Equal(ZmianaSekcji.Rodzaje.Instalacja, wczytana.Rodzaj);
            Assert.Equal("R2 alfa", wczytana.Instalacja!.Nazwa);
            Assert.Equal("dpapi:h", wczytana.Instalacja.HasloR2);   // sekrety jada zaszyfrowane, tak jak w konfiguracji
            Assert.Equal(1, Assert.Single(wczytana.Instalacja.Firmy).FirmIde);

            var p = Plik();
            wczytana.Zastosuj(p);
            Assert.Equal("R2 alfa", Assert.Single(p.Instalacje).Nazwa);
        }
        finally { File.Delete(sciezka); }
    }

    [Fact]
    public void Usuniecie_instalacji_przechodzi_przez_plik_jako_usuniecie()
    {
        string sciezka = Path.Combine(Path.GetTempPath(), $"wu-test-{Guid.NewGuid():N}.json");
        try
        {
            ZmianaSekcji.DlaInstalacji(@"C:\R2\BIN\", null).Zapisz(sciezka);
            var p = Plik(Instalacja(@"C:\R2\BIN\"));
            ZmianaSekcji.Wczytaj(sciezka).Zastosuj(p);
            Assert.Empty(p.Instalacje);
        }
        finally { File.Delete(sciezka); }
    }
}
