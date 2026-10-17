using WuConnector.Core.Konfiguracja;

namespace WuConnector.Tests;

public class HarmonogramTesty
{
    static readonly DateTime Pon10 = new(2026, 9, 21, 10, 0, 0);   // poniedzialek
    static readonly DateTime Pon21 = new(2026, 9, 21, 21, 0, 0);
    static readonly DateTime Pt2330 = new(2026, 9, 25, 23, 30, 0);  // piatek wieczor

    [Fact]
    public void Domyslny_w_godzinach_pracy_co_5_minut()
        => Assert.Equal(TimeSpan.FromMinutes(5), Harmonogram.Domyslny().Odstep(Pon10));

    [Fact]
    public void Ponizej_5_minut_nie_da_sie()
    {
        var h = new Harmonogram { OdstepMinut = 1 };
        Assert.Equal(TimeSpan.FromMinutes(5), h.Odstep(Pon10));
        Assert.NotNull(h.Sprawdz());
    }

    [Fact]
    public void Poza_godzinami_rzadziej()
        => Assert.Equal(TimeSpan.FromMinutes(60), Harmonogram.Domyslny().Odstep(Pon21));

    [Fact]
    public void Poza_godzinami_nie_przesypia_poczatku_dnia_pracy()
    {
        var h = new Harmonogram { OdstepPozaMinut = 240 };
        var t = new DateTime(2026, 9, 22, 5, 0, 0);   // wtorek 5:00, okno od 6:00
        Assert.Equal(TimeSpan.FromHours(1), h.Odstep(t));
    }

    [Fact]
    public void Wstrzymana_w_weekend_budzi_sie_w_poniedzialek_rano()
    {
        var h = new Harmonogram { WstrzymajPoza = true };
        Assert.Equal(new DateTime(2026, 9, 28, 6, 0, 0) - Pt2330, h.Odstep(Pt2330));
    }

    [Fact]
    public void Okno_przez_polnoc()
    {
        var h = new Harmonogram { GodzinaOd = "22:00", GodzinaDo = "06:00", Dni = new() { 1, 2, 3, 4, 5, 6, 7 } };
        Assert.True(h.WOknie(new DateTime(2026, 9, 21, 23, 0, 0)));
        Assert.True(h.WOknie(new DateTime(2026, 9, 22, 3, 0, 0)));
        Assert.False(h.WOknie(Pon10));
    }

    [Fact]
    public void Opis_czytelny()
        => Assert.Equal("Pn–Pt 06:00–20:00 co 5 min, poza tym co 60 min", Harmonogram.Domyslny().Opis());

    [Fact]
    public void Wstrzymanie_bez_dni_pracy_odrzucone()
        => Assert.NotNull(new Harmonogram { Dni = new(), WstrzymajPoza = true }.Sprawdz());
}
