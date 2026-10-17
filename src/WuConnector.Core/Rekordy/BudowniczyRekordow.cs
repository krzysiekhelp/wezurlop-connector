using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WuConnector.Core.Odczyt;

namespace WuConnector.Core.Rekordy;

/// <param name="UzytkownikConnectora">USERS.X_I konta connectora w tej instalacji — wpisy z X_F7 = ta wartosc to "connector".</param>
/// <param name="SekretOsoby">Sekret instalacji do osobaId = HMAC-SHA256(sekret, PESEL). Nigdy nie opuszcza connectora.</param>
/// <param name="Dzis">Data odniesienia: kto jest zatrudniony, okno nieobecnosci i limitow.</param>
public sealed record UstawieniaBudowy(int? UzytkownikConnectora, byte[] SekretOsoby, DateTime Dzis)
{
    public int RokBiezacy => Dzis.Year;

    /// <summary>Nieobecnosci wysylamy od 1 stycznia poprzedniego roku (decyzja 19.09.2026, do zmiany tutaj).</summary>
    public DateTime PoczatekOkna => new(Dzis.Year - 1, 1, 1);
}

/// <param name="GrupyWZakresie">Id pracownikow w zakresie — dla bezpiecznika usuniec.</param>
/// <param name="Uwagi">Do dziennika connectora — bez danych osobowych, same identyfikatory.</param>
public sealed record WynikBudowy(
    IReadOnlyDictionary<string, IReadOnlyList<Rekord>> Zbiory,
    IReadOnlySet<string> GrupyWZakresie,
    DateTime PoczatekOkna,
    IReadOnlyList<string> Uwagi);

/// <summary>
/// Zamienia surowe wiersze R2 na rekordy wymiany zgodne z KSZTALT-DANYCH-R2.md.
/// Czysta funkcja: ten sam stan bazy daje te same rekordy i te same sumy.
///
/// ZAKRES (decyzja uzytkownika 19.09.2026): WYLACZNIE pracownicy aktualnie zatrudnieni na umowe o prace
/// — i tylko ich zatrudnienia, limity i nieobecnosci. Zleceniobiorcy, zwolnieni i kartoteki skasowane
/// nie opuszczaja connectora. Kto wypada z zakresu (zwolnienie), jego rekordy ida do serwera jako usuniete.
/// </summary>
public static class BudowniczyRekordow
{
    static readonly Regex WzorRequestId = new("^[A-Za-z0-9-]{1,32}$", RegexOptions.CultureInvariant);
    static readonly TimeZoneInfo StrefaPL = Strefa();

    /// <summary>
    /// Zatrudniony na umowe o prace. Zwolniony z data zwolnienia w przyszlosci (okres wypowiedzenia)
    /// nadal pracuje — wypada z zakresu dzien po dacie zwolnienia.
    /// </summary>
    public static bool WZakresie(PracownikWiersz p, DateTime dzis)
        => p.UmowaOPrace
        && !p.Skasowany
        && (!p.Zwolniony || (p.DataZwolnienia is DateTime dz && dz.Date >= dzis.Date));

    public static WynikBudowy Zbuduj(SuroweDane d, UstawieniaBudowy u)
    {
        if (u.SekretOsoby is null || u.SekretOsoby.Length < 16)
            throw new ArgumentException("Sekret osobaId musi miec co najmniej 16 bajtow.");

        var uwagi = new List<string>();
        var zbiory = new Dictionary<string, IReadOnlyList<Rekord>>();

        zbiory[Zbiory.Firma] = new[] { new Rekord(Zbiory.Firma, d.Firma.Nip, Pola(("nazwa", d.Firma.Nazwa))) };
        // Powiazanie rodzaj → limit liczymy z sald tej firmy (PuleLimitow), nie wpisujemy na sztywno.
        var pule = PuleLimitow.Wylicz(d.Urlopy);
        zbiory[Zbiory.Slownik] = d.Slownik.Select(s => Slownik(s, pule)).ToList();

        var kalendarze = d.Kalendarze.ToDictionary(k => k.Id);
        zbiory[Zbiory.Kalendarz] = d.LataKal
            .Where(l => kalendarze.ContainsKey(l.KalendarzId))
            .OrderBy(l => l.KalendarzId).ThenBy(l => l.Rok)
            .Select(l => Kalendarz(l, kalendarze[l.KalendarzId]))
            .ToList();

        // --- zakres
        var pracownicy = d.Pracownicy.Where(p => WZakresie(p, u.Dzis)).ToDictionary(p => p.Id);
        var zatrudnienia = d.Zatrudnienia.Where(z => z.Aktualne && pracownicy.ContainsKey(z.PracownikId)).ToDictionary(z => z.Id);

        var bezZatrudnienia = pracownicy.Keys.Except(zatrudnienia.Values.Select(z => z.PracownikId)).Count();
        if (bezZatrudnienia > 0)
            uwagi.Add($"pracownicy w zakresie bez aktualnego zatrudnienia: {bezZatrudnienia}");

        zbiory[Zbiory.Pracownik] = pracownicy.Values.OrderBy(p => p.Id).Select(p => Pracownik(p, u.SekretOsoby)).ToList();
        zbiory[Zbiory.Zatrudnienie] = zatrudnienia.Values.OrderBy(z => z.Id).Select(Zatrudnienie).ToList();
        zbiory[Zbiory.Limit] = Limity(d, zatrudnienia, u, pule, uwagi);
        zbiory[Zbiory.Nieobecnosc] = Nieobecnosci(d, zatrudnienia, u);

        var grupy = pracownicy.Keys.Select(Id).ToHashSet(StringComparer.Ordinal);
        return new WynikBudowy(zbiory, grupy, u.PoczatekOkna, uwagi);
    }

    // ------------------------------------------------------------------ zbiory

    static Rekord Slownik(SlownikWiersz s, IReadOnlyDictionary<int, PowiazanieLimitu> pule)
    {
        bool urlop = s.Zrodlo == "urlop";
        // Limity prowadzi wylacznie karta urlopowa — nieobecnosci kadrowe i zasilkowe nie obciazaja zadnej puli.
        var p = urlop ? pule.GetValueOrDefault(s.Kod) : null;
        return new Rekord(Zbiory.Slownik, $"{s.Zrodlo}:{s.Kod}", Pola(
            ("zrodlo", s.Zrodlo),
            ("kod", s.Kod),
            ("nazwa", s.Nazwa),
            ("tylkoOdczyt", !urlop),
            ("doWniosku", urlop && (s.Kod == 0 || s.Kod == 5)),
            ("nieobecnosc", !(urlop && (s.Kod == 6 || s.Kod == 8))),
            ("obciazaLimit", p?.Pula),
            ("obciazaLimitZrodlo", p is null ? null : p.ZDanych ? "dane" : "domyslne")));
    }

    static Rekord Kalendarz(LataKalWiersz l, KalendarzWiersz k)
        => new(Zbiory.Kalendarz, $"{l.KalendarzId}:{l.Rok}", Pola(
            ("kalendarzId", l.KalendarzId.ToString(CultureInfo.InvariantCulture)),
            ("nazwa", k.Nazwa?.Trim() ?? ""),
            ("rok", l.Rok),
            ("indywidualne", k.Indywidualne),
            ("normaUrlopowaMin", k.NormaUrlopowaMin),
            ("normaDobowaMin", k.NormaDobowaMin),
            ("dniNierobocze", new JsonArray(KalendarzR2.DniNierobocze(l).Select(x => (JsonNode?)Data(x)).ToArray()))));

    static Rekord Pracownik(PracownikWiersz p, byte[] sekret)
        => new(Zbiory.Pracownik, Id(p.Id), Pola(
            ("osobaId", p.Pesel is null ? null : OsobaId(sekret, p.Pesel)),
            ("nazwisko", p.Nazwisko.Trim()),
            ("imie", p.Imie.Trim()),
            ("identyfikator", p.Identyfikator),
            ("zwolniony", p.Zwolniony),
            ("dataZwolnienia", DataN(p.DataZwolnienia)),
            ("skasowany", p.Skasowany),
            ("umowy", new JsonObject
            {
                ["oPrace"] = p.UmowaOPrace,
                ["zlecenie"] = p.UmowaZlecenie,
                ["oDzielo"] = p.UmowaODzielo,
            })),
            grupa: Id(p.Id));

    static Rekord Zatrudnienie(ZatrudnienieWiersz z)
    {
        double? etat = z.EtatLicznik is double l && z.EtatMianownik is double m && m != 0
            ? Math.Round(l / m, 4, MidpointRounding.AwayFromZero)
            : null;
        return new Rekord(Zbiory.Zatrudnienie, Id(z.Id), Pola(
            ("pracownikId", Id(z.PracownikId)),
            ("aktualne", z.Aktualne),
            ("domyslne", z.Domyslne),
            ("etat", etat),
            ("dataRozpoczecia", DataN(z.DataRozpoczecia)),
            ("dataZakonczenia", DataN(z.DataZakonczenia)),
            ("kalendarzId", z.KalendarzId is int k ? Id(k) : null)),
            grupa: Id(z.PracownikId));
    }

    static List<Rekord> Limity(SuroweDane d, Dictionary<int, ZatrudnienieWiersz> zatrudnienia, UstawieniaBudowy u,
                               IReadOnlyDictionary<int, PowiazanieLimitu> pule, List<string> uwagi)
    {
        var wynik = new List<Rekord>();
        int rokOd = u.RokBiezacy - 1, rokDo = u.RokBiezacy + 1;
        var wierszeZatr = d.Urlopy.Where(w => w.ZatrudnienieId.HasValue && zatrudnienia.ContainsKey(w.ZatrudnienieId.Value))
                                  .ToLookup(w => w.ZatrudnienieId!.Value);

        foreach (var zatr in zatrudnienia.Values)
        {
            var wiersze = wierszeZatr[zatr.Id].ToList();
            string grupa = Id(zatr.PracownikId);

            // Wypoczynkowy: lata z wierszem limitu w oknie + zawsze rok biezacy (naliczony=false, gdy kadry jeszcze nie zalozyly).
            var lata = wiersze.Where(w => w.L && w.RDnia == 0 && w.Od.HasValue)
                              .Select(w => w.Od!.Value.Year).Where(r => r >= rokOd && r <= rokDo).ToHashSet();
            lata.Add(u.RokBiezacy);

            foreach (int rok in lata.OrderBy(r => r))
            {
                var s = Saldo.Policz(wiersze, rok, 0, PuleLimitow.ZuzywajacePule(pule, 0));
                int? naZadanie = s.Naliczony
                    ? wiersze.Where(w => !w.L && w.RDnia == 5 && w.Od?.Year == rok).Sum(w => w.IlDni ?? 0)
                    : null;
                wynik.Add(LimitRekord(zatr.Id, rok, "wypoczynkowy", s, naZadanie, grupa));

                if (s.Naliczony && s.SaldoNarastajaceR2 is int nar && nar != s.PozostaloMin)
                    uwagi.Add($"saldo: zatrudnienie {zatr.Id} rok {rok} formula {s.PozostaloMin} min, R2 narastajaco {nar} min");
            }

            // Opieka art. 188: tylko lata, w ktorych kadry zalozyly limit.
            foreach (int rok in wiersze.Where(w => w.L && w.RDnia == 2 && w.Od.HasValue)
                                       .Select(w => w.Od!.Value.Year).Where(r => r >= rokOd && r <= rokDo)
                                       .Distinct().OrderBy(r => r))
                wynik.Add(LimitRekord(zatr.Id, rok, "opieka_art188", Saldo.Policz(wiersze, rok, 2, PuleLimitow.ZuzywajacePule(pule, 2)), null, grupa));
        }
        return wynik.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    static Rekord LimitRekord(int zatrId, int rok, string rodzaj, WynikSalda s, int? naZadanie, string grupa)
        => new(Zbiory.Limit, $"{zatrId}:{rok}:{rodzaj}", Pola(
            ("zatrudnienieId", Id(zatrId)),
            ("rok", rok),
            ("rodzaj", rodzaj),
            ("naliczony", s.Naliczony),
            ("wymiarMin", s.WymiarMin),
            ("zaleglyMin", s.ZaleglyMin),
            ("dostepneNaStartMin", s.DostepneNaStartMin),
            ("wykorzystaneMin", s.WykorzystaneMin),
            ("korektyMin", s.KorektyMin),
            ("pozostaloMin", s.PozostaloMin),
            ("naZadanieWykorzystaneDni", naZadanie)),
            grupa, koniec: new DateTime(rok, 12, 31));

    static List<Rekord> Nieobecnosci(SuroweDane d, Dictionary<int, ZatrudnienieWiersz> zatrudnienia, UstawieniaBudowy u)
    {
        var wynik = new List<Rekord>();
        DateTime okno = u.PoczatekOkna;
        bool Bierz(int? zatr, DateTime @do) => zatr is int z && zatrudnienia.ContainsKey(z) && @do >= okno;

        foreach (var w in d.Urlopy.Where(w => !w.L && w.Od.HasValue && w.Do.HasValue && Bierz(w.ZatrudnienieId, w.Do!.Value)))
        {
            bool connector = u.UzytkownikConnectora.HasValue && w.UtworzylUzytkownik == u.UzytkownikConnectora;
            wynik.Add(Nieobecnosc("urlop", w.Id, zatrudnienia[w.ZatrudnienieId!.Value], w.RDnia, w.Od!.Value, w.Do!.Value,
                w.IlDni, w.IlGodz,
                requestId: connector && w.Opis is not null && WzorRequestId.IsMatch(w.Opis) ? w.Opis : null,
                zrodloWpisu: connector ? "connector" : "kadry",
                kontynuacja: null, w.Utworzono, w.Zmieniono));
        }

        foreach (var w in d.Kadrowe.Where(w => Bierz(w.ZatrudnienieId, w.Do)))
            wynik.Add(Nieobecnosc("kadrowe", w.Id, zatrudnienia[w.ZatrudnienieId!.Value], w.Rodzaj, w.Od, w.Do,
                w.IlDni, w.IlGodz, null, "kadry", null, w.Utworzono, w.Zmieniono));

        // Karta zasilkowa: minuty zawsze null — nie ma ich, a wiecej niz rodzaj i daty nie wysylamy.
        foreach (var w in d.Zasilki.Where(w => Bierz(w.ZatrudnienieId, w.Do)))
            wynik.Add(Nieobecnosc("zasilek", w.Id, zatrudnienia[w.ZatrudnienieId!.Value], w.Rodzaj, w.Od, w.Do,
                w.IlDni, null, null, "kadry", w.Kontynuacja, w.Utworzono, w.Zmieniono));

        return wynik.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    static Rekord Nieobecnosc(string zrodlo, int id, ZatrudnienieWiersz zatr, int kod, DateTime od, DateTime @do, int? dni, int? minuty,
                              string? requestId, string zrodloWpisu, bool? kontynuacja, DateTime? utw, DateTime? zm)
        => new(Zbiory.Nieobecnosc, $"{zrodlo}:{id}", Pola(
            ("zatrudnienieId", Id(zatr.Id)),
            ("rodzaj", new JsonObject { ["zrodlo"] = zrodlo, ["kod"] = kod }),
            ("dataOd", Data(od)),
            ("dataDo", Data(@do)),
            ("dni", dni),
            ("minuty", minuty),
            ("requestId", requestId),
            ("zrodloWpisu", zrodloWpisu),
            ("kontynuacja", kontynuacja),
            ("utworzono", Znacznik(utw)),
            ("zmieniono", Znacznik(zm))),
            grupa: Id(zatr.PracownikId), koniec: @do);

    // ------------------------------------------------------------------ pomocnicze

    /// <summary>osobaId = "hmac:" + HMAC-SHA256(sekret, PESEL). Surowy SHA z PESEL bylby odwracalny.</summary>
    public static string OsobaId(byte[] sekret, string pesel)
        => "hmac:" + Convert.ToHexString(HMACSHA256.HashData(sekret, Encoding.UTF8.GetBytes(pesel.Trim()))).ToLowerInvariant();

    static string Id(int x) => x.ToString(CultureInfo.InvariantCulture);
    static string Data(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static string? DataN(DateTime? d) => d.HasValue ? Data(d.Value) : null;

    /// <summary>R2 trzyma czas lokalny bez strefy — przeliczamy na Europe/Warsaw z przesunieciem.</summary>
    static string? Znacznik(DateTime? t)
    {
        if (t is null) return null;
        var lokalny = DateTime.SpecifyKind(t.Value, DateTimeKind.Unspecified);
        return new DateTimeOffset(lokalny, StrefaPL.GetUtcOffset(lokalny))
            .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    static TimeZoneInfo Strefa()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); }
    }

    static IEnumerable<KeyValuePair<string, JsonNode?>> Pola(params (string Nazwa, object? Wartosc)[] pola)
        => pola.Select(p => new KeyValuePair<string, JsonNode?>(p.Nazwa, p.Wartosc switch
        {
            null => null,
            JsonNode n => n,
            string s => JsonValue.Create(s),
            int i => JsonValue.Create(i),
            double x => JsonValue.Create(x),
            bool b => JsonValue.Create(b),
            _ => throw new ArgumentException($"Nieobslugiwany typ pola {p.Nazwa}: {p.Wartosc.GetType()}"),
        }));
}
