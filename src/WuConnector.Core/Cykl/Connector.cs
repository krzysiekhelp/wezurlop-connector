using Microsoft.Extensions.Logging;
using WuConnector.Core.Biblioteki;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Odczyt;
using WuConnector.Core.Stan;
using WuConnector.Core.Wymiana;
using WuConnector.Core.Zadania;

namespace WuConnector.Core.Cykl;

public sealed record WynikCyklu(TimeSpan Nastepny, bool ConnectorZatrzymany, IReadOnlyList<WynikCykluFirmy> Firmy);

/// <summary>
/// Caly connector: jeden wspolny cykl dla wszystkich firm, obslugiwanych kolejno (API-TENANTA 5b).
/// Interwal = najkrotszy z otrzymanych, nie krotszy niz 30 s — limit dotyczy calego connectora.
/// </summary>
public sealed class Connector : IDisposable
{
    public static readonly TimeSpan DomyslnyInterwal = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan InterwalPoZatrzymaniu = TimeSpan.FromMinutes(15);

    readonly KonfiguracjaConnectora _k;
    readonly KlientTenanta _klient;
    readonly MagazynStanu _stan;
    readonly List<CyklFirmy> _firmy = new();
    readonly ILogger _log;
    readonly Func<DateTime> _teraz;

    public Connector(KonfiguracjaConnectora k, string wersja, ILogger log,
                     IWykonawcaZadan? wykonawca = null,
                     Func<InstalacjaR2, FirmaR2, IZrodloDanych>? zrodla = null,
                     HttpMessageHandler? http = null,
                     Func<TimeSpan, CancellationToken, Task>? czekaj = null,
                     Func<DateTime>? dzis = null,
                     IWyborBiblioteki? biblioteki = null,
                     Func<DateTime>? teraz = null)
    {
        _teraz = teraz ?? (() => DateTime.Now);
        _k = k;
        _log = log;
        _klient = new KlientTenanta(k.Serwer.Adres, k.Serwer.Klucz, wersja, http);
        _stan = new MagazynStanu(k.SciezkaStanu);
        wykonawca ??= new UruchamiaczWorkera(k.SciezkaWorkera, k.LimitZadania);
        zrodla ??= (i, f) => new ZrodloSql(new ZrodloFirmy(i.SerwerSql, i.BazaBin, f.BazaDane, f.FirmIde, i.LoginSql, i.HasloSql));

        if (biblioteki is null && k.KatalogBibliotek is not null && OperatingSystem.IsWindows())
            biblioteki = new MagazynBibliotek(k.KatalogBibliotek, k.AdresManifestuBibliotek, log);

        // Ta sama baza firmy w dwoch instalacjach (np. stara i nowa wersja programu na jednej bazie) = ta sama firma
        // wyslana dwa razy jako dwa zrodla. Synchronizujemy ja raz — z pierwszej instalacji — a druga zglaszamy.
        var bazy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in k.Instalacje)
            foreach (var f in inst.Firmy.Where(f => f.Aktywna))
            {
                if (!bazy.TryAdd($"{inst.SerwerSql}|{f.BazaDane}", inst.Nazwa))
                {
                    log.LogError("{Instalacja}: baza {Baza} jest już synchronizowana przez instalację {Pierwsza} — pomijam (odznacz tę firmę w jednej z instalacji)",
                                 inst.Nazwa, f.BazaDane, bazy[$"{inst.SerwerSql}|{f.BazaDane}"]);
                    continue;
                }
                _firmy.Add(new CyklFirmy(inst, f, zrodla(inst, f), _klient, _stan, wykonawca, k.SekretOsoby, wersja, log, czekaj, dzis, biblioteki));
            }
    }

    public MagazynStanu Stan => _stan;

    public async Task<WynikCyklu> WykonajCyklAsync(CancellationToken ct)
    {
        var wyniki = new List<WynikCykluFirmy>();
        foreach (var f in _firmy)
        {
            ct.ThrowIfCancellationRequested();
            WynikCykluFirmy w;
            try { w = await f.WykonajAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Awaria jednej firmy nie zatrzymuje pozostalych.
                _log.LogError(ex, "{Firma}: nieobsluzony blad cyklu", f.Opis);
                w = new WynikCykluFirmy(null, WynikFirmy.Przerwany, null, null, new(), new(), 0, new[] { ex.GetType().Name });
            }
            wyniki.Add(w);

            if (w.Wynik == WynikFirmy.ZatrzymanyConnector)
            {
                _log.LogError("Connector zatrzymany (klucz nieprawidlowy albo niezgodna wersja protokolu) — ponowna proba za {M} min",
                              InterwalPoZatrzymaniu.TotalMinutes);
                return new WynikCyklu(InterwalPoZatrzymaniu, true, wyniki);
            }
        }

        // Harmonogram wyznacza odstep; serwer moze go tylko wydluzyc (nastepnyInterwal, Retry-After).
        var odstep = _k.Harmonogram.Odstep(_teraz());
        var serwer = wyniki.Select(w => w.NastepnyInterwal).Where(t => t.HasValue).Select(t => t!.Value).DefaultIfEmpty(TimeSpan.Zero).Min();
        var retry = wyniki.Select(w => w.CzekajDo).Where(t => t.HasValue).Select(t => t!.Value).DefaultIfEmpty(TimeSpan.Zero).Max();
        if (serwer > odstep) odstep = serwer;
        if (retry > odstep) odstep = retry;
        if (odstep < _k.MinimalnyInterwal) odstep = _k.MinimalnyInterwal;
        return new WynikCyklu(odstep, false, wyniki);
    }

    public void Dispose() { _klient.Dispose(); _stan.Dispose(); }
}
