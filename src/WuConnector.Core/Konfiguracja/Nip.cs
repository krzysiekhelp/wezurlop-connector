namespace WuConnector.Core.Konfiguracja;

/// <summary>
/// NIP ze sprawdzeniem cyfry kontrolnej — serwer wezurlop odrzuca bledny numer kodem 422 NIP_NIEPOPRAWNY
/// (tura 8). Connector sprawdza go sam, zeby literowka w R2 dala czytelny komunikat w dzienniku.
/// </summary>
public static class Nip
{
    static readonly int[] Wagi = { 6, 5, 7, 2, 3, 4, 5, 6, 7 };

    /// <summary>Usuwa myslniki, spacje i prefiks PL — tak samo jak serwer.</summary>
    public static string Normalizuj(string nip)
    {
        var s = new string(nip.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray());
        return s.StartsWith("PL", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
    }

    // NIP idzie do dziennika connectora w pelnej postaci — decyzja uzytkownika z 17.10.2026, koryguje
    // wczesniejsze maskowanie. Uzasadnienie: dziennik lezy na serwerze klienta, a kto ma dostep do
    // connectora, ma i tak dostep do danych kadrowych; skracanie numeru utrudnialo diagnoze, nie dajac
    // w zamian ochrony. Co innego ADRES zadania do tenanta: tam NIP-u nie ma i byc nie moze, bo trafialby
    // do logu dostepu serwera WWW — stad naglowek X-Nip zamiast parametru (patrz KlientTenanta).

    public static bool CzyPoprawny(string nip)
    {
        var n = Normalizuj(nip);
        if (n.Length != 10 || !n.All(char.IsAsciiDigit)) return false;
        int suma = 0;
        for (int i = 0; i < 9; i++) suma += (n[i] - '0') * Wagi[i];
        int k = suma % 11;
        return k != 10 && k == n[9] - '0';
    }
}
