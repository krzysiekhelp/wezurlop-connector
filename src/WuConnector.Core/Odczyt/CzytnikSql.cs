using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace WuConnector.Core.Odczyt;

/// <summary>Gdzie czytac dane jednej firmy.</summary>
public sealed record ZrodloFirmy(
    string SerwerSql,
    string BazaBin,
    string BazaDane,
    int FirmIde,
    string? LoginSql = null,
    string? HasloSql = null);

/// <summary>
/// Odczyt WYLACZNIE przez SQL — bez sesji R2, bez biblioteki, bez wplywu na licencje.
/// Nigdy nie pisze do bazy. Jeden batch, kilka wynikow: odczyt udaje sie w calosci albo wcale.
/// </summary>
public sealed class CzytnikSql
{
    static readonly Regex BezpiecznaNazwaBazy = new("^[A-Za-z0-9_]{1,128}$", RegexOptions.CultureInvariant);

    readonly ZrodloFirmy _zrodlo;

    public CzytnikSql(ZrodloFirmy zrodlo)
    {
        // Nazwy baz ida do tekstu zapytania (fn_Kod i FIRM sa w bazie _bin) — tylko bezpieczne znaki.
        if (!BezpiecznaNazwaBazy.IsMatch(zrodlo.BazaBin) || !BezpiecznaNazwaBazy.IsMatch(zrodlo.BazaDane))
            throw new ArgumentException("Niedozwolona nazwa bazy danych.");
        _zrodlo = zrodlo;
    }

    public string ConnectionString()
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = _zrodlo.SerwerSql,
            InitialCatalog = _zrodlo.BazaDane,
            ApplicationName = "WuConnector",
            ConnectTimeout = 15,
            Encrypt = SqlConnectionEncryptOption.Optional,   // lokalna siec klienta, zwykle bez certyfikatu SQL
            TrustServerCertificate = true,
        };
        if (string.IsNullOrEmpty(_zrodlo.LoginSql)) b.IntegratedSecurity = true;
        else { b.UserID = _zrodlo.LoginSql; b.Password = _zrodlo.HasloSql; }
        return b.ConnectionString;
    }

    /// <param name="rokOd">Pierwszy rok kalendarza do odczytu.</param>
    /// <param name="rokDo">Ostatni rok kalendarza do odczytu.</param>
    public async Task<SuroweDane> CzytajAsync(int rokOd, int rokDo, CancellationToken ct = default)
    {
        await using var c = new SqlConnection(ConnectionString());
        await c.OpenAsync(ct);

        await using var cmd = c.CreateCommand();
        cmd.CommandText = Zapytanie(_zrodlo.BazaBin);
        cmd.CommandTimeout = 60;
        cmd.Parameters.Add(new SqlParameter("@firm", SqlDbType.Int) { Value = _zrodlo.FirmIde });
        cmd.Parameters.Add(new SqlParameter("@rokOd", SqlDbType.Int) { Value = rokOd });
        cmd.Parameters.Add(new SqlParameter("@rokDo", SqlDbType.Int) { Value = rokDo });

        await using var r = await cmd.ExecuteReaderAsync(ct);

        FirmaWiersz? firma = null;
        while (await r.ReadAsync(ct))
            firma = new FirmaWiersz(Str(r, 0) ?? "", Str(r, 1) ?? "");
        if (firma is null) throw new InvalidOperationException($"Brak firmy X_I={_zrodlo.FirmIde} w {_zrodlo.BazaBin}.FIRM");

        var prac = new List<PracownikWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            prac.Add(new PracownikWiersz(Int(r, 0), Str(r, 1) ?? "", Str(r, 2) ?? "", Str(r, 3),
                Bool(r, 4), Data(r, 5), Bool(r, 6), Bool(r, 7), Bool(r, 8), Bool(r, 9), Str(r, 10)));

        var zatr = new List<ZatrudnienieWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            zatr.Add(new ZatrudnienieWiersz(Int(r, 0), Int(r, 1), Bool(r, 2), Bool(r, 3), IntN(r, 4),
                Dbl(r, 5), Dbl(r, 6), Data(r, 7), Data(r, 8)));

        var url = new List<UrlopWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            url.Add(new UrlopWiersz(Int(r, 0), IntN(r, 1), Bool(r, 2), Int(r, 3), Data(r, 4), Data(r, 5),
                IntN(r, 6), IntN(r, 7), IntN(r, 8), IntN(r, 9), Bool(r, 10), IntN(r, 11), Str(r, 12),
                IntN(r, 13), Data(r, 14), Data(r, 15)));

        var kadr = new List<KadroweWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            kadr.Add(new KadroweWiersz(Int(r, 0), IntN(r, 1), Int(r, 2), Data(r, 3)!.Value, Data(r, 4)!.Value,
                IntN(r, 5), IntN(r, 6), Data(r, 7), Data(r, 8)));

        var zas = new List<ZasilekWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            zas.Add(new ZasilekWiersz(Int(r, 0), IntN(r, 1), Int(r, 2), Data(r, 3)!.Value, Data(r, 4)!.Value,
                IntN(r, 5), Bool(r, 6), Data(r, 7), Data(r, 8)));

        var slow = new List<SlownikWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            slow.Add(new SlownikWiersz(Str(r, 0)!, Int(r, 1), Str(r, 2)!.Trim()));

        var kal = new List<KalendarzWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
            kal.Add(new KalendarzWiersz(Int(r, 0), Str(r, 1), Bool(r, 2), IntN(r, 3), IntN(r, 4)));

        var lata = new List<LataKalWiersz>();
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var mies = new byte[]?[12];
            for (int m = 0; m < 12; m++) mies[m] = r.IsDBNull(2 + m) ? null : (byte[])r.GetValue(2 + m);
            lata.Add(new LataKalWiersz(Int(r, 0), Int(r, 1), mies));
        }

        string? wersjaBazy = null;
        await r.NextResultAsync(ct);
        while (await r.ReadAsync(ct)) wersjaBazy = Str(r, 0);

        return new SuroweDane(firma, prac, zatr, url, kadr, zas, slow, kal, lata, DateTime.Now, wersjaBazy);
    }

    // Kolejnosc wynikow = kolejnosc odczytu w CzytajAsync.
    static string Zapytanie(string bin) => $"""
        SET NOCOUNT ON;
        SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

        -- 1. firma
        SELECT REPLACE(REPLACE(LTRIM(RTRIM(f.NIP)), '-', ''), ' ', ''), f.Nazwa
        FROM [{bin}].dbo.FIRM f WHERE f.X_I = @firm;

        -- 2. pracownicy. PESEL tylko do HMAC w pamieci i TYLKO dla mozliwie zatrudnionych na umowe o prace
        --    (luzny warunek: dokladny zakres wyznacza BudowniczyRekordow.WZakresie). Pozostalych nie czytamy wcale.
        SELECT p.X_I, p.Nazwisko, p.Imie, NULLIF(LTRIM(RTRIM(p.Identyfikator)), ''),
               ISNULL(p.Zwolniony, 0), p.Data_zwolnienia, ISNULL(p.Skasowany, 0),
               ISNULL(p.Umowa_o_prace, 0), ISNULL(p.Umowa_zlecenie, 0), ISNULL(p.Umowa_o_dzielo, 0),
               CASE WHEN ISNULL(p.Umowa_o_prace, 0) = 1 AND ISNULL(p.Skasowany, 0) = 0
                         AND (ISNULL(p.Zwolniony, 0) = 0 OR p.Data_zwolnienia >= DATEADD(day, -1, CAST(GETDATE() AS date)))
                    THEN (SELECT TOP 1 NULLIF(LTRIM(RTRIM(PESEL)), '') FROM PRACDANE WHERE X_IPracownik = p.X_I)
               END
        FROM PRACOWNK p;

        -- 3. zatrudnienia (nieskasowane) + ostatnia umowa o prace
        WITH um AS (
            SELECT X_IAktZatrudnienie, Etat_licznik, Etat_mianownik, Data_rozpoczecia_pracy, Data_zakonczenia_pracy,
                   ROW_NUMBER() OVER (PARTITION BY X_IAktZatrudnienie
                                      ORDER BY Data_obowiaz_nowych_war DESC, Data_zawarcia_umowy DESC, X_I DESC) nr
            FROM UMPRACA)
        SELECT h.X_I, h.X_IPracownik, ISNULL(h.AktZatrudnienie, 0), ISNULL(h.X_Default, 0), NULLIF(h.X_IKalendarz, 0),
               um.Etat_licznik, um.Etat_mianownik, um.Data_rozpoczecia_pracy, um.Data_zakonczenia_pracy
        FROM HISTORIA h
        LEFT JOIN um ON um.X_IAktZatrudnienie = h.X_I AND um.nr = 1
        WHERE ISNULL(h.Skasowany, 0) = 0 AND h.X_IPracownik IS NOT NULL;

        -- 4. karta urlopowa: okresy i limity
        SELECT k.X_I, k.X_I_HISTORII, ISNULL(k.L, 0), ISNULL(k.R_DNIA, 0), k.OD, k.DO, k.IL_DNI, k.IL_GODZ,
               k.LIMIT_GODZ, k.POZOSTALO_GODZ, ISNULL(k.KOR_POZOST, 0), k.KOR_POZOST_GODZ,
               NULLIF(LTRIM(RTRIM(k.OPIS)), ''), k.X_F7, k.X_F7DataCzas, k.X_F6DataCzas
        FROM KART_URL k;

        -- 5. nieobecnosci kadrowe
        SELECT o.X_I, o.X_IHistoria, o.Rodzaj_nieobecnosci, o.Od, o.Do, o.IlDni, o.IL_GODZ, o.X_F7DataCzas, o.X_F6DataCzas
        FROM OKRESHIS o
        WHERE ISNULL(o.Anuluj, 0) = 0 AND o.Od IS NOT NULL AND o.Do IS NOT NULL;

        -- 6. karta zasilkowa — WYLACZNIE rodzaj i daty (bez KOD_CHOR*, SERIA, NR, kwot, Uwag)
        SELECT z.X_I, z.X_I_HISTORII, z.R_NIEOB, z.OKRES_OD, z.OKRES_DO, z.IL_DNI_ZAS, ISNULL(z.KONTYNUACJA, 0),
               z.X_F7DataCzas, z.X_F6DataCzas
        FROM KART_ZAS z
        WHERE z.OKRES_OD IS NOT NULL AND z.OKRES_DO IS NOT NULL;

        -- 7. slowniki: fn_Kod zwraca pusty tekst poza zakresem
        WITH n AS (SELECT TOP 64 ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS k FROM sys.all_objects),
             s AS (
                SELECT 'urlop' zrodlo, n.k, [{bin}].dbo.fn_Kod('KART_URL', 'R_DNIA', n.k) nazwa FROM n
                UNION ALL SELECT 'kadrowe', n.k, [{bin}].dbo.fn_Kod('OKRESHIS', 'Rodzaj_nieobecnosci', n.k) FROM n
                UNION ALL SELECT 'zasilek', n.k, [{bin}].dbo.fn_Kod('KART_ZAS', 'R_NIEOB', n.k) FROM n)
        SELECT zrodlo, k, nazwa FROM s WHERE LEN(ISNULL(nazwa, '')) > 0 ORDER BY zrodlo, k;

        -- 8. kalendarze
        -- DZIEN = norma dnia urlopowego (dzielnik minut na dni), DobowaNormaCzPr = dlugosc zmiany (informacyjnie)
        SELECT X_I, NAZWA, ISNULL(Indywidualne, 0),
               CASE WHEN DZIEN > 0 THEN CAST(ROUND(DZIEN * 60, 0) AS int) END,
               CASE WHEN DobowaNormaCzPr > 0 THEN CAST(ROUND(DobowaNormaCzPr * 60, 0) AS int) END
        FROM KALENDAR;

        -- 9. dni kalendarza w zakresie lat
        SELECT X_I_KALENDARZA, ROK, STYCZEN, LUTY, MARZEC, KWIECIEN, MAJ, CZERWIEC,
               LIPIEC, SIERPIEN, WRZESIEN, PAZDZIERNIK, LISTOPAD, GRUDZIEN
        FROM LATA_KAL WHERE ROK BETWEEN @rokOd AND @rokDo;

        -- 10. wersja schematu bazy (ostatnia migracja) — dobor biblioteki R2 do bazy
        SELECT TOP 1 CONCAT(VER_MAJOR, '.', VER_MINOR, '.', VER_COMPILE) FROM [{bin}].dbo.VERSION ORDER BY X_I DESC;
        """;

    static string? Str(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
    static int Int(SqlDataReader r, int i) => Convert.ToInt32(r.GetValue(i));
    static int? IntN(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));
    static double? Dbl(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i));
    static bool Bool(SqlDataReader r, int i) => !r.IsDBNull(i) && Convert.ToBoolean(r.GetValue(i));
    static DateTime? Data(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDateTime(r.GetValue(i));
}
