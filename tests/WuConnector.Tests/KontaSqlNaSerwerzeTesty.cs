using WuConnector.Core.Konfiguracja;

namespace WuConnector.Tests;

/// <summary>
/// Pelna sciezka na PRAWDZIWYM serwerze SQL: zalozenie konta, zmiana hasla, nadanie i odebranie odczytu,
/// zmiana nazwy, usuniecie. Testy jednostkowe sprawdzaja tresc skryptow — te sprawdzaja, ze serwer je przyjmuje.
///
/// Uruchamiane tylko przy `set WU_TEST_SQL=1` (w CI nie ma serwera). Pracuja na wlasnym koncie
/// wu_test_&lt;losowe&gt;, ktore na koncu kasuja — zadnego istniejacego konta nie dotykaja.
/// </summary>
public class KontaSqlNaSerwerzeTesty
{
    // Nazwa serwera to dana konkretnej instalacji — nie nalezy do repozytorium. Bierzemy ja ze zmiennej
    // srodowiskowej WU_TEST_SQL_SERWER (np. `set WU_TEST_SQL_SERWER=MOJ-HOST\INSTANCJA`); bez niej test jest pomijany.
    static string? Serwer => Environment.GetEnvironmentVariable("WU_TEST_SQL_SERWER");
    static bool Pominac => Environment.GetEnvironmentVariable("WU_TEST_SQL") != "1" || string.IsNullOrWhiteSpace(Serwer);
    static KontaSql.Dostep Dostep => new(Serwer!);

    [Fact]
    public async Task Pelny_cykl_zycia_konta_na_serwerze()
    {
        if (Pominac) return;

        string login = "wu_test_" + Guid.NewGuid().ToString("N")[..8];
        string poZmianie = login + "_b";

        var stan = await KontaSql.WczytajAsync(Dostep);
        Assert.True(stan.MozeZarzadzacKontami);
        Assert.NotEmpty(stan.Bazy);                       // serwer testowy ma bazy R2
        Assert.DoesNotContain(stan.Konta, k => k.Nazwa == login);

        try
        {
            // 1. zalozenie z haslem wygenerowanym
            string haslo = await KontaSql.UtworzAsync(Dostep, login, windows: false);
            Assert.Equal(24, haslo.Length);
            var konto = await ZnajdzAsync(login);
            Assert.Empty(konto.BazyZOdczytem);

            // 2. zmiana hasla — to jest operacja, ktora w oknie nie dzialala
            string nowe = await KontaSql.UstawHasloAsync(Dostep, konto);
            Assert.NotEqual(haslo, nowe);
            // nowe haslo naprawde dziala: logujemy sie nim do bazy master
            await using (var c = new Microsoft.Data.SqlClient.SqlConnection(
                             KontoOdczytuSql.Polaczenie(Dostep.Serwer, "master", login, nowe)))
                await c.OpenAsync();
            // Pula trzyma polaczenie przy zyciu, a SQL Server nie usunie zalogowanego konta — zamykamy je tutaj,
            // bo inaczej sprzatanie na koncu testu nie przeszloby (tak wlasnie wyszlo przy pierwszym uruchomieniu).
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();

            // 3. nadanie odczytu calej instalacji (firmowa + wspolna)
            var instalacja = stan.Bazy.GroupBy(b => b.Prefiks).First().ToList();
            await KontaSql.UstawOdczytAsync(Dostep, konto, stan.Bazy, instalacja.Select(b => b.Nazwa));
            konto = await ZnajdzAsync(login);
            Assert.Equal(instalacja.Count, konto.BazyZOdczytem.Count);

            // 4. zmiana nazwy — login i uzytkownicy w bazach ida razem
            await KontaSql.ZmienNazweAsync(Dostep, konto, poZmianie, stan.Bazy);
            var poNazwie = await ZnajdzAsync(poZmianie);
            Assert.Equal(instalacja.Count, poNazwie.BazyZOdczytem.Count);   // uprawnienia przezyly zmiane nazwy

            // 5. odebranie odczytu
            await KontaSql.UstawOdczytAsync(Dostep, poNazwie, stan.Bazy, Array.Empty<string>());
            Assert.Empty((await ZnajdzAsync(poZmianie)).BazyZOdczytem);
        }
        finally
        {
            var koncowy = await KontaSql.WczytajAsync(Dostep);
            foreach (var nazwa in new[] { login, poZmianie })
                if (koncowy.Konta.FirstOrDefault(k => k.Nazwa == nazwa) is { } doUsuniecia)
                    await KontaSql.UsunAsync(Dostep, doUsuniecia, koncowy.Bazy);

            var pusty = await KontaSql.WczytajAsync(Dostep);
            Assert.DoesNotContain(pusty.Konta, k => k.Nazwa == login || k.Nazwa == poZmianie);
        }
    }

    static async Task<KontaSql.Konto> ZnajdzAsync(string login)
    {
        var stan = await KontaSql.WczytajAsync(Dostep);
        return stan.Konta.FirstOrDefault(k => k.Nazwa.Equals(login, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Brak konta {login} na serwerze.");
    }
}
