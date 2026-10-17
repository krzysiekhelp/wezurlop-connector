namespace WuConnector.Core.Odczyt;

// Surowe wiersze z baz R2 — 1:1 z kolumnami, bez logiki. Logika (saldo, sumy, mapowanie)
// jest w Rekordy/, zeby dalo sie ja testowac bez bazy.
//
// Nazwy kolumn: patrz MAPOWANIE-POL-R2-wewnetrzne.md. Uwaga na pisownie — OKRESHIS.X_IHistoria,
// ale KART_URL.X_I_HISTORII i KART_ZAS.X_I_HISTORII.

public sealed record FirmaWiersz(string Nip, string Nazwa);

/// <summary>PRACOWNK + PRACDANE.PESEL. PESEL zyje tylko w pamieci — do HMAC, nigdy nie jest zapisywany ani wysylany.</summary>
public sealed record PracownikWiersz(
    int Id, string Nazwisko, string Imie, string? Identyfikator,
    bool Zwolniony, DateTime? DataZwolnienia, bool Skasowany,
    bool UmowaOPrace, bool UmowaZlecenie, bool UmowaODzielo,
    string? Pesel);

/// <summary>HISTORIA (nieskasowane) + ostatnia umowa o prace z UMPRACA (moze jej nie byc).</summary>
public sealed record ZatrudnienieWiersz(
    int Id, int PracownikId, bool Aktualne, bool Domyslne, int? KalendarzId,
    double? EtatLicznik, double? EtatMianownik, DateTime? DataRozpoczecia, DateTime? DataZakonczenia);

/// <summary>KART_URL — wiersze okresow (L=0) i limitow (L=1).</summary>
public sealed record UrlopWiersz(
    int Id, int? ZatrudnienieId, bool L, int RDnia,
    DateTime? Od, DateTime? Do, int? IlDni, int? IlGodz,
    int? LimitGodz, int? PozostaloGodz, bool KorPozost, int? KorPozostGodz,
    string? Opis, int? UtworzylUzytkownik, DateTime? Utworzono, DateTime? Zmieniono);

/// <summary>OKRESHIS — nieobecnosci kadrowe (bez anulowanych).</summary>
public sealed record KadroweWiersz(
    int Id, int? ZatrudnienieId, int Rodzaj, DateTime Od, DateTime Do,
    int? IlDni, int? IlGodz, DateTime? Utworzono, DateTime? Zmieniono);

/// <summary>KART_ZAS — WYLACZNIE rodzaj i daty. Kody chorob, numery zwolnien i kwoty nie sa nawet czytane.</summary>
public sealed record ZasilekWiersz(
    int Id, int? ZatrudnienieId, int Rodzaj, DateTime Od, DateTime Do,
    int? IlDni, bool Kontynuacja, DateTime? Utworzono, DateTime? Zmieniono);

/// <summary>Pozycja slownika z &lt;BIN&gt;.fn_Kod(tabela, pole, kod).</summary>
public sealed record SlownikWiersz(string Zrodlo, int Kod, string Nazwa);

public sealed record KalendarzWiersz(int Id, string? Nazwa, bool Indywidualne, int? NormaUrlopowaMin, int? NormaDobowaMin);

/// <summary>LATA_KAL — 12 blobow (styczen..grudzien), 2 bajty na dzien, little-endian.</summary>
public sealed record LataKalWiersz(int KalendarzId, int Rok, byte[]?[] Miesiace);

/// <summary>Komplet surowych danych jednej firmy z jednego odczytu.</summary>
public sealed record SuroweDane(
    FirmaWiersz Firma,
    IReadOnlyList<PracownikWiersz> Pracownicy,
    IReadOnlyList<ZatrudnienieWiersz> Zatrudnienia,
    IReadOnlyList<UrlopWiersz> Urlopy,
    IReadOnlyList<KadroweWiersz> Kadrowe,
    IReadOnlyList<ZasilekWiersz> Zasilki,
    IReadOnlyList<SlownikWiersz> Slownik,
    IReadOnlyList<KalendarzWiersz> Kalendarze,
    IReadOnlyList<LataKalWiersz> LataKal,
    DateTime CzasOdczytu,
    string? WersjaBazy = null);   // BIN.VERSION, np. "10.10.33" — do doboru biblioteki R2
