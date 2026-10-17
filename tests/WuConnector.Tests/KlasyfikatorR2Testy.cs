using WuConnector.Contracts;

namespace WuConnector.Tests;

// Teksty to komunikaty faktycznie zwrocone przez R2 10.10.33 w testach 13–19.09.2026.
public class KlasyfikatorR2Testy
{
    [Fact]
    public void Przelom_miesiaca_to_ostrzezenie_informacyjne()
    {
        var o = KlasyfikatorR2.Ostrzezenia("Urlop na przełomie miesiąca. Wpis zostanie rozbity. (Ostrzeżenie)");
        Assert.Single(o);
        Assert.Equal(Kody.OstrzPrzelomMiesiaca, o[0].Kod);
    }

    [Fact]
    public void Przekroczony_limit_wypoczynkowy()
    {
        var o = KlasyfikatorR2.Ostrzezenia("KOWALSKI JAN: limit urlopu wypoczynkowego za rok 2026 został przekroczony (Ostrzeżenie)");
        Assert.Equal(Kody.OstrzLimitPrzekroczony, o[0].Kod);
    }

    [Fact]
    public void Wykorzystany_limit_na_zadanie()
    {
        var o = KlasyfikatorR2.Ostrzezenia("KOWALSKI JAN: limit urlopu na żądanie (max.4 dni) został wykorzystany (Ostrzeżenie)");
        Assert.Equal(Kody.OstrzNaZadanieWykorzystany, o[0].Kod);
    }

    [Fact]
    public void Kilka_ostrzezen_naraz_limit_wygrywa_z_przelomem()
    {
        var o = KlasyfikatorR2.Ostrzezenia(
            "Urlop na przełomie miesiąca. Wpis zostanie rozbity. (Ostrzeżenie)\r\n" +
            "KOWALSKI JAN: limit urlopu wypoczynkowego za rok 2026 został przekroczony (Ostrzeżenie)");
        Assert.Equal(2, o.Count);
        Assert.Equal(Kody.OstrzLimitPrzekroczony, KlasyfikatorR2.NajwazniejszeOstrzezenie(o));
    }

    [Fact]
    public void Nierozpoznane_ostrzezenie_nie_ginie()
    {
        var o = KlasyfikatorR2.Ostrzezenia("Coś zupełnie nowego (Ostrzeżenie)");
        Assert.Equal(Kody.OstrzInne, o[0].Kod);
        Assert.Equal("Coś zupełnie nowego (Ostrzeżenie)", o[0].Tekst);
    }

    [Fact]
    public void Brak_ostrzezen_to_pusta_lista()
    {
        Assert.Empty(KlasyfikatorR2.Ostrzezenia(""));
        Assert.Null(KlasyfikatorR2.NajwazniejszeOstrzezenie(KlasyfikatorR2.Ostrzezenia(null)));
    }

    [Theory]
    [InlineData("Urlop pokrywa się z wcześniejszymi okresami", Kody.Kolizja)]
    [InlineData("Limit urlopu na żądanie (max.4 dni) został przekroczony", Kody.NaZadaniePrzekroczony)]
    [InlineData("Jakiś inny błąd", Kody.Nieznany)]
    public void Bledy_zapisu(string komunikat, string kod)
        => Assert.Equal(kod, KlasyfikatorR2.BladZapisu(komunikat));

    [Fact]
    public void Konto_zajete_przez_czlowieka_jest_przejsciowe()
    {
        var (status, kod) = KlasyfikatorR2.BladLogowania(
            "Błąd przy logowaniu użytkownika WNIOSKI_DEMO (Użytkownik WNIOSKI_DEMO jest zalogowany na SERWER-DEMO)");
        Assert.Equal(Statusy.BladPrzejsciowy, status);
        Assert.Equal(Kody.Niedostepne, kod);
    }

    [Theory]
    [InlineData("Błędne hasło użytkownika WNIOSKI_DEMO")]   // faktyczny komunikat R2 10.10.33 (19.09.2026)
    [InlineData("Nieprawidłowe hasło użytkownika")]
    [InlineData("Nie można zlokalizować użytkownika WNIOSKI_DEMO")]   // konto nie istnieje w instalacji (BETA, 19.09.2026)
    public void Zle_haslo_jest_trwale(string komunikat)
    {
        var (status, kod) = KlasyfikatorR2.BladLogowania(komunikat);
        Assert.Equal(Statusy.Blad, status);
        Assert.Equal(Kody.Konfiguracja, kod);
    }

    [Fact]
    public void Normalizacja_usuwa_polskie_znaki()
        => Assert.Equal("zazolc gesla jazn", KlasyfikatorR2.Normalizuj("Zażółć  gęślą JAŹŃ"));
}
