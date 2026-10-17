namespace WuConnector.Core.Biblioteki;

/// <summary>
/// Klucz publiczny, ktorym connector sprawdza podpis manifestu bibliotek.
/// Prywatna para lezy u wydawcy (tools/Manifest generuj-klucz) — nigdy w repozytorium ani na serwerze WWW.
/// Zmiana klucza = nowa wersja connectora.
/// </summary>
public static class KluczManifestu
{
    // KLUCZ DEWELOPERSKI (19.09.2026, C:\WU_Connector_klucze\). Przed pierwszym wdrozeniem u klienta:
    // wydawca generuje klucz produkcyjny na odlaczonej maszynie i podmienia te stala.
    public const string Publiczny = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0l/GnX/kfIto8eJRqokw9F451Ady
        DplwnyG20q7xlWhX9Xfw6z5NCs22wQ+CBGqzEolJ8qmYzM/Wm+Du8YAl3g==
        -----END PUBLIC KEY-----
        """;
}
