using System.Collections.Concurrent;
using System.Text;

namespace WuConnector.Service;

/// <summary>
/// Dziennik w plikach dziennych (dziennik-RRRR-MM-DD.log) obok konfiguracji — czyta go okno w zasobniku.
/// 30 dni wstecz, starsze usuwane. Do dziennika nie trafiaja sekrety ani tresc rekordow.
/// </summary>
public sealed class DziennikPlikowy : ILoggerProvider
{
    readonly string _katalog;
    readonly object _zamek = new();
    readonly ConcurrentDictionary<string, Rejestrator> _rej = new();

    public DziennikPlikowy(string katalog)
    {
        _katalog = katalog;
        Directory.CreateDirectory(katalog);
        foreach (var f in Directory.GetFiles(katalog, "dziennik-*.log"))
            if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30)) try { File.Delete(f); } catch (IOException) { }
    }

    public ILogger CreateLogger(string kategoria) => _rej.GetOrAdd(kategoria, k => new Rejestrator(this, k));

    void Zapisz(LogLevel poziom, string kategoria, string tresc, Exception? ex)
    {
        string linia = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {Skrot(poziom)} {tresc}"
                     + (ex is null ? "" : $"{Environment.NewLine}    {ex.GetType().Name}: {ex.Message}");
        lock (_zamek)
            File.AppendAllText(Path.Combine(_katalog, $"dziennik-{DateTime.Now:yyyy-MM-dd}.log"), linia + Environment.NewLine, Encoding.UTF8);
    }

    static string Skrot(LogLevel l) => l switch
    {
        LogLevel.Critical => "KRYT", LogLevel.Error => "BLAD", LogLevel.Warning => "UWAG",
        LogLevel.Information => "INFO", _ => "DBG ",
    };

    public void Dispose() { }

    sealed class Rejestrator(DziennikPlikowy d, string kategoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => l >= LogLevel.Information;
        public void Log<TState>(LogLevel l, EventId id, TState s, Exception? ex, Func<TState, Exception?, string> f)
        {
            if (IsEnabled(l) && !kategoria.StartsWith("Microsoft", StringComparison.Ordinal)) d.Zapisz(l, kategoria, f(s, ex), ex);
        }
    }
}
