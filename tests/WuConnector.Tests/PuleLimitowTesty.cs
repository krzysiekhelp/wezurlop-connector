using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;

namespace WuConnector.Tests;

/// <summary>
/// Powiazanie rodzaj → limit wyliczane z sald firmy. Pulapka, ktora to wywracala: program kadrowy
/// zapisuje przy urlopie okolicznosciowym „ile by zostalo", choc puli nie zmniejsza — pojedynczy
/// wiersz wyglada wtedy jak zuzycie. Rozstrzyga dopiero tozsamosc salda na koniec roku.
/// </summary>
public class PuleLimitowTesty
{
    static UrlopWiersz Limit(int id, int zatr, int rodzaj, int rok, int wymiar, int? pozostalo) =>
        new(id, zatr, true, rodzaj, new DateTime(rok, 1, 1), null, null, null, wymiar, pozostalo, false, null, null, 3, null, null);

    static UrlopWiersz Okres(int id, int zatr, int rodzaj, DateTime od, int minuty, int pozostalo) =>
        new(id, zatr, false, rodzaj, od, od, minuty / 480, minuty, null, pozostalo, false, null, null, 3, null, null);

    /// <summary>Rok jednego pracownika: wypoczynkowy i na zadanie zuzywaja pule, okolicznosciowy nie.</summary>
    static IEnumerable<UrlopWiersz> Rok(int zatr, int rok, int start)
    {
        yield return Limit(1000 + zatr, zatr, 0, rok, 12480, start);
        int saldo = start;
        yield return Okres(2000 + zatr * 10 + 1, zatr, 0, new DateTime(rok, 2, 5), 2400, saldo -= 2400);
        // Okolicznosciowy: program zapisuje saldo pomniejszone o jego godziny, ale puli nie rusza.
        yield return Okres(2000 + zatr * 10 + 2, zatr, 1, new DateTime(rok, 4, 19), 960, saldo - 960);
        yield return Okres(2000 + zatr * 10 + 3, zatr, 0, new DateTime(rok, 5, 2), 480, saldo -= 480);
        yield return Okres(2000 + zatr * 10 + 4, zatr, 5, new DateTime(rok, 5, 6), 480, saldo -= 480);
        // Wiersz po urlopie na zadanie — dopiero on pokazuje, ze pula faktycznie zeszla o te 480 minut.
        yield return Okres(2000 + zatr * 10 + 5, zatr, 0, new DateTime(rok, 6, 10), 960, saldo -= 960);
    }

    [Fact]
    public void Wyliczone_z_danych_rozroznia_zuzywajace_od_niezuzywajacych()
    {
        var wiersze = Rok(1, 2024, 15360).Concat(Rok(2, 2024, 12480)).Concat(Rok(3, 2025, 9600)).ToList();
        var pule = PuleLimitow.Wylicz(wiersze);

        Assert.Equal(new PowiazanieLimitu("wypoczynkowy", true), pule[0]);
        Assert.Equal(new PowiazanieLimitu("wypoczynkowy", true), pule[5]);
        Assert.Equal(new PowiazanieLimitu(null, true), pule[1]);            // okolicznosciowy nie rusza puli
        // Do liczenia salda bierzemy potwierdzone z danych ORAZ domyslne (np. ekwiwalent, gdy u klienta go nie bylo),
        // ale nigdy rodzaju, o ktorym dane mowia, ze puli nie rusza.
        var zuzywajace = PuleLimitow.ZuzywajacePule(pule, 0);
        Assert.Contains(0, zuzywajace);
        Assert.Contains(5, zuzywajace);
        Assert.Contains(6, zuzywajace);
        Assert.DoesNotContain(1, zuzywajace);
    }

    [Fact]
    public void Opieka_ma_wlasna_pule_i_nie_rusza_wypoczynkowej()
    {
        var wiersze = new List<UrlopWiersz>();
        for (int z = 1; z <= 3; z++)
        {
            wiersze.AddRange(Rok(z, 2024, 15360));
            wiersze.Add(Limit(3000 + z, z, 2, 2024, 960, 960));
            wiersze.Add(Okres(4000 + z, z, 2, new DateTime(2024, 12, 22), 960, 0));
        }
        var pule = PuleLimitow.Wylicz(wiersze);

        Assert.Equal(new PowiazanieLimitu("opieka_art188", true), pule[2]);
        Assert.Equal(new int[] { 2 }, PuleLimitow.ZuzywajacePule(pule, 2));
        Assert.DoesNotContain(2, PuleLimitow.ZuzywajacePule(pule, 0));
    }

    [Fact]
    public void Rodzaj_nieuzywany_u_klienta_jest_domyslny_a_nie_zgadniety()
    {
        var pule = PuleLimitow.Wylicz(Rok(1, 2024, 15360));

        Assert.False(pule[6].ZDanych);                      // ekwiwalent: brak wystapien w tej firmie
        Assert.Equal("wypoczynkowy", pule[6].Pula);         // zachowanie programu, oznaczone jako domyslne
        Assert.False(pule[7].ZDanych);
        Assert.Null(pule[7].Pula);
    }

    [Fact]
    public void Brak_danych_nie_daje_falszywej_pewnosci()
        => Assert.All(PuleLimitow.Wylicz(Array.Empty<UrlopWiersz>()).Values, p => Assert.False(p.ZDanych));
}
