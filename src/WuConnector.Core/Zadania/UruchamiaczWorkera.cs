using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using WuConnector.Contracts;

namespace WuConnector.Core.Zadania;

/// <summary>Wykonanie jednego zadania w R2. W testach zastepowane atrapa.</summary>
public interface IWykonawcaZadan
{
    Task<WynikWorkera> WykonajAsync(ZadanieWorkera zadanie, CancellationToken ct);
}

/// <summary>
/// Uruchamia WuConnector.Worker.exe (x86) na jedno zadanie, z twardym limitem czasu.
/// Proces roboczy moze sie zawiesic albo pasc przez nieprzechwytywalne AccessViolation —
/// dlatego jest osobnym procesem, a jego wynik czytamy tylko z linii WU-WYNIK na stdout.
/// </summary>
public sealed class UruchamiaczWorkera : IWykonawcaZadan
{
    readonly string _sciezka;
    readonly TimeSpan _limit;

    // Jeden proces roboczy naraz na instalacje R2: praca pozycyjna, sesja zwiazana z watkiem.
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Blokady = new(StringComparer.OrdinalIgnoreCase);

    public UruchamiaczWorkera(string sciezkaWorkera, TimeSpan limit)
    {
        _sciezka = sciezkaWorkera;
        _limit = limit;
    }

    public async Task<WynikWorkera> WykonajAsync(ZadanieWorkera zadanie, CancellationToken ct)
    {
        var blokada = Blokady.GetOrAdd(zadanie.KatalogBin.TrimEnd('\\'), _ => new SemaphoreSlim(1, 1));
        await blokada.WaitAsync(ct);
        try { return await UruchomAsync(zadanie, ct); }
        finally { blokada.Release(); }
    }

    async Task<WynikWorkera> UruchomAsync(ZadanieWorkera zadanie, CancellationToken ct)
    {
        if (!File.Exists(_sciezka))
            return Porazka(Statusy.Blad, Kody.Konfiguracja, "Brak procesu roboczego: " + _sciezka);

        var psi = new ProcessStartInfo(_sciezka)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var sw = Stopwatch.StartNew();
        using var p = new Process { StartInfo = psi };
        p.Start();

        // Haslo idzie wylacznie przez stdin — nigdy w linii polecen (widoczna w menedzerze zadan).
        await using (var wej = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false)))
            await wej.WriteAsync(WorkerJson.Serializuj(zadanie));

        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_limit);
        try
        {
            await p.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();   // zatrzymanie uslugi — nie raportujemy jako timeout
            return Porazka(Statusy.BladPrzejsciowy, Kody.ConnectorTimeout,
                           $"Proces roboczy przerwany po {_limit.TotalSeconds:0} s", sw.ElapsedMilliseconds);
        }

        string wyjscie = await stdout;
        var wynik = WorkerJson.OdczytajWynik(wyjscie);
        if (wynik is null)
        {
            // Bez linii wyniku: najpewniej nieprzechwytywalna awaria w bibliotece R2.
            string err = await stderr;
            return Porazka(Statusy.BladPrzejsciowy, Kody.ConnectorAwaria,
                           $"Proces roboczy zakonczyl sie bez wyniku (kod {p.ExitCode}). {Skroc(err)}", sw.ElapsedMilliseconds);
        }
        return wynik;
    }

    static WynikWorkera Porazka(string status, string kod, string komunikat, long ms = 0)
        => new() { Status = status, Kod = kod, Komunikat = komunikat, CzasMs = ms };

    static string Skroc(string s) => s.Length > 300 ? s[..300] : s;
}
