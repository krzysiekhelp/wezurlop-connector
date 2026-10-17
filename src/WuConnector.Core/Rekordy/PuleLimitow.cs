using WuConnector.Core.Odczyt;

namespace WuConnector.Core.Rekordy;

/// <param name="Pula">Rodzaj limitu obciazany przez ten rodzaj nieobecnosci (null = zaden).</param>
/// <param name="ZDanych">true = policzone z sald tej firmy; false = zachowanie programu, rodzaj u klienta nieuzywany.</param>
public sealed record PowiazanieLimitu(string? Pula, bool ZDanych);

/// <summary>
/// Ktory rodzaj urlopu obciaza ktory limit — WYLICZANE Z DANYCH KLIENTA, nie wpisane na sztywno.
///
/// Program kadrowy nie trzyma tego powiazania w slowniku (tabela kodow to tylko wartosc → tekst),
/// ale zapisuje przy kazdym okresie saldo PO jego odjeciu. Dlatego kazdy okres da sie dopiac do
/// lancucha konkretnego wiersza limitu: saldo_poprzednie − IL_GODZ + korekta = saldo_okresu.
/// Rodzaj limitu z dopietego lancucha jest odpowiedzia.
///
/// Zweryfikowane na 8 141 wierszach dwoch firm: 0/5/6 → wypoczynkowy, 2 → opiekunczy,
/// 1 (okolicznosciowy) nie obciaza zadnej puli. Domyslne sa uzywane tylko tam, gdzie danych brak.
/// </summary>
public static class PuleLimitow
{
    /// <summary>Nazwy pul takie jak w rekordach zbioru `limit` (R_DNIA wiersza limitu → nazwa).</summary>
    public static readonly IReadOnlyDictionary<int, string> NazwyPul = new Dictionary<int, string>
    {
        [0] = "wypoczynkowy",
        [2] = "opieka_art188",
    };

    /// <summary>Zachowanie programu kadrowego dla rodzajow, ktorych u klienta nikt nie uzyl.</summary>
    static readonly IReadOnlyDictionary<int, int?> Domyslne = new Dictionary<int, int?>
    {
        [0] = 0, [1] = null, [2] = 2, [3] = 0, [4] = null, [5] = 0, [6] = 0, [7] = null, [8] = 0,
    };

    /// <summary>Ile dowodow musi sie zebrac i jaka czesc ma byc zgodna, zeby uznac wynik za policzony z danych.</summary>
    const int MinDowodow = 3;
    const double ProgZgodnosci = 0.9;

    /// <summary>Powiazanie dla kazdego rodzaju urlopu (R_DNIA) wystepujacego w slowniku firmy.</summary>
    public static IReadOnlyDictionary<int, PowiazanieLimitu> Wylicz(IEnumerable<UrlopWiersz> wiersze)
    {
        var dowody = Dowody(wiersze);
        var wynik = new Dictionary<int, PowiazanieLimitu>();
        foreach (int rodzaj in Domyslne.Keys.Concat(dowody.Keys).Distinct())
        {
            if (dowody.TryGetValue(rodzaj, out var glosy) && glosy.Count > 0)
            {
                // Dowod pozytywny („obciaza te pule") ma pierwszenstwo: rodzaj zuzywajacy pule opiekuncza
                // z natury nie rusza wypoczynkowej, wiec glosy „nie obciaza" pojawiaja sie przy kazdej innej puli.
                var pozytywne = glosy.Where(g => g.Key >= 0).ToList();
                if (pozytywne.Count > 0)
                {
                    int razem = pozytywne.Sum(g => g.Value);
                    var (pula, ile) = pozytywne.MaxBy(g => g.Value);
                    if (razem >= MinDowodow && ile >= ProgZgodnosci * razem)
                    {
                        wynik[rodzaj] = new PowiazanieLimitu(NazwyPul.GetValueOrDefault(pula), true);
                        continue;
                    }
                }
                else if (glosy.GetValueOrDefault(-1) >= MinDowodow)
                {
                    wynik[rodzaj] = new PowiazanieLimitu(null, true);   // w tej firmie nie zuzywa zadnej puli
                    continue;
                }
            }
            // Za malo dowodow albo sprzeczne (np. reczne korekty kadr) — zachowanie programu, oznaczone jako domyslne.
            int? domyslna = Domyslne.GetValueOrDefault(rodzaj);
            wynik[rodzaj] = new PowiazanieLimitu(domyslna is int d ? NazwyPul.GetValueOrDefault(d) : null, false);
        }
        return wynik;
    }

    /// <summary>Rodzaje okresow, ktore wedlug danych zuzywaja limit o podanym R_DNIA (z domyslnymi w tle).</summary>
    public static int[] ZuzywajacePule(IReadOnlyDictionary<int, PowiazanieLimitu> powiazania, int limitRDnia)
    {
        string? nazwa = NazwyPul.GetValueOrDefault(limitRDnia);
        return nazwa is null
            ? Array.Empty<int>()
            : powiazania.Where(p => p.Value.Pula == nazwa).Select(p => p.Key).OrderBy(x => x).ToArray();
    }

    /// <summary>
    /// rodzaj okresu → (hipoteza → ile razy potwierdzona); hipoteza to R_DNIA obciazanego limitu albo -1 („nie obciaza").
    ///
    /// Dowodem jest tozsamosc salda na koniec roku, a nie pojedynczy wiersz. Program kadrowy zapisuje przy okresie,
    /// ktory puli NIE zuzywa (np. urlop okolicznosciowy), wartosc „ile by zostalo" — pojedynczy wiersz wyglada wtedy
    /// jak zuzycie, a nastepny pokazuje, ze pula stoi w miejscu. Dlatego dla kazdego roku i kazdej puli sprawdzamy:
    ///
    ///     stan poczatkowy − Σ IL_GODZ rodzajow z zestawu + Σ korekt = saldo ostatniego okresu z tego zestawu
    ///
    /// raz z danym rodzajem w zestawie i raz bez niego. Rozstrzyga tylko przypadek, w ktorym dokladnie jedna
    /// wersja sie zgadza.
    /// </summary>
    static Dictionary<int, Dictionary<int, int>> Dowody(IEnumerable<UrlopWiersz> wiersze)
    {
        var dowody = new Dictionary<int, Dictionary<int, int>>();
        void Zapisz(int rodzaj, int hipoteza)
        {
            var per = dowody.TryGetValue(rodzaj, out var x) ? x : dowody[rodzaj] = new();
            per[hipoteza] = per.GetValueOrDefault(hipoteza) + 1;
        }

        foreach (var grupa in wiersze.Where(w => w.Od.HasValue && w.ZatrudnienieId is not null)
                                     .GroupBy(w => (w.ZatrudnienieId!.Value, w.Od!.Value.Year)))
        {
            var okresy = grupa.Where(w => !w.L && w.PozostaloGodz is not null && w.IlGodz is not null)
                              .OrderBy(w => w.Od).ThenBy(w => w.Id).ToList();
            if (okresy.Count == 0) continue;

            foreach (int pula in NazwyPul.Keys)
            {
                // Stan poczatkowy roku wg reguly 22.19: saldo pierwszego wiersza limitu + wymiary kolejnych.
                var limity = grupa.Where(w => w.L && w.RDnia == pula).OrderBy(w => w.Od).ThenBy(w => w.Id).ToList();
                if (limity.Count == 0 || limity[0].PozostaloGodz is null) continue;
                int start = limity[0].PozostaloGodz!.Value + limity.Skip(1).Sum(w => w.LimitGodz ?? 0);

                // Saldo narastajace musi sie zgadzac na KAZDYM wierszu zestawu, nie tylko na ostatnim —
                // rodzaj, ktory puli nie rusza, psuje dopiero wiersz nastepny po nim.
                bool Zgadza(IReadOnlySet<int> zestaw)
                {
                    var wybrane = okresy.Where(o => zestaw.Contains(o.RDnia)).ToList();
                    if (wybrane.Count == 0) return false;
                    int saldo = start;
                    foreach (var o in wybrane)
                    {
                        saldo -= o.IlGodz!.Value;
                        if (o.KorPozost) saldo += o.KorPozostGodz ?? 0;
                        if (saldo != o.PozostaloGodz!.Value) return false;
                    }
                    return true;
                }

                var obecne = okresy.Select(o => o.RDnia).Distinct().ToList();
                var zestaw = new HashSet<int> { pula };

                // Dokladamy po jednym rodzaju, dopoki tozsamosc roku sie nie domknie (np. urlop na zadanie obok wypoczynkowego).
                while (!Zgadza(zestaw))
                {
                    int? dolozony = obecne.FirstOrDefault(r => !zestaw.Contains(r) && Zgadza(new HashSet<int>(zestaw) { r }), -1) is int x && x >= 0 ? x : null;
                    if (dolozony is null) break;
                    zestaw.Add(dolozony.Value);
                }
                if (!Zgadza(zestaw)) continue;   // rok sie nie domyka (reczne korekty, brakujace wiersze) — bez dowodow

                foreach (int rodzaj in zestaw) Zapisz(rodzaj, pula);
                foreach (int rodzaj in obecne.Where(r => !zestaw.Contains(r)))
                    if (!Zgadza(new HashSet<int>(zestaw) { rodzaj }))
                        Zapisz(rodzaj, -1);   // dolozenie go psuje domknieta tozsamosc — ten rodzaj puli nie rusza
            }
        }
        return dowody;
    }
}
