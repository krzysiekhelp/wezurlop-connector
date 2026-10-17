using WuConnector.Contracts;

namespace WuConnector.Tests;

public class WalidatorZadaniaTesty
{
    static ZadanieWorkera Zapis() => new()
    {
        Operacja = Operacje.Zapis,
        KatalogBin = @"C:\R2\BIN\",
        Login = "WNIOSKI_DEMO",
        FirmIde = 1,
        PracownikId = 207,
        ZatrudnienieId = 207,
        RequestId = "wu-01J9ZD11M2A8",
        Kod = 0,
        DataOd = "2026-10-29",
        DataDo = "2026-11-03",
    };

    [Fact]
    public void Poprawny_zapis() => Assert.Null(WalidatorZadania.Sprawdz(Zapis()));

    [Theory]
    [InlineData("wu_z_podkreslnikiem")]
    [InlineData("123456789012345678901234567890123")]   // 33 znaki — nie zmiesci sie w OPIS
    [InlineData("")]
    public void Niepoprawny_requestId(string id)
    {
        var z = Zapis(); z.RequestId = id;
        Assert.NotNull(WalidatorZadania.Sprawdz(z));
    }

    [Theory]
    [InlineData(1)]   // okolicznosciowy — poza etapem 1
    [InlineData(6)]   // ekwiwalent
    public void Kod_spoza_etapu_1(int kod)
    {
        var z = Zapis(); z.Kod = kod;
        Assert.NotNull(WalidatorZadania.Sprawdz(z));
    }

    [Fact]
    public void Na_zadanie_dozwolony()
    {
        var z = Zapis(); z.Kod = 5;
        Assert.Null(WalidatorZadania.Sprawdz(z));
    }

    [Fact]
    public void Odwrocone_daty()
    {
        var z = Zapis(); z.DataOd = "2026-11-03"; z.DataDo = "2026-10-29";
        Assert.NotNull(WalidatorZadania.Sprawdz(z));
    }

    [Fact]
    public void Data_w_zlym_formacie()
    {
        var z = Zapis(); z.DataOd = "29.10.2026";
        Assert.NotNull(WalidatorZadania.Sprawdz(z));
    }

    [Fact]
    public void Usuniecie_wymaga_zapisow()
    {
        var z = Zapis(); z.Operacja = Operacje.Usuniecie; z.Zapisy = new();
        Assert.NotNull(WalidatorZadania.Sprawdz(z));
        z.Zapisy = new() { 12330, 12331 };
        Assert.Null(WalidatorZadania.Sprawdz(z));
    }

    [Fact]
    public void Wynik_przechodzi_przez_JSON_i_jest_odnajdywany_w_wyjsciu()
    {
        var w = new WynikWorkera
        {
            Status = Statusy.ZapisanoZOstrzezeniem,
            Kod = Kody.OstrzPrzelomMiesiaca,
            Komunikat = "Urlop na przełomie miesiąca.",
            Zapisy = { new ZapisR2 { Id = 12330, DataOd = "2026-10-29", DataDo = "2026-10-31", Dni = 2, Minuty = 960 } },
        };
        string stdout = "smiec z biblioteki\r\n" + WorkerJson.Znacznik + WorkerJson.Serializuj(w) + "\r\n";

        var odczyt = WorkerJson.OdczytajWynik(stdout);

        Assert.NotNull(odczyt);
        Assert.Equal(Statusy.ZapisanoZOstrzezeniem, odczyt.Status);
        Assert.Equal(12330, odczyt.Zapisy[0].Id);
        Assert.Contains("\"dataOd\":\"2026-10-29\"", stdout);   // camelCase jak w protokole
    }

    [Fact]
    public void Brak_linii_wyniku_to_null()
        => Assert.Null(WorkerJson.OdczytajWynik("proces padl bez wyniku"));
}
