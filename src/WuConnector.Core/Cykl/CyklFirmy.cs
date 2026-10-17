using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WuConnector.Contracts;
using WuConnector.Core.Biblioteki;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;
using WuConnector.Core.Stan;
using WuConnector.Core.Wymiana;
using WuConnector.Core.Zadania;

namespace WuConnector.Core.Cykl;

/// <summary>Zrodlo surowych danych firmy — CzytnikSql w uzyciu, atrapa w testach.</summary>
public interface IZrodloDanych
{
    Task<SuroweDane> CzytajAsync(int rokOd, int rokDo, CancellationToken ct);
}

public sealed class ZrodloSql(ZrodloFirmy zrodlo) : IZrodloDanych
{
    readonly CzytnikSql _c = new(zrodlo);
    public Task<SuroweDane> CzytajAsync(int rokOd, int rokDo, CancellationToken ct) => _c.CzytajAsync(rokOd, rokDo, ct);
}

public enum WynikFirmy { Ok, ZatrzymanaFirma, ZatrzymanyConnector, Przerwany }

public sealed record WynikCykluFirmy(
    string? Nip,
    WynikFirmy Wynik,
    TimeSpan? NastepnyInterwal,
    TimeSpan? CzekajDo,
    Dictionary<string, int> Wyslano,
    Dictionary<string, int> Usuniete,
    int ZadaniaWykonane,
    IReadOnlyList<string> Uwagi);

/// <summary>
/// Jeden przebieg dla jednej firmy wg API-TENANTA-v1:
///   odczyt R2 → start → dane (per zbior) → [przeglad] → zadania → wykonanie → wyniki → koniec.
/// Instancja zyje przez caly czas pracy uslugi (pamieta stan connectora miedzy cyklami).
/// </summary>
public sealed class CyklFirmy
{
    const int LimitZadan = 20;
    const int MaksBajtowPaczki = 1_500_000;          // serwer przyjmuje 2 MB; zapas na koperte
    static readonly TimeSpan MaksCzekaniaWCyklu = TimeSpan.FromSeconds(120);

    readonly InstalacjaR2 _instalacja;
    readonly FirmaR2 _firma;
    readonly IZrodloDanych _zrodlo;
    readonly KlientTenanta _klient;
    readonly MagazynStanu _stan;
    readonly IWykonawcaZadan _wykonawca;
    readonly byte[] _sekret;
    readonly string _wersjaConnectora;
    readonly ILogger _log;
    readonly Func<TimeSpan, CancellationToken, Task> _czekaj;
    readonly Func<DateTime> _dzis;
    readonly IWyborBiblioteki? _biblioteki;

    string? _nip;
    int? _uzytkownikR2;
    DateTimeOffset? _ostatniUdany;
    string? _problemZapisu;          // opis dla administratora — BEZ danych osobowych
    DateTime? _ostatnieZgloszenie;   // limit serwera: 10 zgloszen firmy/h na klucz — przy cyklu co 5 min zglaszamy co 30 min
    static readonly TimeSpan OdstepZgloszen = TimeSpan.FromMinutes(30);
    static readonly TimeSpan PonowienieOdrzuconej = TimeSpan.FromHours(24);

    public CyklFirmy(InstalacjaR2 instalacja, FirmaR2 firma, IZrodloDanych zrodlo, KlientTenanta klient, MagazynStanu stan,
                     IWykonawcaZadan wykonawca, byte[] sekret, string wersjaConnectora, ILogger log,
                     Func<TimeSpan, CancellationToken, Task>? czekaj = null, Func<DateTime>? dzis = null,
                     IWyborBiblioteki? biblioteki = null)
    {
        _biblioteki = biblioteki;
        _instalacja = instalacja; _firma = firma; _zrodlo = zrodlo; _klient = klient; _stan = stan;
        _wykonawca = wykonawca; _sekret = sekret; _wersjaConnectora = wersjaConnectora; _log = log;
        _czekaj = czekaj ?? Task.Delay;
        _dzis = dzis ?? (() => DateTime.Today);
        _uzytkownikR2 = instalacja.UzytkownikR2Id;
    }

    string KluczFirmy => $"{_instalacja.KatalogBin.TrimEnd('\\').ToLowerInvariant()}|{_firma.FirmIde}";

    public string Opis => $"{_instalacja.Nazwa}/firma {_firma.FirmIde}" + (_nip is null ? "" : $" (NIP {_nip})");

    public async Task<WynikCykluFirmy> WykonajAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var uwagi = new List<string>();
        var wyslano = new Dictionary<string, int>();
        var usuniete = new Dictionary<string, int>();
        var odrzucone = new List<OdrzuconyRekord>();
        int pominietePola = 0, zadaniaPobrane = 0, wynikiWyslane = 0;

        // ---- 1. odczyt R2 (SQL). Nieudany odczyt nie przerywa cyklu: zadania i wyniki moga isc dalej.
        SuroweDane? dane = null;
        WynikBudowy? budowa = null;
        string? bladOdczytu = null;
        try
        {
            var dzis = _dzis();
            dane = await _zrodlo.CzytajAsync(dzis.Year - 1, dzis.Year + 1, ct);
            budowa = BudowniczyRekordow.Zbuduj(dane, new UstawieniaBudowy(_uzytkownikR2, _sekret, dzis));
            _nip = Nip.Normalizuj(dane.Firma.Nip);   // do serwera zawsze 10 cyfr: bez myslnikow, spacji i prefiksu PL
            _stan.ZapamietajFirme(KluczFirmy, dane.Firma.Nip, dane.Firma.Nazwa);
            uwagi.AddRange(budowa.Uwagi);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bladOdczytu = ex is Microsoft.Data.SqlClient.SqlException { Number: 18456 or 4060 }
                ? "Brak dostępu do bazy programu kadrowego — sprawdź konto SQL connectora"
                : "Odczyt bazy programu kadrowego nieudany";
            _log.LogError(ex, "{Firma}: odczyt SQL nieudany", Opis);
        }

        // Po restarcie bez dostepu do bazy: NIP z poprzednich cykli — serwer i tak dostanie stan "odczytWstrzymany".
        _nip ??= _stan.NipFirmy(KluczFirmy);
        if (_nip is null)
            return Koniec(WynikFirmy.Przerwany, null, null, uwagi.Append(bladOdczytu ?? "nieznany NIP").ToList());

        string nip = _nip;

        // NIP z R2 z bledna suma kontrolna — serwer odrzuci go 422 NIP_NIEPOPRAWNY; lepiej powiedziec to tutaj.
        if (!Nip.CzyPoprawny(nip))
        {
            _log.LogError("{Firma}: NIP {Nip} w programie kadrowym ma błędną sumę kontrolną — popraw go w kartotece firmy", Opis, nip);
            return Koniec(WynikFirmy.ZatrzymanaFirma, null, nip, uwagi.Append("NIP niepoprawny").ToList());
        }

        // ---- 2. start (drugi raz w tym samym cyklu, gdy zgloszenie firmy wrocilo jako "zatwierdzona")
        OdpowiedzStart start;
        for (int proba = 1; ; proba++)
        try
        {
            string stan = bladOdczytu is not null ? StanyConnectora.OdczytWstrzymany
                        : _problemZapisu is not null ? StanyConnectora.ZapisWstrzymany
                        : StanyConnectora.Ok;
            var sc = new StanConnectora(nip, _wersjaConnectora, dane?.WersjaBazy ?? _instalacja.WersjaR2, _ostatniUdany, stan,
                                        bladOdczytu ?? _problemZapisu, _stan.Kursory(nip),
                                        Konfiguracja.Odcisk.Sekretu(_sekret));
            start = await Ponow(() => _klient.StartAsync(sc, Klucz(), ct), ct);
            if (_stan.CzyOdrzucona(KluczFirmy)) _stan.UstawOdrzucenie(KluczFirmy, nip, false);   // firma jednak zatwierdzona
            break;
        }
        catch (BladProtokolu e)
        {
            var (wynikBledu, ponow) = await ObsluzBladStartu(e, dane, uwagi, ct);
            if (!ponow || proba > 1) return wynikBledu;
        }

        string przebieg = start.PrzebiegId;
        int rozmiar = Math.Clamp(start.RozmiarPaczki, 1, 500);

        try
        {
            // ---- 3. dane (tylko gdy odczyt sie udal)
            if (budowa is not null)
            {
                var zakres = new ZakresPorownania(budowa.GrupyWZakresie, budowa.PoczatekOkna);
                foreach (var zb in Zbiory.Wszystkie)
                {
                    bool pelny = start.Zbiory.TryGetValue(zb, out var z) && z.Tryb == "pelny";
                    var r = _stan.Porownaj(nip, zb, budowa.Zbiory[zb], pelny, zakres);
                    // Podmiana calej bazy (odtworzenie danych zamiast kopii, wskazanie na inna firme): wszystkie id inne.
                    // Wyslanie tego zdublowaloby dane u tenanta — zatrzymujemy firme PRZED wysylka, do decyzji czlowieka.
                    if (MagazynStanu.ZbioryZBezpiecznikiem.Contains(zb) && r.StanPotwierdzony >= 20 && r.Wspolne == 0 && r.DoWyslania.Count > 0)
                    {
                        string opisPodmiany = $"{zb}: żaden z {r.StanPotwierdzony} znanych identyfikatorów nie występuje w programie kadrowym "
                                            + $"(odczytano {r.DoWyslania.Count} nowych) — wygląda na podmienioną bazę albo przeniesienie danych. "
                                            + "Cykl wstrzymany: wysyłka zdublowałaby dane. Sprawdź konfigurację instalacji; "
                                            + "jeśli to świadome przeniesienie — wyczyść stan connectora i zrób pełne ładowanie z przeglądem kontrolnym.";
                        _log.LogError("{Firma}: {Opis}", Opis, opisPodmiany);
                        uwagi.Add(opisPodmiany);
                        return Koniec(WynikFirmy.ZatrzymanaFirma, null, nip, uwagi);
                    }
                    if (r.UsunieciaWstrzymane)
                    {
                        uwagi.Add($"{zb}: usuniecia wstrzymane bezpiecznikiem ({r.UsunieciaWykryte}, niewyjasnionych {r.UsunieciaNiewyjasnione})");
                        _log.LogWarning("{Firma}: {Zbior} — {N} usuniec wstrzymanych bezpiecznikiem", Opis, zb, r.UsunieciaWykryte);
                    }

                    var pozycje = r.DoWyslania.Select(x => new Pozycja(x, null))
                                  .Concat(r.Usuniete.Select(id => new Pozycja(null, id))).ToList();
                    if (pozycje.Count == 0 && !pelny) continue;

                    var (w, u, odrz, pom) = await WyslijZbior(nip, przebieg, zb, pozycje, rozmiar, ct);
                    if (w > 0) wyslano[zb] = w;
                    if (u > 0) usuniete[zb] = u;
                    odrzucone.AddRange(odrz);
                    pominietePola += pom;
                }

                // ---- 4. przeglad kontrolny — na zadanie serwera
                if (start.PrzegladKontrolny)
                    foreach (var zb in Zbiory.Wszystkie)
                        await WyslijPrzeglad(nip, przebieg, zb, budowa.Zbiory[zb], rozmiar, ct);
            }

            // ---- 5. wycofane: niewykonane zamykamy wynikiem "wycofano"
            foreach (var id in start.Wycofane)
                if (_stan.Wynik(nip, id) is null)
                    ZapiszWynik(nip, new WynikZadania(id, StatusyWyniku.Wycofano, null, null, new()));

            // ---- 6. zadania
            var zadania = await Ponow(() => _klient.ZadaniaAsync(nip, przebieg, LimitZadan, ct), ct);
            zadaniaPobrane = zadania.Count;
            foreach (var zad in zadania)
            {
                if (start.Wycofane.Contains(zad.RequestId)) continue;
                var wynik = await WykonajZadanie(zad, dane, budowa, ct);
                ZapiszWynik(nip, wynik);
            }

            // ---- 7. wyniki: wszystkie niepotwierdzone, takze z poprzednich cykli
            wynikiWyslane = await WyslijWyniki(nip, przebieg, ct);

            // ---- 8. koniec
            var kon = await Ponow(() => _klient.KoniecAsync(new PodsumowanieKonca(nip, przebieg, wyslano, usuniete, odrzucone,
                                                              zadaniaPobrane, wynikiWyslane, pominietePola, sw.ElapsedMilliseconds), Klucz(), ct), ct);
            if (kon.PelnyPrzegladWymagany)
            {
                uwagi.Add("serwer zazadal pelnego przegladu w nastepnym cyklu");
                foreach (var zb in Zbiory.Wszystkie) _stan.UstawKursor(nip, zb, null);   // brak kursora → serwer da tryb "pelny"
            }

            if (bladOdczytu is null) _ostatniUdany = DateTimeOffset.Now;
            _log.LogInformation("{Firma}: cykl {Ms} ms, wyslano {W}, usunieto {U}, zadan {Z}, wynikow {R}",
                Opis, sw.ElapsedMilliseconds, wyslano.Values.Sum(), usuniete.Values.Sum(), zadaniaPobrane, wynikiWyslane);
            return new WynikCykluFirmy(nip, WynikFirmy.Ok, TimeSpan.FromSeconds(Math.Max(1, kon.NastepnyInterwal)), null,
                                       wyslano, usuniete, zadaniaPobrane, uwagi);
        }
        catch (BladProtokolu e)
        {
            _log.LogWarning("{Firma}: cykl przerwany — HTTP {Status} {Kod} (zdarzenie {Zd})", Opis, e.Status, e.Kod, e.ZdarzenieId);
            uwagi.Add($"cykl przerwany: HTTP {e.Status} {e.Kod}");
            var wynik = e.ZatrzymujeConnector ? WynikFirmy.ZatrzymanyConnector
                      : e.ZatrzymujeFirme ? WynikFirmy.ZatrzymanaFirma
                      : WynikFirmy.Przerwany;
            return Koniec(wynik, e.RetryAfter, nip, uwagi, wyslano, usuniete, zadaniaPobrane);
        }
    }

    // ================================================================== dane

    sealed record Pozycja(Rekord? Rekord, string? UsunieteId)
    {
        public string Id => Rekord?.Id ?? UsunieteId!;
        public JsonObject Json() => Rekord is not null ? Rekord.Tresc : new JsonObject { ["id"] = UsunieteId, ["usuniety"] = true };
        public int Bajty => Rekord is not null ? Rekord.Json().Length : UsunieteId!.Length + 30;
    }

    async Task<(int Wyslane, int Usuniete, List<OdrzuconyRekord> Odrzucone, int Pominiete)> WyslijZbior(
        string nip, string przebieg, string zbior, List<Pozycja> pozycje, int rozmiar, CancellationToken ct)
    {
        // Podzial na paczki: limit rekordow i bajtow.
        var paczki = new List<List<Pozycja>>();
        var biezaca = new List<Pozycja>();
        long bajty = 0;
        foreach (var p in pozycje)
        {
            if (biezaca.Count > 0 && (biezaca.Count >= rozmiar || bajty + p.Bajty > MaksBajtowPaczki))
            { paczki.Add(biezaca); biezaca = new(); bajty = 0; }
            biezaca.Add(p);
            bajty += p.Bajty;
        }
        paczki.Add(biezaca);   // w trybie pelnym takze pusta — domyka zbior i daje kursor

        int nr = 0, wyslane = 0, usun = 0, pomin = 0;
        var odrzucone = new List<OdrzuconyRekord>();

        async Task Wyslij(List<Pozycja> paczka, bool ostatnia)
        {
            while (true)
            {
                nr++;
                try
                {
                    string klucz = Klucz();   // staly dla ponowien TEJ paczki — serwer nie zapisze jej dwa razy
                    var o = await Ponow(() => _klient.DaneAsync(nip, przebieg, zbior, nr, ostatnia,
                                                                paczka.Select(x => x.Json()).ToList(), klucz, ct), ct);
                    _stan.Potwierdz(nip, zbior, paczka.Where(x => x.Rekord is not null).Select(x => x.Rekord!),
                                    paczka.Where(x => x.UsunieteId is not null).Select(x => x.UsunieteId!));
                    _stan.UstawKursor(nip, zbior, o.Kursor);
                    wyslane += paczka.Count(x => x.Rekord is not null);
                    usun += paczka.Count(x => x.UsunieteId is not null);
                    pomin += o.PominietePola;
                    return;
                }
                catch (BladProtokolu e) when (e.Status == 413 && paczka.Count > 1)
                {
                    // Za duza — dzielimy na pol i wysylamy obie czesci osobno.
                    int pol = paczka.Count / 2;
                    await Wyslij(paczka.Take(pol).ToList(), false);
                    paczka = paczka.Skip(pol).ToList();
                }
                catch (BladProtokolu e) when (e.Status == 422 && e.Pozycje.Count > 0)
                {
                    // Cala paczka odrzucona: usuwamy wskazane rekordy, reszte wysylamy z nowym kluczem.
                    var zle = e.Pozycje.Select(p => p.Id).ToHashSet();
                    foreach (var id in zle) odrzucone.Add(new OdrzuconyRekord(zbior, id, e.ZdarzenieId));
                    _log.LogWarning("{Firma}: {Zbior} — {N} rekordow odrzuconych przez serwer (422, zdarzenie {Zd})", Opis, zbior, zle.Count, e.ZdarzenieId);
                    paczka = paczka.Where(x => !zle.Contains(x.Id)).ToList();
                    if (paczka.Count == 0 && !ostatnia) return;
                }
            }
        }

        for (int i = 0; i < paczki.Count; i++) await Wyslij(paczki[i], i == paczki.Count - 1);
        return (wyslane, usun, odrzucone, pomin);
    }

    async Task WyslijPrzeglad(string nip, string przebieg, string zbior, IReadOnlyList<Rekord> rekordy, int rozmiar, CancellationToken ct)
    {
        var paczki = rekordy.Chunk(rozmiar).ToList();
        if (paczki.Count == 0) paczki.Add(Array.Empty<Rekord>());
        for (int i = 0; i < paczki.Count; i++)
        {
            string klucz = Klucz();
            // "ostatnia" domyka usuniecia po stronie serwera — tylko na ostatniej paczce.
            var o = await Ponow(() => _klient.PrzegladAsync(nip, przebieg, zbior, i + 1, i == paczki.Count - 1, paczki[i], klucz, ct), ct);
            _stan.UstawKursor(nip, zbior, o.Kursor);
        }
    }

    // ================================================================== zadania

    async Task<WynikZadania> WykonajZadanie(ZadanieTenanta z, SuroweDane? dane, WynikBudowy? budowa, CancellationToken ct)
    {
        // Zadanie juz wykonane (wynik niepotwierdzony albo zadanie wydane ponownie) — nie zapisujemy drugi raz.
        if (_stan.Wynik(_nip!, z.RequestId) is { } zap && zap.Status != StatusyWyniku.BladPrzejsciowy)
        {
            _log.LogInformation("{Firma}: zadanie {Id} juz wykonane — odsylam zapisany wynik", Opis, z.RequestId);
            return JsonSerializer.Deserialize<WynikZadania>(zap.Wynik, KlientTenanta.Json)!;
        }

        WynikZadania Odrzuc(string kod, string opis) =>
            new(z.RequestId, StatusyWyniku.Blad, kod, opis,
                (z.Zapisy ?? new()).Select(x => new ZapisWyniku(x.Id, null, null, null, null)).ToList());

        if (z.Operacja is not ("zapis" or "usuniecie"))
            return Odrzuc(Kody.OperacjaNieobslugiwana, $"Operacja '{z.Operacja}' nie jest wykonywana przez connector");
        if (dane is null)
            return new WynikZadania(z.RequestId, StatusyWyniku.BladPrzejsciowy, Kody.Niedostepne, "Odczyt bazy kadrowej nieudany", new());

        if (!int.TryParse(z.ZatrudnienieId, NumberStyles.None, CultureInfo.InvariantCulture, out int zatrId)
            || dane.Zatrudnienia.FirstOrDefault(x => x.Id == zatrId && x.Aktualne) is not { } zatr
            || budowa?.GrupyWZakresie.Contains(zatr.PracownikId.ToString(CultureInfo.InvariantCulture)) != true)
            return Odrzuc(Kody.ZatrudnienieNieznalezione, $"Zatrudnienie {z.ZatrudnienieId} nie jest aktualnym zatrudnieniem na umowe o prace");

        // Biblioteka R2 dobrana do wersji bazy: konfiguracja → BIN → magazyn → pobranie (ARCHITEKTURA §7).
        string? katalogDll = _instalacja.KatalogDll;
        if (_biblioteki is not null)
        {
            var b = await _biblioteki.WybierzAsync(_instalacja.KatalogBin, _instalacja.KatalogDll, dane.WersjaBazy, ct);
            if (b.Uwaga is not null) _log.LogWarning("{Firma}: biblioteka R2 — {Uwaga}", Opis, b.Uwaga);
            if (b.Katalog is null)
            {
                _problemZapisu = $"Brak biblioteki API programu kadrowego dla wersji {dane.WersjaBazy}";
                return new WynikZadania(z.RequestId, StatusyWyniku.BladPrzejsciowy, Kody.Niedostepne, _problemZapisu, new());
            }
            katalogDll = b.Katalog;
        }

        var zw = new ZadanieWorkera
        {
            KatalogBin = _instalacja.KatalogBin,
            KatalogDll = katalogDll,
            Login = _instalacja.LoginR2,
            Haslo = _instalacja.HasloR2,
            FirmIde = _firma.FirmIde,
            PracownikId = zatr.PracownikId,
            ZatrudnienieId = zatr.Id,
            RequestId = z.RequestId,
        };

        if (z.Operacja == "zapis")
        {
            if (z.Rodzaj?.Zrodlo != "urlop") return Odrzuc(Kody.ZadanieNiepoprawne, "Connector zapisuje tylko rodzaje ze zrodla 'urlop'");
            zw.Operacja = Operacje.Zapis;
            zw.Kod = z.Rodzaj.Kod;
            zw.DataOd = z.DataOd;
            zw.DataDo = z.DataDo;
        }
        else
        {
            var ids = new List<int>();
            foreach (var x in z.Zapisy ?? new())
            {
                if (!x.Id.StartsWith("urlop:") || !int.TryParse(x.Id.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                    return Odrzuc(Kody.ZadanieNiepoprawne, $"Connector usuwa tylko wpisy karty urlopowej, nie '{x.Id}'");
                ids.Add(n);
            }
            zw.Operacja = Operacje.Usuniecie;
            zw.Zapisy = ids;
        }

        _log.LogInformation("{Firma}: zadanie {Id} ({Op}) → proces roboczy", Opis, z.RequestId, z.Operacja);
        var w = await _wykonawca.WykonajAsync(zw, ct);
        if (w.UzytkownikId > 0) _uzytkownikR2 = w.UzytkownikId;

        // Problem z dostepem do R2 — pokazujemy administratorowi w stanie connectora (bez tresci komunikatu R2).
        _problemZapisu = w.Kod switch
        {
            Kody.Konfiguracja => "Logowanie do programu kadrowego nieudane — sprawdz konto i konfiguracje",
            Kody.Niedostepne => "Program kadrowy chwilowo niedostepny (np. konto connectora zajete)",
            Kody.ConnectorTimeout => "Zapis w programie kadrowym przekroczyl limit czasu",
            Kody.ConnectorAwaria => "Awaria procesu zapisu connectora",
            _ => null,
        };

        var zapisy = w.Zapisy.Select(x => new ZapisWyniku("urlop:" + x.Id.ToString(CultureInfo.InvariantCulture), x.DataOd, x.DataDo, x.Dni, x.Minuty)).ToList();

        // Ustalenie nadzoru wezurlop (20.09.2026): gdy zapis nie przeszedl, bo termin jest zajety, odsylamy wpisy,
        // ktore go blokuja — u tenanta nie sa realizacja wniosku, ida do tresci konfliktu dla kadr.
        if (zapisy.Count == 0 && w.Kod == Kody.Kolizja && z.Operacja == "zapis")
            zapisy = WpisyBlokujace(dane, zatr.Id, z.DataOd, z.DataDo);

        return new WynikZadania(z.RequestId, w.Status, w.Kod, w.Komunikat, zapisy);
    }


    /// <summary>
    /// Wpisy karty urlopowej tego zatrudnienia nachodzace na zadany termin — z danych odczytanych w tym cyklu
    /// (wpis blokujacy istnial przed proba zapisu, wiec jest w migawce). Wiersze limitu (L) pomijamy.
    /// </summary>
    static List<ZapisWyniku> WpisyBlokujace(SuroweDane dane, int zatrudnienieId, string? od, string? doo)
    {
        const string F = "yyyy-MM-dd";
        if (!DateTime.TryParseExact(od, F, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
            || !DateTime.TryParseExact(doo, F, CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
            return new();

        return dane.Urlopy
            .Where(u => !u.L && u.ZatrudnienieId == zatrudnienieId && u.Od is not null && u.Do is not null
                        && u.Od <= b && u.Do >= a)
            .OrderBy(u => u.Od)
            .Select(u => new ZapisWyniku("urlop:" + u.Id.ToString(CultureInfo.InvariantCulture),
                                         u.Od!.Value.ToString(F, CultureInfo.InvariantCulture),
                                         u.Do!.Value.ToString(F, CultureInfo.InvariantCulture), u.IlDni, u.IlGodz))
            .ToList();
    }

    void ZapiszWynik(string nip, WynikZadania w)
        => _stan.ZapiszWynik(nip, w.RequestId, w.Status, JsonSerializer.Serialize(w, KlientTenanta.Json));

    async Task<int> WyslijWyniki(string nip, string przebieg, CancellationToken ct)
    {
        var wyniki = _stan.NiewyslaneWyniki(nip).Select(j => JsonSerializer.Deserialize<WynikZadania>(j, KlientTenanta.Json)!).ToList();
        foreach (var paczka in wyniki.Chunk(100))
        {
            string klucz = Klucz();
            await Ponow(() => _klient.WynikiAsync(nip, przebieg, paczka, klucz, ct), ct);
            _stan.OznaczWyslane(nip, paczka.Select(w => w.RequestId));
        }
        return wyniki.Count;
    }

    // ================================================================== bledy i ponawianie

    /// <summary>Wartosci pola `powod` tenanta po ludzku; nieznane pokazujemy tak, jak przyszly.</summary>
    static string OpisPowodu(string powod) => powod switch
    {
        "uniewazniony_przez_administratora" => "unieważniony przez administratora wezurlop",
        "zastapiony_nowym_kluczem" => "zastąpiony nowym kluczem — wpisz nowy",
        "wygasl" => "przebieg wygasł po przerwie",
        "zamkniety" => "przebieg już zamknięty",
        "zastapiony" => "przebieg zastąpiony nowym",
        "nieznany" => "przebieg nieznany serwerowi",
        _ => powod,
    };

    /// <summary>Zwraca wynik firmy i czy ponowic start w tym samym cyklu (zgloszenie → "zatwierdzona").</summary>
    async Task<(WynikCykluFirmy, bool Ponow)> ObsluzBladStartu(BladProtokolu e, SuroweDane? dane, List<string> uwagi, CancellationToken ct)
    {
        uwagi.Add($"start: HTTP {e.Status} {e.Kod}");
        if (e.Status == 403 && e.Kod == "FIRMA_NIEZATWIERDZONA")
        {
            // Odrzucona: nie zglaszamy co cykl, ale raz na dobe sprawdzamy, czy administrator nie cofnal decyzji.
            if (_stan.OdrzuconaOd(KluczFirmy) is { } odrzucona && DateTimeOffset.Now - odrzucona < PonowienieOdrzuconej)
            {
                _log.LogWarning("{Firma}: administrator wezurlop odrzucił tę firmę — connector jej nie synchronizuje; ponowne sprawdzenie po {Czas:dd.MM HH:mm}",
                                Opis, (odrzucona + PonowienieOdrzuconej).LocalDateTime);
                return (Koniec(WynikFirmy.ZatrzymanaFirma, e.RetryAfter, _nip, uwagi), false);
            }
            if (dane is not null && (_ostatnieZgloszenie is null || DateTime.Now - _ostatnieZgloszenie > OdstepZgloszen))
            {
                try
                {
                    _ostatnieZgloszenie = DateTime.Now;
                    var odp = await _klient.ZgloszenieFirmyAsync(_nip!, dane.Firma.Nazwa, Klucz(), ct);
                    string status = (string?)odp["status"] ?? "";
                    if (status is "zatwierdzona" or "oczekuje" && _stan.CzyOdrzucona(KluczFirmy))
                    {
                        _stan.UstawOdrzucenie(KluczFirmy, _nip!, false);   // administrator cofnal odrzucenie
                        _log.LogInformation("{Firma}: administrator wezurlop cofnął odrzucenie firmy (stan: {S})", Opis, status);
                    }
                    switch (status)
                    {
                        case "zatwierdzona":
                            _log.LogInformation("{Firma}: firma zatwierdzona w wezurlop — start w tym samym cyklu", Opis);
                            return (Koniec(WynikFirmy.ZatrzymanaFirma, null, _nip, uwagi), true);
                        case "odrzucona":
                            _stan.UstawOdrzucenie(KluczFirmy, _nip!, true);
                            uwagi.Add("firma odrzucona przez administratora wezurlop");
                            _log.LogError("{Firma}: administrator wezurlop ODRZUCIŁ zgłoszenie firmy — connector przestaje ją zgłaszać", Opis);
                            break;
                        default:
                            uwagi.Add("firma zgłoszona — czeka na zatwierdzenie przez administratora wezurlop");
                            _log.LogWarning("{Firma}: firma niezatwierdzona po stronie wezurlop — zgłoszona do zatwierdzenia", Opis);
                            break;
                    }
                }
                catch (BladProtokolu z) { _log.LogWarning("{Firma}: zgłoszenie firmy nieudane — HTTP {S} {K}", Opis, z.Status, z.Kod); }
            }
        }
        else if (e.Status == 422 && e.Kod == "NIP_NIEPOPRAWNY")
            _log.LogError("{Firma}: serwer uznał NIP {Nip} za niepoprawny", Opis, _nip);
        // Dwa rozne NIP-y w jednym zadaniu: naglowek X-Nip i parametr ?nip=. Connector parametru nie wysyla,
        // wiec doklada go cos po drodze (proxy, brama) albo w sieci stoi starsza wersja connectora.
        // Blad konfiguracji, nie przejsciowy — ponawianie nic nie da.
        else if (e.Status == 400 && e.Kod == "NIP_NIESPOJNY")
        {
            _log.LogError("{Firma}: serwer dostał dwa różne NIP-y w jednym żądaniu (nagłówek i adres) — "
                        + "connector wysyła tylko nagłówek, więc parametr dokłada pośrednik po drodze albo działa starsza wersja connectora",
                          Opis);
            return (Koniec(WynikFirmy.ZatrzymanaFirma, null, _nip, uwagi.Append("NIP niespójny — nagłówek i adres").ToList()), false);
        }
        else if (e.Status == 409 && e.Kod == "WERSJA_PROTOKOLU")
        {
            _log.LogError("{Firma}: serwer nie obsluguje protokolu v{V} — connector wymaga aktualizacji", Opis, KlientTenanta.WersjaProtokolu);
            return (Koniec(WynikFirmy.ZatrzymanyConnector, null, _nip, uwagi), false);
        }
        else if (e.Status == 401)
        {
            // Tenant dokłada `powod`: uniewazniony przez administratora albo zastapiony nowym kluczem.
            _log.LogError("{Firma}: klucz odrzucony przez serwer ({Kod}{Powod}) — wpisz aktualny klucz w oknie connectora",
                          Opis, e.Kod, e.Powod is null ? "" : ", " + OpisPowodu(e.Powod));
            uwagi.Add("klucz odrzucony: " + (e.Powod is null ? e.Kod ?? "401" : OpisPowodu(e.Powod)));
        }
        else _log.LogWarning("{Firma}: start odrzucony — HTTP {S} {K}{P} (zdarzenie {Z})", Opis, e.Status, e.Kod,
                             e.Powod is null ? "" : "/" + e.Powod, e.ZdarzenieId);

        var wynik = e.ZatrzymujeConnector ? WynikFirmy.ZatrzymanyConnector
                  : e.ZatrzymujeFirme || e.Status == 422 ? WynikFirmy.ZatrzymanaFirma
                  : WynikFirmy.Przerwany;
        return (Koniec(wynik, e.RetryAfter, _nip, uwagi), false);
    }
    /// <summary>
    /// Bledy przejsciowe (408, 429, 5xx, brak polaczenia) ponawiamy w cyklu do 3 razy,
    /// honorujac Retry-After. Dluzsze czekanie niz 2 min — przerywamy cykl, usluga odczeka.
    /// </summary>
    async Task<T> Ponow<T>(Func<Task<T>> operacja, CancellationToken ct)
    {
        for (int proba = 1; ; proba++)
        {
            try { return await operacja(); }
            catch (BladProtokolu e) when (e.Przejsciowy && proba < 3)
            {
                var czekaj = e.RetryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, proba));
                if (czekaj > MaksCzekaniaWCyklu) throw;
                _log.LogInformation("{Firma}: HTTP {S} — ponowienie {P} za {C:0} s", Opis, e.Status, proba, czekaj.TotalSeconds);
                await _czekaj(czekaj, ct);
            }
        }
    }

    static string Klucz() => Guid.NewGuid().ToString();

    static WynikCykluFirmy Koniec(WynikFirmy w, TimeSpan? czekaj, string? nip, List<string> uwagi,
                                  Dictionary<string, int>? wys = null, Dictionary<string, int>? usun = null, int zad = 0)
        => new(nip, w, null, czekaj, wys ?? new(), usun ?? new(), zad, uwagi);
}
