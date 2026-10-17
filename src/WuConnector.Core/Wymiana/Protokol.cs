using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WuConnector.Core.Wymiana;

// Typy protokolu tenanta v1 (API-TENANTA-v1.md, PRZYKLADY-API-TENANTA.md). Nazwy pol przez camelCase.

public sealed record StanConnectora(
    string Nip,
    string WersjaConnectora,
    string? WersjaR2,
    DateTimeOffset? OstatniUdanyCykl,
    string Stan,                 // ok | zapisWstrzymany | odczytWstrzymany
    string? StanOpis,            // maks. 200 znakow, bez danych osobowych
    IReadOnlyDictionary<string, string> Kursory,
    string? OdciskTozsamosci = null);   // 8 hex z sekretu osobaId — bezpiecznik tenanta przed dublami osob

public static class StanyConnectora
{
    public const string Ok = "ok";
    public const string ZapisWstrzymany = "zapisWstrzymany";
    public const string OdczytWstrzymany = "odczytWstrzymany";
}

public sealed record ZbiorStart(string? Kursor, string Tryb);

public sealed record OdpowiedzStart(
    string PrzebiegId,
    Dictionary<string, ZbiorStart> Zbiory,
    bool PrzegladKontrolny,
    int RozmiarPaczki,
    List<string> Wycofane);

/// <summary>GET /sync/stan — test polaczenia bez otwierania przebiegu (protokol v1, zmiana z 20.09.2026).</summary>
public sealed record OdpowiedzStanu(int Protokol, string Klucz, StanFirmyTenanta? Firma, DateTimeOffset? OstatniPrzebieg);

/// <summary>ok | firma_nieznana | firma_zgloszona | firma_poza_zakresem — te same wartosci co w 403.</summary>
public sealed record StanFirmyTenanta(string Stan);

public sealed record OdpowiedzPaczki(int Przyjeto, string? Kursor, int PominietePola);

public sealed record RodzajNieobecnosci(string Zrodlo, int Kod);

public sealed record OdnosnikZapisu(string Id);

public sealed record ZadanieTenanta(
    string RequestId,
    string Operacja,             // zapis | zmiana | usuniecie
    string? ZatrudnienieId,
    RodzajNieobecnosci? Rodzaj,
    string? DataOd,
    string? DataDo,
    List<OdnosnikZapisu>? Zapisy);

public sealed record OdpowiedzZadania(List<ZadanieTenanta> Zadania);

public sealed record ZapisWyniku(string Id, string? DataOd, string? DataDo, int? Dni, int? Minuty);

public sealed record WynikZadania(string RequestId, string Status, string? Kod, string? Komunikat, List<ZapisWyniku> Zapisy);

public static class StatusyWyniku
{
    public const string Zapisano = "zapisano";
    public const string ZapisanoZOstrzezeniem = "zapisanoZOstrzezeniem";
    public const string Blad = "blad";
    public const string BladPrzejsciowy = "bladPrzejsciowy";
    public const string Wycofano = "wycofano";
}

public sealed record OdrzuconyRekord(string Zbior, string Id, string? ZdarzenieId);

public sealed record PodsumowanieKonca(
    string Nip,
    string PrzebiegId,
    Dictionary<string, int> Wyslano,
    Dictionary<string, int> Usuniete,
    List<OdrzuconyRekord> Odrzucone,
    int ZadaniaPobrane,
    int WynikiWyslane,
    int PominietePola,
    long CzasMs);

public sealed record OdpowiedzKoniec(int NastepnyInterwal, bool PelnyPrzegladWymagany);

public sealed record PozycjaBledu(string Id, string Kod, string? Pole);

/// <summary>
/// Odpowiedz bledna serwera albo brak odpowiedzi. Status 0 = brak polaczenia / przekroczony czas.
/// Podzial wg API-TENANTA-v1.md §4.
/// </summary>
public sealed class BladProtokolu : Exception
{
    public int Status { get; }
    public string? Kod { get; }
    public string? ZdarzenieId { get; }

    /// <summary>
    /// Doprecyzowanie przyczyny obok `kod` — tenant dokłada je przy 409 (wygasl | zamkniety | zastapiony | nieznany)
    /// i przy 401 (uniewazniony_przez_administratora | zastapiony_nowym_kluczem). Nieznane wartosci tylko pokazujemy.
    /// </summary>
    public string? Powod { get; }
    public TimeSpan? RetryAfter { get; }
    public IReadOnlyList<PozycjaBledu> Pozycje { get; }

    public BladProtokolu(int status, string? kod, string? komunikat, string? zdarzenieId,
                         TimeSpan? retryAfter = null, IReadOnlyList<PozycjaBledu>? pozycje = null, Exception? inner = null,
                         string? powod = null)
        : base($"HTTP {status} {kod}: {komunikat}", inner)
    {
        Status = status; Kod = kod; ZdarzenieId = zdarzenieId; RetryAfter = retryAfter; Powod = powod;
        Pozycje = pozycje ?? Array.Empty<PozycjaBledu>();
    }

    /// <summary>408, 429, 5xx i brak polaczenia — ponawiac. Pozostale — nie ponawiac bez zmiany.</summary>
    public bool Przejsciowy => Status is 0 or 408 or 429 or >= 500;

    /// <summary>401 — zatrzymuje caly connector.</summary>
    public bool ZatrzymujeConnector => Status == 401;

    /// <summary>403 — zatrzymuje tylko te firme.</summary>
    public bool ZatrzymujeFirme => Status == 403;
}
