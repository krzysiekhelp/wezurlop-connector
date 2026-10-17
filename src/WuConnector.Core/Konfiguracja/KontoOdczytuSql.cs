using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace WuConnector.Core.Konfiguracja;

/// <summary>
/// Konto SQL connectora — WYLACZNIE do odczytu baz R2 (odczyt po SQL, zapis tylko przez API R2).
///
/// Dlaczego osobne konto: usluga dziala jako LocalSystem, a SQL Server domyslnie nie wpuszcza
/// 'NT AUTHORITY\SYSTEM' do baz uzytkownika (sprawdzone 19.09.2026). Login SQL dziala tez wtedy,
/// gdy SQL Server stoi na innej maszynie niz connector.
///
/// Uprawnienia minimalne: db_datareader na bazach _bin i _dane_N danej instalacji
/// + EXECUTE na _bin.dbo.fn_Kod (slowniki). Zadnego zapisu, zadnych innych baz (Optima!).
/// </summary>
public static class KontoOdczytuSql
{
    public const string DomyslnyLogin = "wu_connector";

    /// <summary>
    /// Login dla jednej instalacji: wu_&lt;prefiks baz&gt;, np. wu_r2p_platnik10_alfa.
    /// Osobny na instalacje: login jest wspolny dla calego serwera SQL, wiec jedno konto dla dwoch instalacji
    /// oznaczaloby, ze zalozenie go dla drugiej zmienia haslo zapisane przy pierwszej (wyszlo w tescie 19.09.2026).
    /// Przy okazji kazdy login czyta tylko bazy swojej instalacji.
    /// </summary>
    public static string LoginDla(string bazaBin)
    {
        string prefiks = bazaBin.EndsWith("_bin", StringComparison.OrdinalIgnoreCase) ? bazaBin[..^4] : bazaBin;
        string l = "wu_" + prefiks.ToLowerInvariant();
        return l.Length > 128 ? l[..128] : l;
    }

    static readonly Regex Nazwa = new("^[A-Za-z0-9_]{1,128}$", RegexOptions.CultureInvariant);

    /// <summary>Login SQL na serwerze: czy ma juz odczyt wszystkich baz instalacji (db_datareader), czy jest wylaczony.</summary>
    public sealed record LoginSql(string Nazwa, bool Wylaczony, int BazyZOdczytem, int BazyWszystkie)
    {
        public bool PelnyOdczyt => BazyZOdczytem == BazyWszystkie;
    }

    /// <summary>
    /// Loginy SQL (uwierzytelnianie SQL, bez systemowych ## i bez administratorow sysadmin) do wyboru jako konto odczytu.
    /// Wymaga administratora SQL — tego samego konta, ktorym zakladamy login.
    /// </summary>
    public static async Task<List<LoginSql>> ListaLoginowAsync(string serwer, string bazaBin, IEnumerable<string> bazyDane,
                                                              string? adminLogin = null, string? adminHaslo = null, CancellationToken ct = default)
    {
        var bazy = Bazy(bazaBin, bazyDane);
        await using var c = await PolaczAdministratoraAsync(serwer, adminLogin, adminHaslo, ct);

        var loginy = new List<(string Nazwa, bool Wylaczony, byte[] Sid)>();
        await using (var cmd = new SqlCommand("""
            SELECT name, is_disabled, sid FROM sys.server_principals
            WHERE type = 'S' AND name NOT LIKE '##%' AND IS_SRVROLEMEMBER('sysadmin', name) = 0
            ORDER BY name
            """, c))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) loginy.Add((r.GetString(0), r.GetBoolean(1), (byte[])r[2]));

        var odczyt = loginy.ToDictionary(l => l.Nazwa, _ => 0);
        foreach (var baza in bazy)
        {
            if (Convert.ToInt32(await Skalar(c, $"SELECT CASE WHEN DB_ID(N'{baza}') IS NULL THEN 0 ELSE 1 END", ct)) == 0) continue;
            var sidy = new List<byte[]>();
            await using (var cmd = new SqlCommand($"""
                SELECT u.sid FROM [{baza}].sys.database_role_members m
                JOIN [{baza}].sys.database_principals r ON r.principal_id = m.role_principal_id
                JOIN [{baza}].sys.database_principals u ON u.principal_id = m.member_principal_id
                WHERE r.name IN (N'db_datareader', N'db_owner') AND u.sid IS NOT NULL
                """, c))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct)) sidy.Add((byte[])r[0]);
            foreach (var l in loginy)
                if (sidy.Any(s => s.AsSpan().SequenceEqual(l.Sid))) odczyt[l.Nazwa]++;
        }
        return loginy.Select(l => new LoginSql(l.Nazwa, l.Wylaczony, odczyt[l.Nazwa], bazy.Count)).ToList();
    }

    /// <summary>
    /// Zaklada login albo korzysta z istniejacego i nadaje mu odczyt baz instalacji.
    /// Haslo: nowy login zawsze dostaje losowe; istniejacy — tylko gdy zmienHaslo (wtedy przestaje dzialac
    /// wszystko, co dotad uzywalo tego loginu). Zwraca nowe haslo albo null, gdy haslo pozostalo bez zmian.
    /// Wolajacy musi byc administratorem SQL: logowanie Windows (adminLogin = null) albo login SQL, np. sa.
    /// </summary>
    public static async Task<string?> UtworzAsync(string serwer, string bazaBin, IEnumerable<string> bazyDane,
                                                  string? adminLogin = null, string? adminHaslo = null,
                                                  string? login = null, bool zmienHaslo = true, CancellationToken ct = default)
    {
        login ??= LoginDla(bazaBin);
        var bazy = Bazy(bazaBin, bazyDane);
        if (!CzyPoprawnaNazwa(login)) throw new ArgumentException("Login może zawierać tylko litery, cyfry i podkreślenie (do 128 znaków).");

        await using var c = await PolaczAdministratoraAsync(serwer, adminLogin, adminHaslo, ct);
        if (Convert.ToInt32(await Skalar(c, "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)", ct)) == 1)
            throw new InvalidOperationException(
                "SQL Server dopuszcza tylko logowanie Windows — włącz tryb mieszany (SQL Server i Windows) albo nadaj dostęp kontu usługi.");

        bool istnieje = await Skalar(c, $"SELECT SUSER_ID(N'{login}')", ct) is not (null or DBNull);
        if (istnieje && Convert.ToInt32(await Skalar(c, $"SELECT IS_SRVROLEMEMBER('sysadmin', N'{login}')", ct)) == 1)
            throw new InvalidOperationException($"{login} jest administratorem SQL Servera — connector potrzebuje konta tylko do odczytu.");

        string? haslo = null;
        if (!istnieje || zmienHaslo)
        {
            haslo = NoweHaslo();
            // Haslo jako literal przez QUOTENAME — nie da sie sparametryzowac CREATE LOGIN.
            await Wykonaj(c, istnieje
                ? $"""
                  DECLARE @h nvarchar(200) = QUOTENAME(@haslo, '''');
                  EXEC(N'ALTER LOGIN [{login}] WITH PASSWORD = ' + @h + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF; ALTER LOGIN [{login}] ENABLE');
                  """
                : $"""
                  DECLARE @h nvarchar(200) = QUOTENAME(@haslo, '''');
                  EXEC(N'CREATE LOGIN [{login}] WITH PASSWORD = ' + @h + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{bazaBin}]');
                  """, ct, ("@haslo", haslo));
        }

        foreach (var baza in bazy)
        {
            await Wykonaj(c, $"""
                USE [{baza}];
                IF USER_ID(N'{login}') IS NULL CREATE USER [{login}] FOR LOGIN [{login}];
                ALTER ROLE db_datareader ADD MEMBER [{login}];
                """, ct);
        }
        // Slowniki rodzajow nieobecnosci — funkcja skalarna w bazie _bin (db_datareader nie daje EXECUTE).
        await Wykonaj(c, $"USE [{bazaBin}]; GRANT EXECUTE ON dbo.fn_Kod TO [{login}];", ct);
        return haslo;
    }

    public static bool CzyPoprawnaNazwa(string login) => Nazwa.IsMatch(login);

    static List<string> Bazy(string bazaBin, IEnumerable<string> bazyDane)
    {
        var bazy = new[] { bazaBin }.Concat(bazyDane).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (bazy.Any(b => !Nazwa.IsMatch(b))) throw new ArgumentException("Niedozwolona nazwa bazy.");
        return bazy;
    }

    static async Task<SqlConnection> PolaczAdministratoraAsync(string serwer, string? adminLogin, string? adminHaslo, CancellationToken ct)
    {
        var c = new SqlConnection(Polaczenie(serwer, "master", adminLogin, adminHaslo));
        try
        {
            await c.OpenAsync(ct);
            if (Convert.ToInt32(await Skalar(c, "SELECT IS_SRVROLEMEMBER('sysadmin') | IS_SRVROLEMEMBER('securityadmin')", ct)) != 1)
                throw new UnauthorizedAccessException("To konto nie jest administratorem SQL Servera (potrzebna rola sysadmin albo securityadmin).");
            return c;
        }
        catch { await c.DisposeAsync(); throw; }
    }


    /// <summary>Skrypt T-SQL do przekazania administratorowi baz — to samo, co robi UtworzAsync.</summary>
    public static string Skrypt(string bazaBin, IEnumerable<string> bazyDane, string? login = null)
    {
        login ??= LoginDla(bazaBin);
        var bazy = new[] { bazaBin }.Concat(bazyDane).Distinct(StringComparer.OrdinalIgnoreCase);
        var s = new System.Text.StringBuilder();
        s.AppendLine($"-- Konto odczytu WU_Connector. Haslo ustaw wlasne i wpisz je w oknie connectora.");
        s.AppendLine($"CREATE LOGIN [{login}] WITH PASSWORD = N'<haslo>', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = [{bazaBin}];");
        foreach (var b in bazy)
            s.AppendLine($"USE [{b}]; CREATE USER [{login}] FOR LOGIN [{login}]; ALTER ROLE db_datareader ADD MEMBER [{login}];");
        s.AppendLine($"USE [{bazaBin}]; GRANT EXECUTE ON dbo.fn_Kod TO [{login}];");
        return s.ToString();
    }

    public static string Polaczenie(string serwer, string baza, string? login, string? haslo)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = serwer, InitialCatalog = baza, ConnectTimeout = 10, ApplicationName = "WuConnector-konfiguracja",
            Encrypt = SqlConnectionEncryptOption.Optional, TrustServerCertificate = true,
        };
        if (string.IsNullOrEmpty(login)) b.IntegratedSecurity = true;
        else { b.UserID = login; b.Password = haslo; }
        return b.ConnectionString;
    }

    /// <summary>24 znaki: litery, cyfry i znaki specjalne — spelnia polityke hasel Windows/SQL.</summary>
    static string NoweHaslo()
    {
        const string znaki = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        const string specjalne = "!#%*+-=?@";
        var h = new char[24];
        for (int i = 0; i < h.Length; i++) h[i] = znaki[RandomNumberGenerator.GetInt32(znaki.Length)];
        h[5] = specjalne[RandomNumberGenerator.GetInt32(specjalne.Length)];
        h[13] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        h[19] = specjalne[RandomNumberGenerator.GetInt32(specjalne.Length)];
        return new string(h);
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
