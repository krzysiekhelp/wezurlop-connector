using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace WuConnector.Core.Konfiguracja;

/// <summary>
/// Konta SQL w zakresie CALEGO SERWERA — na potrzeby okna „Zarządzaj kontami MSSQL”.
///
/// Rozni sie od <see cref="KontoOdczytuSql"/> tym, ze nie pracuje w zakresie jednej instalacji:
/// sama znajduje bazy R2 na serwerze, pokazuje wszystkie konta (SQL i Windows) z ich odczytem per baza
/// i pozwala ten odczyt nadawac oraz odbierac.
///
/// Connector czyta bazy wylacznie do odczytu, wiec „nadanie dostepu” to zawsze te same dwie rzeczy:
/// rola db_datareader w bazie i EXECUTE na dbo.fn_Kod w bazie _bin (slowniki). Nic wiecej.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class KontaSql
{
    static readonly Regex Identyfikator = new("^[A-Za-z0-9_]{1,128}$", RegexOptions.CultureInvariant);
    static readonly Regex WzorDanych = new(@"^(?<prefiks>.+)_dane_\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Baza R2 na serwerze: wspolna (_bin) albo firmowa (_dane_N). Prefiks laczy je w jedna instalacje.</summary>
    public sealed record BazaR2(string Nazwa, string Prefiks, bool Wspolna);

    /// <summary>
    /// Konto na serwerze z informacja, ktore bazy R2 juz czyta. „Connectorowe” to konta wu_*,
    /// „systemowe” to konta, ktorych okno nie pozwoli ruszyc (sa, ##MS_*, konta NT).
    /// </summary>
    public sealed record Konto(string Nazwa, bool Windows, bool Wylaczony, bool Administrator, IReadOnlyList<string> BazyZOdczytem)
    {
        public bool Connectorowe => Nazwa.StartsWith("wu_", StringComparison.OrdinalIgnoreCase);

        public bool Systemowe =>
            Nazwa.StartsWith("##", StringComparison.Ordinal)
            || Nazwa.Equals("sa", StringComparison.OrdinalIgnoreCase)
            || Nazwa.StartsWith("NT AUTHORITY\\", StringComparison.OrdinalIgnoreCase)
            || Nazwa.StartsWith("NT SERVICE\\", StringComparison.OrdinalIgnoreCase)
            || Nazwa.StartsWith("BUILTIN\\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Dane logowania administratora — uzywane tylko w trakcie pracy okna, nigdzie niezapisywane.</summary>
    public sealed record Dostep(string Serwer, string? Login = null, string? Haslo = null);

    /// <summary>To, co okno pokazuje po udanym polaczeniu.</summary>
    public sealed record StanSerwera(string Wersja, bool MozeZarzadzacKontami, bool TylkoLogowanieWindows,
                                     IReadOnlyList<BazaR2> Bazy, IReadOnlyList<Konto> Konta);

    // ---------------------------------------------------------------- odczyt stanu serwera

    /// <summary>
    /// Laczy sie kontem administratora i zbiera wszystko, czego potrzebuje okno: wersje serwera, swoje uprawnienia,
    /// bazy R2 i konta z ich odczytem. Rzuca <see cref="UnauthorizedAccessException"/>, gdy konto nie moze zarzadzac loginami.
    /// </summary>
    public static async Task<StanSerwera> WczytajAsync(Dostep dostep, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(KontoOdczytuSql.Polaczenie(dostep.Serwer, "master", dostep.Login, dostep.Haslo));
        await c.OpenAsync(ct);

        string wersja = (string)(await Skalar(c, "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50))", ct) ?? "");
        bool moze = Convert.ToInt32(await Skalar(c, "SELECT IS_SRVROLEMEMBER('sysadmin') | IS_SRVROLEMEMBER('securityadmin')", ct)) == 1;
        bool tylkoWindows = Convert.ToInt32(await Skalar(c, "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)", ct)) == 1;
        if (!moze)
            throw new UnauthorizedAccessException(
                "To konto nie może zarządzać loginami na tym serwerze — potrzebna rola sysadmin albo securityadmin. "
                + "Zaloguj się kontem administratora SQL.");

        var bazy = await BazyR2Async(c, ct);
        var konta = await KontaAsync(c, bazy, ct);
        return new StanSerwera(wersja, moze, tylkoWindows, bazy, konta);
    }

    /// <summary>
    /// Bazy R2 na serwerze. Rozpoznajemy po zawartosci, nie po nazwie: baza wspolna ma tabele FIRM i USERS,
    /// firmowa — PRACOWNK i KART_URL. Dzieki temu bazy innych programow (Optima!) nigdy nie trafiaja na liste.
    /// </summary>
    static async Task<List<BazaR2>> BazyR2Async(SqlConnection c, CancellationToken ct)
    {
        var kandydaci = new List<string>();
        await using (var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE state = 0 AND database_id > 4 AND HAS_DBACCESS(name) = 1 ORDER BY name", c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) kandydaci.Add(r.GetString(0));

        var bazy = new List<BazaR2>();
        foreach (var nazwa in kandydaci)
        {
            if (!Identyfikator.IsMatch(nazwa)) continue;   // nazwy spoza alfabetu nie wchodza do zapytan
            bool wspolna = Convert.ToInt32(await Skalar(c,
                $"SELECT CASE WHEN OBJECT_ID(N'[{nazwa}].dbo.FIRM') IS NOT NULL AND OBJECT_ID(N'[{nazwa}].dbo.USERS') IS NOT NULL THEN 1 ELSE 0 END", ct)) == 1;
            bool firmowa = !wspolna && Convert.ToInt32(await Skalar(c,
                $"SELECT CASE WHEN OBJECT_ID(N'[{nazwa}].dbo.PRACOWNK') IS NOT NULL AND OBJECT_ID(N'[{nazwa}].dbo.KART_URL') IS NOT NULL THEN 1 ELSE 0 END", ct)) == 1;
            if (wspolna || firmowa) bazy.Add(new BazaR2(nazwa, Prefiks(nazwa), wspolna));
        }
        return bazy;
    }

    /// <summary>Prefiks instalacji: nazwa bez koncowki _bin albo _dane_N. Po nim grupujemy bazy w oknie.</summary>
    public static string Prefiks(string nazwaBazy)
    {
        if (nazwaBazy.EndsWith("_bin", StringComparison.OrdinalIgnoreCase)) return nazwaBazy[..^4];
        var m = WzorDanych.Match(nazwaBazy);
        return m.Success ? m.Groups["prefiks"].Value : nazwaBazy;
    }

    static async Task<List<Konto>> KontaAsync(SqlConnection c, List<BazaR2> bazy, CancellationToken ct)
    {
        var loginy = new List<(string Nazwa, bool Windows, bool Wylaczony, bool Admin, byte[] Sid)>();
        await using (var cmd = new SqlCommand("""
            SELECT name, type, is_disabled, sid, IS_SRVROLEMEMBER('sysadmin', name)
            FROM sys.server_principals
            WHERE type IN ('S', 'U', 'G') AND name NOT LIKE '##%'
            ORDER BY name
            """, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                loginy.Add((r.GetString(0), r.GetString(1).Trim() != "S", r.GetBoolean(2),
                            !r.IsDBNull(4) && r.GetInt32(4) == 1, (byte[])r[3]));

        var odczyt = loginy.ToDictionary(l => l.Nazwa, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var baza in bazy)
        {
            var sidy = new List<byte[]>();
            await using var cmd = new SqlCommand($"""
                SELECT u.sid FROM [{baza.Nazwa}].sys.database_role_members m
                JOIN [{baza.Nazwa}].sys.database_principals r ON r.principal_id = m.role_principal_id
                JOIN [{baza.Nazwa}].sys.database_principals u ON u.principal_id = m.member_principal_id
                WHERE r.name IN (N'db_datareader', N'db_owner') AND u.sid IS NOT NULL
                """, c);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) sidy.Add((byte[])r[0]);
            foreach (var l in loginy)
                if (sidy.Any(s => s.AsSpan().SequenceEqual(l.Sid))) odczyt[l.Nazwa].Add(baza.Nazwa);
        }

        return loginy.Select(l => new Konto(l.Nazwa, l.Windows, l.Wylaczony, l.Admin, odczyt[l.Nazwa])).ToList();
    }

    // ---------------------------------------------------------------- zmiany na serwerze

    /// <summary>Zaklada konto SQL (z haslem) albo konto Windows. Zwraca haslo, ktore trzeba pokazac uzytkownikowi.</summary>
    public static async Task<string> UtworzAsync(Dostep dostep, string login, bool windows, string? haslo = null, CancellationToken ct = default)
    {
        SprawdzLogin(login, windows);
        await using var c = await PolaczAsync(dostep, ct);

        if (windows)
        {
            await Wykonaj(c, $"CREATE LOGIN [{login}] FROM WINDOWS;", ct);
            return "";
        }
        if (Convert.ToInt32(await Skalar(c, "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)", ct)) == 1)
            throw new InvalidOperationException(
                "SQL Server dopuszcza tylko logowanie Windows — włącz tryb mieszany albo załóż konto Windows.");

        haslo = string.IsNullOrEmpty(haslo) ? NoweHaslo() : haslo;
        await Wykonaj(c, $"""
            DECLARE @h nvarchar(200) = QUOTENAME(@haslo, '''');
            EXEC(N'CREATE LOGIN [{login}] WITH PASSWORD = ' + @h + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF');
            """, ct, ("@haslo", haslo));
        return haslo;
    }

    /// <summary>Nowe haslo konta SQL. Konto Windows hasla nie ma — wtedy blad.</summary>
    public static async Task<string> UstawHasloAsync(Dostep dostep, Konto konto, string? haslo = null, CancellationToken ct = default)
    {
        if (konto.Windows) throw new InvalidOperationException("Konto Windows nie ma hasła w SQL Serverze — hasłem zarządza system.");
        SprawdzLogin(konto.Nazwa, false);
        if (konto.Systemowe) throw new InvalidOperationException($"{konto.Nazwa} to konto systemowe serwera — connector go nie zmienia.");

        await using var c = await PolaczAsync(dostep, ct);
        haslo = string.IsNullOrEmpty(haslo) ? NoweHaslo() : haslo;
        await Wykonaj(c, $"""
            DECLARE @h nvarchar(200) = QUOTENAME(@haslo, '''');
            EXEC(N'ALTER LOGIN [{konto.Nazwa}] WITH PASSWORD = ' + @h + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF');
            EXEC(N'ALTER LOGIN [{konto.Nazwa}] ENABLE');
            """, ct, ("@haslo", haslo));
        return haslo;
    }

    /// <summary>
    /// Zmiana nazwy konta SQL. Razem z loginem przemianowujemy uzytkownikow w bazach R2 — mapowanie idzie po SID,
    /// wiec dzialaloby i bez tego, ale rozjazd nazw (login X, uzytkownik Y) jest pulapka przy kazdym kolejnym przegladzie.
    /// Konta Windows nazwy nie zmienia: ich tozsamosc nalezy do systemu, nie do SQL Servera.
    /// </summary>
    public static async Task ZmienNazweAsync(Dostep dostep, Konto konto, string nowa, IEnumerable<BazaR2> bazy, CancellationToken ct = default)
    {
        if (konto.Windows) throw new InvalidOperationException(
            "Konta Windows nie da się przemianować w SQL Serverze — nazwa należy do systemu. Usuń je i dodaj pod nową nazwą.");
        if (konto.Systemowe) throw new InvalidOperationException($"{konto.Nazwa} to konto systemowe serwera — nie wolno zmieniać mu nazwy.");
        SprawdzLogin(konto.Nazwa, false);
        SprawdzLogin(nowa, false);
        if (nowa.Equals(konto.Nazwa, StringComparison.OrdinalIgnoreCase)) return;

        await using var c = await PolaczAsync(dostep, ct);
        if (await Skalar(c, $"SELECT SUSER_ID(N'{nowa}')", ct) is not (null or DBNull))
            throw new InvalidOperationException($"Konto {nowa} już istnieje na tym serwerze.");
        foreach (var sql in SkryptZmianyNazwy(konto.Nazwa, nowa, bazy)) await Wykonaj(c, sql, ct);
    }

    /// <summary>Kroki zmiany nazwy — najpierw login, potem uzytkownicy w bazach (kazdy osobnym poleceniem).</summary>
    public static IEnumerable<string> SkryptZmianyNazwy(string stara, string nowa, IEnumerable<BazaR2> bazy)
    {
        yield return $"ALTER LOGIN [{stara}] WITH NAME = [{nowa}];";
        foreach (var b in bazy)
            yield return $"USE [{b.Nazwa}]; IF USER_ID(N'{stara}') IS NOT NULL ALTER USER [{stara}] WITH NAME = [{nowa}];";
    }

    /// <summary>Usuwa konto wraz z jego uzytkownikami w bazach R2. Kont systemowych i administratorow nie rusza.</summary>
    public static async Task UsunAsync(Dostep dostep, Konto konto, IEnumerable<BazaR2> bazy, CancellationToken ct = default)
    {
        SprawdzLogin(konto.Nazwa, konto.Windows);
        if (konto.Windows) throw new InvalidOperationException(
            $"{konto.Nazwa} to konto Windows — usuwa się je w systemie albo w domenie. Tutaj można mu tylko odebrać uprawnienia.");
        if (konto.Systemowe) throw new InvalidOperationException($"{konto.Nazwa} to konto systemowe serwera — nie wolno go usuwać.");
        if (konto.Administrator) throw new InvalidOperationException($"{konto.Nazwa} jest administratorem serwera — usuń mu najpierw rolę sysadmin.");

        await using var c = await PolaczAsync(dostep, ct);
        // Najpierw sesje: SQL Server nie usunie loginu, ktory jest zalogowany, a my zdazylibysmy juz skasowac
        // jego uzytkownikow w bazach i zostawic konto w polowie rozebrane.
        if (await SesjeAsync(c, konto.Nazwa, ct) is { Count: > 0 } sesje)
            throw new InvalidOperationException(
                $"Konto {konto.Nazwa} jest w tej chwili zalogowane ({string.Join("; ", sesje)}). "
                + "Zatrzymaj to, co go używa (np. usługę connectora), i spróbuj ponownie.");

        foreach (var b in bazy) await Wykonaj(c, $"USE [{b.Nazwa}]; IF USER_ID(N'{konto.Nazwa}') IS NOT NULL DROP USER [{konto.Nazwa}];", ct);
        await Wykonaj(c, $"DROP LOGIN [{konto.Nazwa}];", ct);
    }

    /// <summary>
    /// Doprowadza konto do stanu „czyta dokladnie te bazy”: brakujace nadaje, zbedne odbiera.
    /// </summary>
    public static async Task UstawOdczytAsync(Dostep dostep, Konto konto, IEnumerable<BazaR2> wszystkie,
                                              IEnumerable<string> majaByc, CancellationToken ct = default)
    {
        SprawdzLogin(konto.Nazwa, konto.Windows);
        var chciane = majaByc.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await using var c = await PolaczAsync(dostep, ct);
        foreach (var b in wszystkie)
            await Wykonaj(c, chciane.Contains(b.Nazwa) ? SkryptNadania(konto.Nazwa, b) : SkryptOdebrania(konto.Nazwa, b), ct);
    }

    /// <summary>Nadanie odczytu jednej bazie — ten sam tekst, ktory idzie na serwer i ktory pokazujemy w podgladzie skryptu.</summary>
    public static string SkryptNadania(string login, BazaR2 baza)
    {
        var s = new StringBuilder();
        s.AppendLine($"USE [{baza.Nazwa}];");
        s.AppendLine($"IF USER_ID(N'{login}') IS NULL CREATE USER [{login}] FOR LOGIN [{login}];");
        s.AppendLine($"ALTER ROLE db_datareader ADD MEMBER [{login}];");
        if (baza.Wspolna) s.AppendLine($"GRANT EXECUTE ON dbo.fn_Kod TO [{login}];");
        return s.ToString();
    }

    /// <summary>Odebranie odczytu: wychodzimy z roli i kasujemy uzytkownika, o ile nie ma w bazie nic wlasnego.</summary>
    public static string SkryptOdebrania(string login, BazaR2 baza)
    {
        var s = new StringBuilder();
        s.AppendLine($"USE [{baza.Nazwa}];");
        s.AppendLine($"IF USER_ID(N'{login}') IS NOT NULL");
        s.AppendLine("BEGIN");
        s.AppendLine($"    IF IS_ROLEMEMBER('db_datareader', N'{login}') = 1 ALTER ROLE db_datareader DROP MEMBER [{login}];");
        if (baza.Wspolna) s.AppendLine($"    IF OBJECT_ID(N'dbo.fn_Kod') IS NOT NULL REVOKE EXECUTE ON dbo.fn_Kod TO [{login}];");
        s.AppendLine($"    IF NOT EXISTS (SELECT 1 FROM sys.database_role_members m JOIN sys.database_principals u ON u.principal_id = m.member_principal_id WHERE u.name = N'{login}')");
        s.AppendLine($"       AND NOT EXISTS (SELECT 1 FROM sys.database_permissions p JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id WHERE u.name = N'{login}')");
        s.AppendLine($"        DROP USER [{login}];");
        s.AppendLine("END");
        return s.ToString();
    }

    /// <summary>Skrypt dla administratora klienta, ktory woli zrobic to sam — bez laczenia sie z serwera connectora.</summary>
    public static string Skrypt(string login, IEnumerable<BazaR2> bazy)
    {
        var s = new StringBuilder();
        s.AppendLine("-- Konto odczytu dla WU_Connector. Hasło ustaw własne i wpisz je w oknie connectora.");
        s.AppendLine($"CREATE LOGIN [{login}] WITH PASSWORD = N'<hasło>', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;");
        foreach (var b in bazy) s.Append(SkryptNadania(login, b));
        return s.ToString();
    }

    /// <summary>Nazwa konta: litery, cyfry i podkreślenie; konto Windows dodatkowo z jedna kreska ukosna (DOMENA\user).</summary>
    public static bool CzyPoprawnaNazwa(string login, bool windows)
    {
        if (!windows) return Identyfikator.IsMatch(login);
        var czesci = login.Split('\\');
        return czesci.Length == 2 && czesci.All(cz => Identyfikator.IsMatch(cz));
    }

    static void SprawdzLogin(string login, bool windows)
    {
        if (!CzyPoprawnaNazwa(login, windows))
            throw new ArgumentException(windows
                ? @"Konto Windows podaje się jako DOMENA\użytkownik (litery, cyfry i podkreślenie)."
                : "Nazwa konta może zawierać tylko litery, cyfry i podkreślenie (do 128 znaków).");
    }

    public static string NoweHaslo()
    {
        const string znaki = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        const string specjalne = "!#%*+-=?@";
        var h = new char[24];
        for (int i = 0; i < h.Length; i++) h[i] = znaki[System.Security.Cryptography.RandomNumberGenerator.GetInt32(znaki.Length)];
        h[5] = specjalne[System.Security.Cryptography.RandomNumberGenerator.GetInt32(specjalne.Length)];
        h[13] = (char)('0' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10));
        h[19] = specjalne[System.Security.Cryptography.RandomNumberGenerator.GetInt32(specjalne.Length)];
        return new string(h);
    }

    /// <summary>Kto jest w tej chwili zalogowany tym kontem — komputer i program, zeby bylo wiadomo, co zamknac.</summary>
    static async Task<List<string>> SesjeAsync(SqlConnection c, string login, CancellationToken ct)
    {
        var sesje = new List<string>();
        await using var cmd = new SqlCommand(
            """
            SELECT DISTINCT TOP 5 ISNULL(NULLIF(LTRIM(RTRIM(host_name)), ''), '?'),
                                  ISNULL(NULLIF(LTRIM(RTRIM(program_name)), ''), '?')
            FROM sys.dm_exec_sessions WHERE login_name = @login AND session_id <> @@SPID
            """, c);
        cmd.Parameters.AddWithValue("@login", login);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) sesje.Add($"{r.GetString(0)} / {r.GetString(1)}");
        return sesje;
    }

    static async Task<SqlConnection> PolaczAsync(Dostep dostep, CancellationToken ct)
    {
        var c = new SqlConnection(KontoOdczytuSql.Polaczenie(dostep.Serwer, "master", dostep.Login, dostep.Haslo));
        try
        {
            await c.OpenAsync(ct);
            if (Convert.ToInt32(await Skalar(c, "SELECT IS_SRVROLEMEMBER('sysadmin') | IS_SRVROLEMEMBER('securityadmin')", ct)) != 1)
                throw new UnauthorizedAccessException("To konto nie może zarządzać loginami (potrzebna rola sysadmin albo securityadmin).");
            return c;
        }
        catch { await c.DisposeAsync(); throw; }
    }

    static async Task<object?> Skalar(SqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, c);
        return await cmd.ExecuteScalarAsync(ct);
    }

    static async Task Wykonaj(SqlConnection c, string sql, CancellationToken ct, params (string, object)[] p)
    {
        await using var cmd = new SqlCommand(sql, c);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
