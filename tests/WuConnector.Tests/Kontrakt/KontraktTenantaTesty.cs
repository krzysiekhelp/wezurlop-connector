using System.Text.Json.Nodes;
using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;

namespace WuConnector.Tests.Kontrakt;

/// <summary>
/// Rekordy connectora sprawdzane schematem tenanta (openapi-sync-v1.json, tura 9) — nie nasza interpretacja
/// dokumentu, tylko jego maszynowy opis. Plik kopiujemy bez zmian; nowa wersja od tenanta = podmiana pliku.
/// </summary>
public class KontraktTenantaTesty
{
    static readonly Dictionary<string, string> Schemat = new()
    {
        [Zbiory.Firma] = "RekordFirma", [Zbiory.Pracownik] = "RekordPracownik", [Zbiory.Zatrudnienie] = "RekordZatrudnienie",
        [Zbiory.Limit] = "RekordLimit", [Zbiory.Nieobecnosc] = "RekordNieobecnosc", [Zbiory.Slownik] = "RekordSlownik",
        [Zbiory.Kalendarz] = "RekordKalendarz",
    };

    static readonly WalidatorOpenApi Walidator = WalidatorOpenApi.Tenanta();

    /// <summary>Tresc przez serializacje — tak, jak ja zobaczy serwer.</summary>
    static JsonNode Jak(JsonNode n) => JsonNode.Parse(n.ToJsonString())!;

    static List<string> Sprawdz(WynikBudowy w)
    {
        var bledy = new List<string>();
        foreach (var (zbior, rekordy) in w.Zbiory)
            foreach (var r in rekordy)
                bledy.AddRange(Walidator.Sprawdz(Schemat[zbior], Jak(r.Tresc)).Select(b => $"{zbior} {r.Id}: {b}"));
        return bledy;
    }

    static byte[] Miesiac(int rok, int m)
    {
        int dni = DateTime.DaysInMonth(rok, m);
        var b = new byte[dni * 2];
        for (int d = 1; d <= dni; d++)
        {
            var dz = new DateTime(rok, m, d).DayOfWeek;
            ushort v = dz == DayOfWeek.Sunday ? (ushort)9999 : dz == DayOfWeek.Saturday ? (ushort)0 : (ushort)480;
            BitConverter.GetBytes(v).CopyTo(b, (d - 1) * 2);
        }
        return b;
    }

    /// <summary>Wszystkie rodzaje rekordow: zwolnienie z data, 3/4 etatu, dwa limity w roku z korekta, trzy zrodla nieobecnosci.</summary>
    static SuroweDane Pelne()
    {
        var dzis = new DateTime(2026, 9, 19);
        return new SuroweDane(
            new FirmaWiersz("1234567009", "ALFA SPÓŁKA Z O.O."),
            new[]
            {
                new PracownikWiersz(207, "KOWALSKI", "JAN", "12", false, null, false, true, false, false, "90010112345"),
                new PracownikWiersz(208, "NOWAK", "ANNA", null, true, dzis.AddDays(20), false, true, true, false, "85050554321"),   // w wypowiedzeniu
            },
            new[]
            {
                new ZatrudnienieWiersz(207, 207, true, true, 2, 1, 1, new(2020, 1, 1), null),
                new ZatrudnienieWiersz(208, 208, true, true, 2, 3, 4, new(2021, 3, 1), dzis.AddDays(20)),
            },
            new[]
            {
                new UrlopWiersz(9000, 207, true, 0, new(2026, 1, 1), null, null, null, 12480, 18600, false, null, null, 3, null, null),
                new UrlopWiersz(9001, 207, true, 0, new(2026, 6, 1), null, null, null, 960, null, false, null, null, 3, null, null),
                new UrlopWiersz(9002, 208, true, 0, new(2026, 1, 1), null, null, null, 9360, 9360, false, null, null, 3, null, null),
                new UrlopWiersz(10001, 207, false, 0, new(2026, 10, 5), new(2026, 10, 9), 5, 2400, null, 16200, true, -120, "wu-01J9ZC8QK4T7", 6,
                                new(2026, 9, 19, 10, 9, 56), null),
                new UrlopWiersz(10002, 208, false, 5, new(2026, 3, 2), new(2026, 3, 2), 1, 360, null, null, false, null, null, 3,
                                new(2026, 3, 1, 8, 0, 0), new(2026, 3, 2, 9, 0, 0)),
            },
            new[] { new KadroweWiersz(17, 207, 4, new(2026, 4, 1), new(2026, 4, 3), 3, 1440, null, null) },
            new[]
            {
                new ZasilekWiersz(512, 207, 0, new(2026, 9, 14), new(2026, 9, 18), 5, false, null, null),
                new ZasilekWiersz(513, 207, 0, new(2026, 9, 19), new(2026, 9, 25), 7, true, null, null),
            },
            new[]
            {
                new SlownikWiersz("urlop", 0, "urlop wypoczynkowy"), new SlownikWiersz("urlop", 5, "urlop na żądanie"),
                new SlownikWiersz("kadrowe", 4, "urlop bezpłatny"), new SlownikWiersz("zasilek", 0, "choroba"),
            },
            new[] { new KalendarzWiersz(2, "Główny", false, 480, 480) },
            new[] { new LataKalWiersz(2, 2026, Enumerable.Range(1, 12).Select(m => (byte[]?)Miesiac(2026, m)).ToArray()) },
            dzis);
    }

    [Fact]
    public void Wszystkie_zbiory_zgodne_ze_schematem_tenanta()
    {
        var w = BudowniczyRekordow.Zbuduj(Pelne(), new UstawieniaBudowy(6, new byte[32], new DateTime(2026, 9, 19)));
        Assert.All(Schemat.Keys, z => Assert.NotEmpty(w.Zbiory[z]));   // test obejmuje kazdy zbior
        Assert.Empty(Sprawdz(w));
    }

    [Fact]
    public void Usuniecie_zgodne_ze_schematem()
        => Assert.Empty(Walidator.Sprawdz("Usuniecie", JsonNode.Parse("""{ "id": "urlop:12315", "usuniety": true }""")));

    [Fact]
    public void Walidator_wykrywa_bledy()   // kontrola samego walidatora — inaczej pusta lista nic nie znaczy
    {
        var zly = JsonNode.Parse("""
            { "id": "urlop:1", "suma": "sha256:xyz", "zatrudnienieId": 207, "rodzaj": { "zrodlo": "urlop", "kod": 0 },
              "dataOd": "2026-13-01", "zrodloWpisu": "kadry", "kodChoroby": "A01" }
            """);
        var b = Walidator.Sprawdz("RekordNieobecnosc", zly);
        Assert.Contains(b, x => x.Contains(".suma"));
        Assert.Contains(b, x => x.Contains(".zatrudnienieId"));
        Assert.Contains(b, x => x.Contains(".dataOd"));
        Assert.Contains(b, x => x.Contains(".dataDo: pole wymagane"));
        Assert.Contains(b, x => x.Contains(".kodChoroby: pole spoza schematu"));
    }

    /// <summary>
    /// Rekordy zbudowane z prawdziwych baz R2 kontra schemat tenanta (tylko odczyt, dane nie opuszczaja procesu).
    /// Serwer i prefiksy baz sa dana konkretnej instalacji, wiec nie ma ich w repozytorium — podaje sie je recznie:
    ///   set WU_TEST_SQL=1
    ///   set WU_TEST_SQL_SERWER=MOJ-HOST\INSTANCJA
    ///   set WU_TEST_SQL_BAZY=prefiks_pierwszej,prefiks_drugiej
    /// Bez tych zmiennych test jest pomijany (w CI nie ma serwera).
    /// </summary>
    [Fact]
    public async Task Prawdziwe_dane_zgodne_ze_schematem_tenanta()
    {
        // WU_TEST_SQL rozne od 1 = swiadome wylaczenie, pomijamy cicho. Ale WU_TEST_SQL=1 z brakujaca
        // nazwa serwera albo pusta lista baz to pomylka w konfiguracji — wtedy PADAMY, zamiast przejsc
        // na zielono nic nie sprawdzajac (patrz docs\stan.md 3.10).
        if (Environment.GetEnvironmentVariable("WU_TEST_SQL") != "1") return;
        string? serwer = Environment.GetEnvironmentVariable("WU_TEST_SQL_SERWER");
        string[] prefiksy = (Environment.GetEnvironmentVariable("WU_TEST_SQL_BAZY") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.False(string.IsNullOrWhiteSpace(serwer),
            "WU_TEST_SQL=1, ale WU_TEST_SQL_SERWER nie jest ustawione.");
        Assert.NotEmpty(prefiksy);   // WU_TEST_SQL_BAZY: prefiksy baz po przecinku

        foreach (string prefiks in prefiksy)
        {
            var d = await new CzytnikSql(new ZrodloFirmy(serwer, prefiks + "_bin", prefiks + "_dane_1", 1, null, null))
                .CzytajAsync(DateTime.Today.Year - 1, DateTime.Today.Year + 1);
            var w = BudowniczyRekordow.Zbuduj(d, new UstawieniaBudowy(6, new byte[32], DateTime.Today));
            var bledy = Sprawdz(w);
            Assert.True(bledy.Count == 0, $"{prefiks}: {bledy.Count} niezgodności, pierwsze:\n" + string.Join("\n", bledy.Take(20)));
        }
    }
}
