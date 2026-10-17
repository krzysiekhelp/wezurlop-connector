using WuConnector.Core.Cykl;
using WuConnector.Core.Konfiguracja;

namespace WuConnector.Service;

/// <summary>Petla uslugi: cykl → odstep z harmonogramu (serwer moze go wydluzyc) albo reczne zlecenie z okna → cykl …</summary>
public sealed class SluzbaConnectora(KonfiguracjaConnectora konfiguracja, ILogger<SluzbaConnectora> log, IHostApplicationLifetime zycie,
                                     SygnalSynchronizacji sygnal)
    : BackgroundService
{
    public static readonly string Wersja = typeof(SluzbaConnectora).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Tryb --raz: jeden cykl i koniec (testy, diagnostyka).</summary>
    public static bool JedenCykl { get; set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Connector {Wersja} start: {Firmy} firm(y), serwer {Adres}, harmonogram: {Harmonogram}", Wersja,
            konfiguracja.Instalacje.Sum(i => i.Firmy.Count(f => f.Aktywna)), konfiguracja.Serwer.Adres.Host, konfiguracja.Harmonogram.Opis());

        using var connector = new Connector(konfiguracja, Wersja, log);
        while (!ct.IsCancellationRequested)
        {
            TimeSpan odstep;
            try
            {
                var w = await connector.WykonajCyklAsync(ct);
                odstep = w.Nastepny;
                foreach (var f in w.Firmy.Where(f => f.Uwagi.Count > 0))
                    foreach (var u in f.Uwagi) log.LogInformation("{Nip}: {Uwaga}", Nip.DoLogu(f.Nip), u);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Cykl nieudany");
                odstep = Connector.DomyslnyInterwal;
            }

            if (JedenCykl) { zycie.StopApplication(); break; }
            log.LogInformation("Następna synchronizacja o {Czas:HH:mm}", DateTime.Now + odstep);
            try { await sygnal.CzekajAsync(odstep, ct); } catch (OperationCanceledException) { break; }
        }
        log.LogInformation("Connector zatrzymany");
    }
}
