using Microsoft.Data.Sqlite;
using WuConnector.Core.Rekordy;

namespace WuConnector.Core.Stan;

/// <param name="DoWyslania">Rekordy nowe i zmienione (w trybie pelnym — wszystkie).</param>
/// <param name="Usuniete">Id obecne w potwierdzonym stanie, ktorych nie ma juz w odczycie. Puste, gdy usuniecia wstrzymane.</param>
/// <param name="UsunieciaWstrzymane">Bezpiecznik: podejrzane usuniecia — wymaga czlowieka.</param>
/// <param name="UsunieciaWykryte">Ile usuniec wykryto (takze gdy wstrzymane).</param>
/// <param name="Wspolne">Ile id z potwierdzonego stanu jest tez w biezacym odczycie (0 przy podmianie calej bazy).</param>
/// <param name="UsunieciaNiewyjasnione">Usuniecia, ktorych nie tlumaczy zwolnienie pracownika ani wyjscie z okna dat.</param>
public sealed record Roznice(
    string Nip, string Zbior,
    IReadOnlyList<Rekord> DoWyslania, IReadOnlyList<string> Usuniete,
    int Nowe, int Zmienione, int BezZmian,
    int Wspolne,
    bool UsunieciaWstrzymane, int UsunieciaWykryte, int UsunieciaNiewyjasnione, int StanPotwierdzony);

/// <summary>Co moze legalnie zniknac: pracownicy spoza zakresu i rekordy konczace sie przed oknem.</summary>
public sealed record ZakresPorownania(IReadOnlySet<string> GrupyWZakresie, DateTime PoczatekOkna);

/// <summary>
/// Lokalny stan connectora: co serwer wezurlop POTWIERDZIL (id + suma per firma i zbior) oraz kursory.
/// Bez tresci rekordow — tresc zawsze pochodzi z biezacego odczytu R2.
/// Stan aktualizowany wylacznie po potwierdzeniu paczki przez serwer.
/// </summary>
public sealed class MagazynStanu : IDisposable
{
    /// <summary>Zbiory chronione bezpiecznikiem. Slownik, kalendarz i firma nie znikaja z powodu blednego odczytu masowo.</summary>
    public static readonly HashSet<string> ZbioryZBezpiecznikiem = new()
    {
        Zbiory.Pracownik, Zbiory.Zatrudnienie, Zbiory.Limit, Zbiory.Nieobecnosc,
    };

    /// <summary>Niewyjasnionych usuniec wiecej niz max(prog, procent stanu) → wstrzymane.</summary>
    public int ProgUsuniecBezwzgledny { get; init; } = 20;
    public double ProgUsuniecProcent { get; init; } = 0.05;

    /// <summary>
    /// Odczyt dal mniej niz ten ulamek potwierdzonego stanu → wstrzymane, nawet jesli „wyjasnione".
    /// Chroni przed zla baza / pustym odczytem, w ktorym znikaja wszyscy pracownicy naraz.
    /// </summary>
    public double ProgSpadku { get; init; } = 0.5;
    public int MinStanDlaProguSpadku { get; init; } = 10;

    readonly SqliteConnection _c;

    public MagazynStanu(string sciezkaPliku)
    {
        _c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sciezkaPliku,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _c.Open();
        Wykonaj("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS stan (
                nip TEXT NOT NULL, zbior TEXT NOT NULL, id TEXT NOT NULL,
                suma TEXT NOT NULL, grupa TEXT, koniec TEXT, potwierdzono TEXT NOT NULL,
                PRIMARY KEY (nip, zbior, id)) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS kursor (
                nip TEXT NOT NULL, zbior TEXT NOT NULL, kursor TEXT,
                PRIMARY KEY (nip, zbior)) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS firma (
                klucz TEXT NOT NULL PRIMARY KEY, nip TEXT NOT NULL, nazwa TEXT, odrzucona INTEGER NOT NULL DEFAULT 0) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS zadanie (
                nip TEXT NOT NULL, request_id TEXT NOT NULL,
                status TEXT NOT NULL, wynik TEXT NOT NULL, wyslany INTEGER NOT NULL DEFAULT 0,
                zapisano TEXT NOT NULL,
                PRIMARY KEY (nip, request_id)) WITHOUT ROWID;
            """);
        // Starsze pliki stanu — tabela firma bez kolumny odrzucona.
        using (var cmd = _c.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('firma') WHERE name = 'odrzucona'";
            if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) Wykonaj("ALTER TABLE firma ADD COLUMN odrzucona INTEGER NOT NULL DEFAULT 0");
        }
    }

    // ------------------------------------------------------------------ firmy

    /// <summary>
    /// Administrator wezurlop odrzucil zgloszenie firmy (tura 8). Kolumna odrzucona = czas odrzucenia (sekundy Unix),
    /// 0 = nie; 1 = zapis ze starszej wersji (czas nieznany). Connector nie zgłasza takiej firmy co cykl,
    /// ale raz na dobe sprawdza, czy administrator nie cofnal decyzji.
    /// </summary>
    public void UstawOdrzucenie(string klucz, string nip, bool odrzucona, DateTimeOffset? kiedy = null)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "INSERT INTO firma (klucz, nip, odrzucona) VALUES ($k, $n, $o) ON CONFLICT (klucz) DO UPDATE SET odrzucona = excluded.odrzucona";
        cmd.Parameters.AddWithValue("$k", klucz);
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$o", odrzucona ? (kiedy ?? DateTimeOffset.Now).ToUnixTimeSeconds() : 0L);
        cmd.ExecuteNonQuery();
    }

    public bool CzyOdrzucona(string klucz) => OdrzuconaOd(klucz) is not null;

    /// <summary>Kiedy firme odrzucono (null = nie odrzucona; DateTimeOffset.MinValue = czas nieznany).</summary>
    public DateTimeOffset? OdrzuconaOd(string klucz)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT odrzucona FROM firma WHERE klucz = $k";
        cmd.Parameters.AddWithValue("$k", klucz);
        long v = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        return v switch { 0 => null, 1 => DateTimeOffset.MinValue, _ => DateTimeOffset.FromUnixTimeSeconds(v) };
    }

    /// <summary>NIP firmy (klucz: instalacja + FIRM.X_I) — zeby po restarcie bez dostepu do bazy zameldowac serwerowi problem.</summary>
    public void ZapamietajFirme(string klucz, string nip, string nazwa)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "INSERT INTO firma (klucz, nip, nazwa) VALUES ($k, $n, $z) ON CONFLICT (klucz) DO UPDATE SET nip = excluded.nip, nazwa = excluded.nazwa";
        cmd.Parameters.AddWithValue("$k", klucz);
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$z", nazwa);
        cmd.ExecuteNonQuery();
    }

    public string? NipFirmy(string klucz)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT nip FROM firma WHERE klucz = $k";
        cmd.Parameters.AddWithValue("$k", klucz);
        return cmd.ExecuteScalar() as string;
    }

    // ------------------------------------------------------------------ rejestr zadan

    /// <summary>
    /// Zapamietuje wynik zadania PRZED wyslaniem go do serwera. Wynik zyje tu, dopoki serwer go nie potwierdzi —
    /// zerwane lacze albo restart uslugi nie gubi zapisu wykonanego w R2.
    /// </summary>
    public void ZapiszWynik(string nip, string requestId, string status, string wynikJson)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO zadanie (nip, request_id, status, wynik, wyslany, zapisano) VALUES ($n, $r, $s, $w, 0, $t)
            ON CONFLICT (nip, request_id) DO UPDATE SET status = excluded.status, wynik = excluded.wynik, wyslany = 0, zapisano = excluded.zapisano
            """;
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$r", requestId);
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$w", wynikJson);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Zapisany wynik zadania (status, JSON) albo null, gdy zadania nie wykonywano.</summary>
    public (string Status, string Wynik)? Wynik(string nip, string requestId)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT status, wynik FROM zadanie WHERE nip = $n AND request_id = $r";
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$r", requestId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1)) : null;
    }

    public List<string> NiewyslaneWyniki(string nip)
    {
        var l = new List<string>();
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT wynik FROM zadanie WHERE nip = $n AND wyslany = 0 ORDER BY zapisano";
        cmd.Parameters.AddWithValue("$n", nip);
        using var r = cmd.ExecuteReader();
        while (r.Read()) l.Add(r.GetString(0));
        return l;
    }

    public void OznaczWyslane(string nip, IEnumerable<string> requestIds)
    {
        using var tx = _c.BeginTransaction();
        using var cmd = _c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE zadanie SET wyslany = 1 WHERE nip = $n AND request_id = $r";
        cmd.Parameters.AddWithValue("$n", nip);
        var pr = cmd.Parameters.Add("$r", SqliteType.Text);
        foreach (var id in requestIds) { pr.Value = id; cmd.ExecuteNonQuery(); }
        tx.Commit();
    }

    public Dictionary<string, string> Kursory(string nip)
    {
        var d = new Dictionary<string, string>();
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT zbior, kursor FROM kursor WHERE nip = $n AND kursor IS NOT NULL";
        cmd.Parameters.AddWithValue("$n", nip);
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return d;
    }

    public Roznice Porownaj(string nip, string zbior, IReadOnlyList<Rekord> biezace, bool trybPelny = false, ZakresPorownania? zakres = null)
    {
        var stan = Wczytaj(nip, zbior);
        var doWyslania = new List<Rekord>();
        int nowe = 0, zmienione = 0, bez = 0;
        var biezaceId = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in biezace)
        {
            biezaceId.Add(r.Id);
            if (!stan.TryGetValue(r.Id, out var s)) { nowe++; doWyslania.Add(r); }
            else if (s.Suma != r.Suma) { zmienione++; doWyslania.Add(r); }
            else { bez++; if (trybPelny) doWyslania.Add(r); }
        }

        var usuniete = stan.Where(kv => !biezaceId.Contains(kv.Key))
                           .OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();

        int niewyjasnione = usuniete.Count(kv => !Wyjasnione(kv.Value, zakres));

        bool wstrzymane = false;
        if (usuniete.Count > 0 && ZbioryZBezpiecznikiem.Contains(zbior))
        {
            double prog = Math.Max(ProgUsuniecBezwzgledny, ProgUsuniecProcent * stan.Count);
            bool zaDuzoNiewyjasnionych = niewyjasnione > prog;
            bool gwaltownySpadek = stan.Count >= MinStanDlaProguSpadku && biezace.Count < ProgSpadku * stan.Count;
            wstrzymane = zaDuzoNiewyjasnionych || gwaltownySpadek;
        }

        return new Roznice(nip, zbior, doWyslania,
                           wstrzymane ? Array.Empty<string>() : usuniete.Select(kv => kv.Key).ToList(),
                           nowe, zmienione, bez, zmienione + bez, wstrzymane, usuniete.Count, niewyjasnione, stan.Count);
    }

    /// <summary>Pracownik wypadl z zakresu (zwolnienie) albo rekord skonczyl sie przed oknem wysylki.</summary>
    static bool Wyjasnione(Zapamietany z, ZakresPorownania? zakres)
    {
        if (zakres is null) return false;
        if (z.Grupa is not null && !zakres.GrupyWZakresie.Contains(z.Grupa)) return true;
        if (z.Koniec is DateTime k && k < zakres.PoczatekOkna) return true;
        return false;
    }

    /// <summary>Po potwierdzeniu paczki przez serwer: zapamietuje wyslane sumy i zapomina usuniete id.</summary>
    public void Potwierdz(string nip, string zbior, IEnumerable<Rekord> wyslane, IEnumerable<string> usuniete)
    {
        string teraz = DateTimeOffset.Now.ToString("O");
        using var tx = _c.BeginTransaction();

        using (var up = _c.CreateCommand())
        {
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO stan (nip, zbior, id, suma, grupa, koniec, potwierdzono) VALUES ($n, $z, $i, $s, $g, $k, $t)
                ON CONFLICT (nip, zbior, id) DO UPDATE SET
                    suma = excluded.suma, grupa = excluded.grupa, koniec = excluded.koniec, potwierdzono = excluded.potwierdzono
                """;
            var pn = up.Parameters.Add("$n", SqliteType.Text); pn.Value = nip;
            var pz = up.Parameters.Add("$z", SqliteType.Text); pz.Value = zbior;
            var pi = up.Parameters.Add("$i", SqliteType.Text);
            var ps = up.Parameters.Add("$s", SqliteType.Text);
            var pg = up.Parameters.Add("$g", SqliteType.Text);
            var pk = up.Parameters.Add("$k", SqliteType.Text);
            var pt = up.Parameters.Add("$t", SqliteType.Text); pt.Value = teraz;
            foreach (var r in wyslane)
            {
                pi.Value = r.Id;
                ps.Value = r.Suma;
                pg.Value = (object?)r.Grupa ?? DBNull.Value;
                pk.Value = r.Koniec is DateTime k ? k.ToString("yyyy-MM-dd") : DBNull.Value;
                up.ExecuteNonQuery();
            }
        }

        using (var del = _c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM stan WHERE nip = $n AND zbior = $z AND id = $i";
            del.Parameters.AddWithValue("$n", nip);
            del.Parameters.AddWithValue("$z", zbior);
            var pi = del.Parameters.Add("$i", SqliteType.Text);
            foreach (var id in usuniete) { pi.Value = id; del.ExecuteNonQuery(); }
        }

        tx.Commit();
    }

    public string? Kursor(string nip, string zbior)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT kursor FROM kursor WHERE nip = $n AND zbior = $z";
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$z", zbior);
        return cmd.ExecuteScalar() as string;
    }

    public void UstawKursor(string nip, string zbior, string? kursor)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO kursor (nip, zbior, kursor) VALUES ($n, $z, $k)
            ON CONFLICT (nip, zbior) DO UPDATE SET kursor = excluded.kursor
            """;
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$z", zbior);
        cmd.Parameters.AddWithValue("$k", (object?)kursor ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public int Liczba(string nip, string zbior)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stan WHERE nip = $n AND zbior = $z";
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$z", zbior);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    sealed record Zapamietany(string Suma, string? Grupa, DateTime? Koniec);

    Dictionary<string, Zapamietany> Wczytaj(string nip, string zbior)
    {
        var d = new Dictionary<string, Zapamietany>(StringComparer.Ordinal);
        using var cmd = _c.CreateCommand();
        cmd.CommandText = "SELECT id, suma, grupa, koniec FROM stan WHERE nip = $n AND zbior = $z";
        cmd.Parameters.AddWithValue("$n", nip);
        cmd.Parameters.AddWithValue("$z", zbior);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            d[r.GetString(0)] = new Zapamietany(
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3), System.Globalization.CultureInfo.InvariantCulture));
        return d;
    }

    void Wykonaj(string sql)
    {
        using var cmd = _c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _c.Dispose();
}
