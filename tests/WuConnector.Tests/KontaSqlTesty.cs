using WuConnector.Core.Konfiguracja;

namespace WuConnector.Tests;

/// <summary>
/// Okno „Zarządzaj kontami MSSQL” pracuje w zakresie serwera, a nie jednej instalacji.
/// Testy pilnuja tego, co da sie sprawdzic bez serwera: rozpoznania baz, klasyfikacji kont
/// i tresci skryptow, ktore ida na serwer — bo to one nadaja i odbieraja uprawnienia.
/// </summary>
public class KontaSqlTesty
{
    static KontaSql.Konto Konto(string nazwa, bool windows = false, bool admin = false)
        => new(nazwa, windows, false, admin, Array.Empty<string>());

    [Fact]
    public void Prefiks_laczy_baze_wspolna_z_firmowymi()
    {
        Assert.Equal("r2p_platnik10_alfa", KontaSql.Prefiks("r2p_platnik10_alfa_bin"));
        Assert.Equal("r2p_platnik10_alfa", KontaSql.Prefiks("r2p_platnik10_alfa_dane_1"));
        Assert.Equal("r2p_platnik10_alfa", KontaSql.Prefiks("r2p_platnik10_alfa_dane_12"));
        // baza spoza R2 zostaje soba — nie udajemy, ze nalezy do instalacji
        Assert.Equal("CDN_Firma", KontaSql.Prefiks("CDN_Firma"));
    }

    [Fact]
    public void Konta_connectora_i_systemowe_sa_rozpoznawane()
    {
        Assert.True(Konto("wu_r2p_platnik10_alfa").Connectorowe);
        Assert.False(Konto("CDNBackup").Connectorowe);

        // tych okno nie moze ruszyc
        Assert.True(Konto("sa").Systemowe);
        Assert.True(Konto("##MS_PolicyEventProcessingLogin##").Systemowe);
        Assert.True(Konto(@"NT AUTHORITY\SYSTEM", windows: true).Systemowe);
        Assert.True(Konto(@"NT SERVICE\MSSQLSERVER", windows: true).Systemowe);
        Assert.True(Konto(@"BUILTIN\Administrators", windows: true).Systemowe);

        // zwykle konto klienta — wolno go uzyc
        Assert.False(Konto("bireader").Systemowe);
        Assert.False(Konto(@"SERWER-DEMO\kadry_ro", windows: true).Systemowe);
    }

    [Fact]
    public void Nazwa_konta_nie_przepusci_wstrzykniecia()
    {
        Assert.True(KontaSql.CzyPoprawnaNazwa("wu_r2p_platnik10_alfa", windows: false));
        Assert.True(KontaSql.CzyPoprawnaNazwa("ksiegowosc_ro", windows: false));
        Assert.False(KontaSql.CzyPoprawnaNazwa("a]; DROP LOGIN [b", windows: false));
        Assert.False(KontaSql.CzyPoprawnaNazwa("konto z plikiem", windows: false));
        Assert.False(KontaSql.CzyPoprawnaNazwa("", windows: false));

        Assert.True(KontaSql.CzyPoprawnaNazwa(@"SERWER-DEMO\kadry_ro".Replace("-", "_"), windows: true));
        Assert.False(KontaSql.CzyPoprawnaNazwa("kadry_ro", windows: true));          // bez domeny
        Assert.False(KontaSql.CzyPoprawnaNazwa(@"A\B\C", windows: true));
    }

    [Fact]
    public void Nadanie_odczytu_to_db_datareader_a_fn_Kod_tylko_w_bazie_wspolnej()
    {
        var wspolna = new KontaSql.BazaR2("r2p_platnik10_alfa_bin", "r2p_platnik10_alfa", Wspolna: true);
        var firmowa = new KontaSql.BazaR2("r2p_platnik10_alfa_dane_1", "r2p_platnik10_alfa", Wspolna: false);

        string nadanieWspolnej = KontaSql.SkryptNadania("wu_test", wspolna);
        Assert.Contains("CREATE USER [wu_test] FOR LOGIN [wu_test]", nadanieWspolnej);
        Assert.Contains("ALTER ROLE db_datareader ADD MEMBER [wu_test]", nadanieWspolnej);
        Assert.Contains("GRANT EXECUTE ON dbo.fn_Kod TO [wu_test]", nadanieWspolnej);

        string nadanieFirmowej = KontaSql.SkryptNadania("wu_test", firmowa);
        Assert.Contains("ALTER ROLE db_datareader ADD MEMBER [wu_test]", nadanieFirmowej);
        Assert.DoesNotContain("fn_Kod", nadanieFirmowej);   // funkcja jest tylko w bazie _bin

        // nigdzie nie nadajemy prawa zapisu
        foreach (var skrypt in new[] { nadanieWspolnej, nadanieFirmowej })
        {
            Assert.DoesNotContain("db_datawriter", skrypt);
            Assert.DoesNotContain("db_owner", skrypt);
        }
    }

    [Fact]
    public void Odebranie_odczytu_zostawia_uzytkownika_jesli_ma_cos_wlasnego()
    {
        var baza = new KontaSql.BazaR2("r2p_platnik10_alfa_dane_1", "r2p_platnik10_alfa", Wspolna: false);
        string s = KontaSql.SkryptOdebrania("wu_test", baza);

        Assert.Contains("ALTER ROLE db_datareader DROP MEMBER [wu_test]", s);
        // DROP USER tylko wtedy, gdy po wyjsciu z roli nie zostaly zadne role ani uprawnienia
        Assert.Contains("NOT EXISTS", s);
        Assert.Contains("DROP USER [wu_test]", s);
    }

    [Fact]
    public void Skrypt_dla_administratora_klienta_obejmuje_wszystkie_bazy_instalacji()
    {
        var bazy = new[]
        {
            new KontaSql.BazaR2("r2p_platnik10_alfa_bin", "r2p_platnik10_alfa", true),
            new KontaSql.BazaR2("r2p_platnik10_alfa_dane_1", "r2p_platnik10_alfa", false),
        };
        string s = KontaSql.Skrypt("wu_r2p_platnik10_alfa", bazy);

        Assert.Contains("CREATE LOGIN [wu_r2p_platnik10_alfa]", s);
        Assert.Contains("USE [r2p_platnik10_alfa_bin];", s);
        Assert.Contains("USE [r2p_platnik10_alfa_dane_1];", s);
        Assert.Contains("fn_Kod", s);
    }

    [Fact]
    public void Zmiana_nazwy_obejmuje_login_i_uzytkownikow_we_wszystkich_bazach()
    {
        var bazy = new[]
        {
            new KontaSql.BazaR2("r2p_platnik10_alfa_bin", "r2p_platnik10_alfa", true),
            new KontaSql.BazaR2("r2p_platnik10_alfa_dane_1", "r2p_platnik10_alfa", false),
        };
        var kroki = KontaSql.SkryptZmianyNazwy("wu_stare", "wu_nowe", bazy).ToList();

        Assert.Equal("ALTER LOGIN [wu_stare] WITH NAME = [wu_nowe];", kroki[0]);
        // uzytkownik w bazie mapuje sie po SID, ale nazwa zostalaby stara — i przy kolejnym przegladzie mylilaby
        Assert.Contains(kroki, k => k.Contains("USE [r2p_platnik10_alfa_bin]") && k.Contains("ALTER USER [wu_stare] WITH NAME = [wu_nowe]"));
        Assert.Contains(kroki, k => k.Contains("USE [r2p_platnik10_alfa_dane_1]"));
        Assert.All(kroki.Skip(1), k => Assert.Contains("IF USER_ID(N'wu_stare') IS NOT NULL", k));
    }

    [Fact]
    public async Task Nazwy_nie_zmienimy_kontu_Windows_ani_systemowemu()
    {
        var dostep = new KontaSql.Dostep("nieistniejacy-serwer");
        var bazy = Array.Empty<KontaSql.BazaR2>();

        // konto Windows ma tozsamosc w systemie, nie w SQL Serverze
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => KontaSql.ZmienNazweAsync(dostep, Konto(@"DOMENA\kadry", windows: true), "wu_nowe", bazy));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => KontaSql.ZmienNazweAsync(dostep, Konto("sa"), "wu_nowe", bazy));
        // nowa nazwa tez przechodzi walidacje
        await Assert.ThrowsAsync<ArgumentException>(
            () => KontaSql.ZmienNazweAsync(dostep, Konto("wu_stare"), "nowe]; DROP LOGIN [x", bazy));
    }

    [Fact]
    public void Haslo_spelnia_polityke_i_za_kazdym_razem_jest_inne()
    {
        var h1 = KontaSql.NoweHaslo();
        var h2 = KontaSql.NoweHaslo();
        Assert.NotEqual(h1, h2);
        Assert.Equal(24, h1.Length);
        Assert.Contains(h1, c => char.IsUpper(c));
        Assert.Contains(h1, c => char.IsLower(c));
        Assert.Contains(h1, char.IsDigit);
        Assert.Contains(h1, c => "!#%*+-=?@".Contains(c));
    }

    [Fact]
    public async Task Konta_systemowego_nie_da_sie_usunac_ani_zmienic_mu_hasla()
    {
        var dostep = new KontaSql.Dostep("nieistniejacy-serwer");
        // Blad leci z walidacji, zanim pojdzie jakiekolwiek polaczenie — dlatego test nie potrzebuje serwera.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => KontaSql.UsunAsync(dostep, Konto("sa"), Array.Empty<KontaSql.BazaR2>()));
        // konto Windows nalezy do systemu albo domeny — SQL Server trzyma tylko jego uprawnienia
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => KontaSql.UsunAsync(dostep, Konto(@"SERWER_DEMO\kadry", windows: true), Array.Empty<KontaSql.BazaR2>()));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => KontaSql.UstawHasloAsync(dostep, Konto("sa")));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => KontaSql.UstawHasloAsync(dostep, Konto(@"SERWER-DEMO\kadry", windows: true)));
    }
}
