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
