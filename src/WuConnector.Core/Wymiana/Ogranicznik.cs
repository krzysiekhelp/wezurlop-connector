namespace WuConnector.Core.Wymiana;

/// <summary>
/// Okno przesuwne: nie wiecej niz <c>limit</c> zadan w <c>okno</c>. Nadmiarowe zadanie czeka, zamiast dostac 429.
/// Serwer tenanta pozwala na 120 zadan/min na klucz (PRZYKLADY/mail tura 7) — connector trzyma sie 100,
/// zeby zostawic zapas na test polaczenia z okna i zegar przesuniety wzgledem serwera.
/// Jeden klucz = jeden connector = jeden ogranicznik dla wszystkich firm.
/// </summary>
public sealed class Ogranicznik
{
    public static Ogranicznik DlaTenanta() => new(100, TimeSpan.FromMinutes(1));

    readonly int _limit;
    readonly TimeSpan _okno;
    readonly Queue<DateTime> _znaczniki = new();
    readonly SemaphoreSlim _zamek = new(1, 1);
    readonly Func<DateTime> _teraz;
    readonly Func<TimeSpan, CancellationToken, Task> _czekaj;

    public Ogranicznik(int limit, TimeSpan okno, Func<DateTime>? teraz = null, Func<TimeSpan, CancellationToken, Task>? czekaj = null)
    {
        _limit = limit;
        _okno = okno;
        _teraz = teraz ?? (() => DateTime.UtcNow);
        _czekaj = czekaj ?? Task.Delay;
    }

    /// <summary>Ile razy zadanie musialo czekac — do dziennika i testow.</summary>
    public int Opoznien { get; private set; }

    public async Task CzekajNaMiejsceAsync(CancellationToken ct)
    {
        await _zamek.WaitAsync(ct);
        try
        {
            while (true)
            {
                var teraz = _teraz();
                while (_znaczniki.Count > 0 && teraz - _znaczniki.Peek() >= _okno) _znaczniki.Dequeue();
                if (_znaczniki.Count < _limit) { _znaczniki.Enqueue(teraz); return; }
                Opoznien++;
                await _czekaj(_znaczniki.Peek() + _okno - teraz, ct);
            }
        }
        finally { _zamek.Release(); }
    }
}
