namespace WuConnector.Core.Cykl;

/// <summary>
/// Reczne wywolanie synchronizacji: okno wysyla do uslugi Windows polecenie uzytkownika (ServiceController.ExecuteCommand),
/// usluga przerywa oczekiwanie i od razu wykonuje cykl. Polecenie moze wyslac zalogowany uzytkownik bez uprawnien
/// administratora (domyslne prawo SERVICE_USER_DEFINED_CONTROL dla uzytkownikow interaktywnych) — niczego nie konfiguruje.
/// </summary>
public sealed class SygnalSynchronizacji
{
    /// <summary>Kod polecenia uzytkownika uslugi (zakres 128–255).</summary>
    public const int Polecenie = 200;

    readonly SemaphoreSlim _sygnal = new(0, 1);

    /// <summary>Zleca cykl. Kilka zlecen w trakcie trwajacego cyklu = jeden kolejny cykl.</summary>
    public void Zlec()
    {
        try { _sygnal.Release(); } catch (SemaphoreFullException) { /* juz zlecony */ }
    }

    /// <summary>Czeka na odstep albo na reczne zlecenie. True = zlecenie reczne.</summary>
    public async Task<bool> CzekajAsync(TimeSpan odstep, CancellationToken ct)
    {
        try { return await _sygnal.WaitAsync(odstep, ct); }
        catch (ArgumentOutOfRangeException) { return await _sygnal.WaitAsync(TimeSpan.FromDays(1), ct); }   // odstep > ~24 dni
    }
}
