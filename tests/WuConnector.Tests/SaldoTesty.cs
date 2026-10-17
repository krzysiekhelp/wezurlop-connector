using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;

namespace WuConnector.Tests;

public class SaldoTesty
{
    static int _id = 1000;

    static UrlopWiersz Limit(string od, int limit, int pozostalo, int rodzaj = 0) =>
        new(_id++, 207, L: true, rodzaj, DateTime.Parse(od), null, null, null, limit, pozostalo, false, null, null, null, null, null);

    static UrlopWiersz Okres(string od, int godz, int rodzaj = 0, int? pozostaloPo = null, int? korekta = null, int? dni = null) =>
        new(_id++, 207, L: false, rodzaj, DateTime.Parse(od), DateTime.Parse(od), dni ?? godz / 480, godz, null, pozostaloPo,
            korekta.HasValue, korekta, null, null, null, null);

    [Fact]
    public void Jeden_limit_bez_zuzycia()
    {
        var s = Saldo.Policz(new[] { Limit("2026-01-01", 12480, 18600) }, 2026, 0, Saldo.RodzajeWypoczynkowe);
        Assert.True(s.Naliczony);
        Assert.Equal(12480, s.WymiarMin);
        Assert.Equal(6120, s.ZaleglyMin);
        Assert.Equal(18600, s.PozostaloMin);
    }

    [Fact]
    public void Na_zadanie_zuzywa_pule_wypoczynkowego_a_okolicznosciowy_nie()
    {
        var w = new[]
        {
            Limit("2026-01-01", 12480, 18600),
            Okres("2026-03-02", 4800),                  // 10 dni wypoczynkowego
            Okres("2026-05-04", 480, rodzaj: 5),         // 1 dzien na zadanie
            Okres("2026-06-01", 960, rodzaj: 1),         // okolicznosciowy — poza pula
        };
        var s = Saldo.Policz(w, 2026, 0, Saldo.RodzajeWypoczynkowe);
        Assert.Equal(5280, s.WykorzystaneMin);
        Assert.Equal(18600 - 5280, s.PozostaloMin);
    }

    [Fact]
    public void Korekta_kadr_dodaje_minuty()   // recznie wpisana korekta salda przez kadry
    {
        var w = new[] { Limit("2026-01-01", 9360, 9360), Okres("2026-01-01", 2400, korekta: 120) };
        Assert.Equal(9360 - 2400 + 120, Saldo.Policz(w, 2026, 0, Saldo.RodzajeWypoczynkowe).PozostaloMin);
    }

    [Fact]
    public void Kilka_limitow_w_roku_dolicza_LIMIT_kolejnych_a_nie_ich_POZOSTALO()
    {
        // Wg zatrudnienia 1112 / 2024: dolozenie 2880 min 2.07 (prog stazowy).
        // POZOSTALO_GODZ drugiego limitu (12960) to saldo PO zuzyciu z pierwszego polrocza — nie wolno go brac.
        var w = new[]
        {
            Limit("2024-01-01", 9600, 13440),
            Okres("2024-03-04", 3360, pozostaloPo: 10080),
            Limit("2024-07-02", 2880, 12960),
            Okres("2024-08-05", 960, pozostaloPo: 12000),
        };
        var s = Saldo.Policz(w, 2024, 0, Saldo.RodzajeWypoczynkowe);
        Assert.Equal(13440 + 2880 - 3360 - 960, s.PozostaloMin);
        Assert.Equal(12000, s.PozostaloMin);
        Assert.Equal(s.PozostaloMin, s.SaldoNarastajaceR2);
        Assert.Equal(9600 + 2880, s.WymiarMin);
    }

    [Fact]
    public void Limit_zalozony_po_urlopie_z_ta_sama_data()   // przypadek 860 / 2021
    {
        var okres = Okres("2021-01-01", 9120, pozostaloPo: 0);
        var limit = Limit("2021-01-01", 9120, 9120);         // X_I wieksze niz okresu
        var s = Saldo.Policz(new[] { okres, limit }, 2021, 0, Saldo.RodzajeWypoczynkowe);
        Assert.Equal(0, s.PozostaloMin);
        Assert.Equal(0, s.SaldoNarastajaceR2);   // kontrola bierze ostatni OKRES, nie limit
    }

    [Fact]
    public void Brak_limitu_to_nienaliczony_z_pustymi_polami()
    {
        var s = Saldo.Policz(new[] { Okres("2026-01-12", 480) }, 2026, 0, Saldo.RodzajeWypoczynkowe);
        Assert.False(s.Naliczony);
        Assert.Null(s.PozostaloMin);
        Assert.Null(s.WymiarMin);
    }

    [Fact]
    public void Wpisy_z_innego_roku_nie_licza_sie()
    {
        var w = new[] { Limit("2026-01-01", 12480, 12480), Okres("2025-12-29", 1440) };
        Assert.Equal(12480, Saldo.Policz(w, 2026, 0, Saldo.RodzajeWypoczynkowe).PozostaloMin);
    }

    [Fact]
    public void Przekroczenie_daje_saldo_ujemne()
    {
        var w = new[] { Limit("2026-01-01", 9600, 9600), Okres("2026-02-02", 10080) };
        Assert.Equal(-480, Saldo.Policz(w, 2026, 0, Saldo.RodzajeWypoczynkowe).PozostaloMin);
    }
}
