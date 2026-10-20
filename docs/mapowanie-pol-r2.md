> **Przeniesione z `C:\Symfonia\_projekt-urlopy` 20.10.2026.** Nazwiska, konta kadrowych, nazwy firm,
> NIP-y i nazwa hosta zastąpione wartościami fikcyjnymi — zasada projektu: do repozytorium nie trafiają
> prawdziwe dane osobowe ani dane klienta (`docs/stan.md`, sekcja 6). Numeracja rozdziałów bez zmian,
> bo kod odwołuje się do niej (np. „22.23 ustaleń").
>
> **Dokument częściowo NIEAKTUALNY:** pola `wyplataNaKolejnejLiscie` i `ustawWyplate` zostały wycofane
> (22.17 ustaleń) — connector zawsze zapisuje flagę wyłączoną. Reszta mapowania obowiązuje.

# Mapowanie pól R2 → rekordy wymiany — DOKUMENT WEWNĘTRZNY

> **19.09.2026:** `wyplataNaKolejnejLiscie` / `ustawWyplate` poniżej są NIEAKTUALNE — flaga zawsze `false`, poza protokołem (22.17 ustaleń).

**Tylko dla zespołu connectora. Nie wysyłać tenantowi** — tenant dostaje `KSZTALT-DANYCH-R2.md` (same pola i znaczenie, bez źródeł w R2). Wersja 0.1, 19.09.2026.

Dokument opisuje **wyłącznie rekordy** — co connector potrafi wyciągnąć z R2, z której kolumny i z jakimi zastrzeżeniami. Protokół (fazy, endpointy, kursory, porcjowanie) należy do tenanta i tu go nie projektujemy.

Każde pole ma podane źródło w R2. Wszystko zweryfikowane na kopii bazy klienta odniesienia (R2 10.10.33, dwie firmy), chyba że oznaczono inaczej.

## Zasady ogólne

| | |
|---|---|
| daty nieobecności | `"2026-10-29"` — data bez godziny i strefy |
| znaczniki zdarzeń | ISO 8601 z przesunięciem, np. `"2026-09-19T16:25:56+02:00"` (R2 trzyma czas lokalny bez strefy; connector przelicza na `Europe/Warsaw`) |
| czas urlopu | **minuty jako wartość źródłowa** (`int`); dni tylko do prezentacji |
| kodowanie | UTF-8 |
| identyfikatory R2 | `int`, **unikalne tylko w obrębie jednej firmy** — zawsze w parze z `firma` (NIP) |
| `suma` | SHA-256 (hex) z kanonicznego JSON pól rekordu, liczona przez connector |

**Czego NIGDY nie wysyłamy:** PESEL (w żadnej postaci), dane płacowe, kody chorób, numery zwolnień lekarskich, kwoty zasiłków.

---

## Faza 1 — pobranie

### `firma`

```json
{ "typ": "firma",
  "firma": "0000000000",
  "nazwa": "ALFA",
  "r2": { "instalacja": "alfa", "firmIde": 1,
          "wersjaSchematu": "10.10.33", "wersjaBiblioteki": "10.10.33" } }
```

| Pole | Źródło | Uwagi |
|---|---|---|
| `firma` | `FIRM.NIP` (bez kresek) | **klucz firmy**; `FIRM.X_I` nie nadaje się — obie firmy klienta mają `1` |
| `nazwa` | `FIRM.NAZWA` | |
| `r2.wersjaSchematu` | `BIN.VERSION` (aktywny wiersz) | wersja R2 na stacjach — wykrywa aktualizację |
| `r2.wersjaBiblioteki` | `FileVersion` używanej `R2PApi10.dll` | informacyjnie |

### `pracownik` (kartoteka)

```json
{ "typ": "pracownik",
  "firma": "0000000000",
  "pracownikId": 207,
  "osobaId": "hmac:3f9a…",
  "nazwisko": "KOWALSKI", "imie": "KRZYSZTOF",
  "identyfikator": "397",
  "zwolniony": false, "dataZwolnienia": null,
  "skasowany": false,
  "umowy": { "oPrace": true, "zlecenie": false, "oDzielo": false },
  "suma": "…" }
```

| Pole | Źródło | Uwagi |
|---|---|---|
| `pracownikId` | `PRACOWNK.X_I` | stabilny w firmie, ale ponowne zatrudnienie **czasem** tworzy nową kartotekę (5 przypadków u klienta) |
| `osobaId` | `HMAC-SHA256(sekret_klienta, PRACDANE.PESEL)` | **wspólny dla firm**; sekret nie opuszcza connectora; `null`, gdy brak PESEL (u klienta wyłącznie kartoteki skasowane) |
| `identyfikator` | `PRACOWNK.Identyfikator` | numer kadrowy — **pusty w 222/353**, tylko do wyświetlania |
| `zwolniony` | `PRACOWNK.Zwolniony` | rozwiązany stosunek pracy |
| `dataZwolnienia` | `PRACOWNK.Data_zwolnienia` | **bywa `null` mimo `zwolniony = true`** (31 starych kartotek) |
| `skasowany` | `PRACOWNK.Skasowany` | kartoteka usunięta — **inny stan niż zwolnienie** |
| `umowy` | `PRACOWNK.Umowa_o_prace`, `Umowa_zlecenie`, `Umowa_o_dzielo` | urlop przysługuje przy `oPrace` |

### `zatrudnienie`

**Klucz nieobecności.** Urlop w R2 wisi pod zatrudnieniem, nie pod pracownikiem.

```json
{ "typ": "zatrudnienie",
  "firma": "0000000000",
  "zatrudnienieId": 207,
  "pracownikId": 207,
  "aktualne": true, "domyslne": true,
  "etat": 1.0,
  "dataRozpoczecia": "2004-07-01", "dataZakonczenia": null,
  "kalendarzId": 2,
  "suma": "…" }
```

| Pole | Źródło | Uwagi |
|---|---|---|
| `zatrudnienieId` | `HISTORIA.X_I` | **zmienia się przy ponownym zatrudnieniu** — stare urlopy zostają pod starym (943 wpisy u klienta) |
| `aktualne` | `HISTORIA.AktZatrudnienie` | |
| `domyslne` | `HISTORIA.X_Default` | 7 osób ma kilka aktualnych zatrudnień |
| `etat` | `UMPRACA.Etat_licznik / Etat_mianownik` (ostatnia umowa) | **ułamek dziesiętny** — R2 zapisuje tę samą wartość różnie (`1/1` i `8/8`) |
| `dataRozpoczecia` | `UMPRACA.Data_rozpoczecia_pracy` | |
| `dataZakonczenia` | `UMPRACA.Data_zakonczenia_pracy` | koniec **tego** zatrudnienia |
| `kalendarzId` | `HISTORIA.X_IKalendarz` | → rekord `kalendarz` |

### `limit`

```json
{ "typ": "limit",
  "firma": "0000000000",
  "zatrudnienieId": 207,
  "rok": 2026,
  "rodzaj": "wypoczynkowy",
  "naliczony": true,
  "wymiarMin": 12480,
  "zaleglyMin": 6120,
  "dostepneNaStartMin": 18600,
  "wykorzystaneMin": 7200,
  "korektyMin": 0,
  "pozostaloMin": 11400,
  "naZadanieWykorzystaneDni": 0,
  "minutNaDzien": 480,
  "suma": "…" }
```

| Pole | Źródło |
|---|---|
| `rodzaj` | `wypoczynkowy` = wiersz `KART_URL` z `L=1, R_DNIA=0`; `opieka_art188` = `L=1, R_DNIA=2` |
| `naliczony` | **`false`, gdy wiersz limitu na dany rok nie istnieje** — wtedy pozostałe pola `null` |
| `wymiarMin` | `LIMIT_GODZ` |
| `dostepneNaStartMin` | `POZOSTALO_GODZ` — wymiar + zaległy |
| `zaleglyMin` | `POZOSTALO_GODZ − LIMIT_GODZ` |
| `wykorzystaneMin` | `SUM(IL_GODZ)` z wpisów `L=0` roku: dla wypoczynkowego `R_DNIA IN (0,5)`, dla opieki `R_DNIA = 2` |
| `korektyMin` | `SUM(KOR_POZOST_GODZ)` z tych samych wpisów, gdzie `KOR_POZOST = 1` — **ręczne korekty salda wprowadzone przez kadry**, dodatnie lub ujemne |
| `pozostaloMin` | `dostepneNaStartMin − wykorzystaneMin + korektyMin` |
| `naZadanieWykorzystaneDni` | `SUM(IL_DNI)` z `R_DNIA = 5` w roku |
| `minutNaDzien` | `KALENDAR.DZIEN × 60` — do przeliczenia na dni |

**Uwagi:**

- Kolumna `POZOSTALO` (w dniach) w R2 jest niewiarygodna — bywa ujemna. Używamy wyłącznie kolumn w minutach.
- Formuła zgodna z ekranem R2 co do godziny u **11 pracowników, w tym na ½ i ¾ etatu**, oraz z saldem narastającym liczonym przez R2 u **wszystkich 36 etatowców**.
- **Prezentacja w dniach: zawsze `minuty ÷ 480`, także przy niepełnym etacie** — tak pokazuje R2 (osoba na ½ etatu z 7 440 min widzi `15D 4G`, nie `31D`). Dzień w saldzie to 8 godzin niezależnie od etatu, choć jeden dzień jej urlopu zużywa 240 minut.
- Kadry zakładają limity na nowy rok ręcznie, **głównie ok. 22 stycznia** — do tego czasu `naliczony = false`. Limitu równego zero nie ma w danych ani razu.
- R2 **nie blokuje** przekroczenia limitu wypoczynkowego, ale **blokuje** piąty dzień na żądanie.

### `nieobecnosc`

Trzy źródła w R2, **kody się nakładają** — rekord identyfikuje para `zrodlo + kod`.

```json
{ "typ": "nieobecnosc",
  "firma": "0000000000",
  "id": "urlop:12315",
  "zrodlo": "urlop",
  "kod": 0,
  "zatrudnienieId": 207,
  "od": "2026-10-29", "do": "2026-10-31",
  "dni": 2, "minuty": 960,
  "requestId": "wns_000123",
  "wyplataNaKolejnejLiscie": true,
  "kontynuacja": false,
  "utworzono": "2026-09-19T10:09:56+02:00", "zmieniono": null,
  "suma": "…" }
```

| `zrodlo` | Tabela R2 | Kod | Daty | Dni | Minuty |
|---|---|---|---|---|---|
| `urlop` | `KART_URL` (`L = 0`) | `R_DNIA` | `OD`, `DO` | `IL_DNI` | `IL_GODZ` |
| `kadrowa` | `OKRESHIS` | `Rodzaj_nieobecnosci` | `Od`, `Do` | `IlDni` | `IL_GODZ` |
| `zus` | `KART_ZAS` | `R_NIEOB` | `OKRES_OD`, `OKRES_DO` | `IL_DNI_ZAS` | brak |

| Pole | Uwagi |
|---|---|
| `id` | `zrodlo:X_I` — `X_I` jest unikalny w obrębie tabeli i firmy |
| `zatrudnienieId` | `X_I_HISTORII` (`urlop`, `zus`), `X_IHistoria` (`kadrowa`) — **uwaga na różną pisownię kolumn** |
| `requestId` | tylko `urlop`: z `OPIS`, jeśli pasuje do wzorca identyfikatora tenanta; `null` dla wpisów kadr |
| `wyplataNaKolejnejLiscie` | tylko `urlop`: `WYPL_NA_KOL_L` |
| `kontynuacja` | tylko `zus`: `KONTYNUACJA` — **591 z 884** wpisów to kontynuacje L4; jedno zwolnienie = kilka rekordów, do prezentacji sklejać |
| `utworzono`, `zmieniono` | `X_F7DataCzas`, `X_F6DataCzas` — informacyjnie; wykrywanie zmian opiera się na `suma` |

**Nieobecności z ZUS (`zus`) — wyłącznie rodzaj i daty.** Rekomendacja: współpracownicy i przełożeni widzą „nieobecny", konkretny rodzaj tylko kadry — to dane o zdrowiu.

**Pomijać w kalendarzu:** `urlop` z kodem 6 i 8 (ekwiwalenty — rozliczenie płacowe, nie nieobecność).

### `slownik`

Przekazywany z bazy, żeby tenant nie zaszywał kodów.

```json
{ "typ": "slownik", "zrodlo": "urlop", "kod": 5,
  "nazwa": "urlop wyp. na żądanie",
  "tylkoOdczyt": false, "doWniosku": true, "nieobecnosc": true }
```

| Flaga | Znaczenie |
|---|---|
| `tylkoOdczyt` | `true` dla całego źródła `zus` i `kadrowa` — pracownik tego nie wnioskuje |
| `doWniosku` | etap 1: tylko `urlop` z kodem 0 i 5 |
| `nieobecnosc` | `false` dla ekwiwalentów (`urlop` 6 i 8) |

Źródło nazw: `<BIN>.fn_Kod(tabela, pole, kod)`. Pełna lista — `ODPOWIEDZ-dla-programisty-tenanta.md`, punkt 7.

### `kalendarz`

Do **orientacyjnego** liczenia dni po stronie tenanta. Wiążące dni i minuty zwraca R2 po zapisie.

```json
{ "typ": "kalendarz",
  "kalendarzId": 2, "nazwa": "Główny",
  "rok": 2026,
  "indywidualne": true,
  "dniNierobocze": ["2026-01-01", "2026-01-03", "…"],
  "minutNaDzienRoboczy": 480,
  "suma": "…" }
```

| Pole | Źródło | Uwagi |
|---|---|---|
| `dniNierobocze` | `LATA_KAL` — blob na miesiąc, 2 bajty/dzień: `0` = wolne, `9999` = święto/niedziela, pozostałe = minuty pracy | zgodność z R2: **717 z 719** urlopów |
| `indywidualne` | `KALENDAR.Indywidualne` | `true` = pracownik może mieć harmonogram nadpisujący kalendarz — stąd pozostałe 2 rozjazdy |

**Nie liczyć godzin jako dni × 8** — R2 bierze je z etatu i harmonogramu (w danych 240, 360, 480, 720 min na dzień).

### Przegląd kontrolny (pełna lista)

Na żądanie tenanta albo okresowo. Tylko identyfikatory i sumy.

```json
{ "typ": "przeglad", "firma": "0000000000",
  "rekordy": [ { "typ": "nieobecnosc", "id": "urlop:12315", "suma": "…" }, "…" ] }
```

Koszt po stronie R2: pełny przegląd wszystkich tabel z sumami — **17 ms**.

---

## Faza 3 — wysyłka (tenant → connector)

```json
{ "typ": "zapiszUrlop",
  "requestId": "wns_000123",
  "firma": "0000000000",
  "zatrudnienieId": 207,
  "kod": 0,
  "od": "2026-10-29", "do": "2026-11-03",
  "wyplataNaKolejnejLiscie": null }
```

| Pole | Uwagi |
|---|---|
| `requestId` | trafia do `KART_URL.OPIS` (`varchar(50)`) — **maks. 32 znaki ASCII** |
| `zatrudnienieId` | **obowiązkowe**; connector nie zgaduje zatrudnienia |
| `kod` | etap 1: `0` lub `5` |
| `od`, `do` | pełny zakres — **R2 sam dzieli przełom miesiąca**; nie dzielić po stronie tenanta |
| `wyplataNaKolejnejLiscie` | `null` = domyślnie R2 (`true`) |

Pozostałe operacje (zweryfikowane przez API):

```json
{ "typ": "usunUrlop",    "requestId": "…", "firma": "…", "r2Ids": [12315, 12316] }
{ "typ": "ustawWyplate", "requestId": "…", "firma": "…", "r2Id": 12316, "wartosc": true }
```

Zmiana dat istniejącego wpisu też działa (R2 przelicza dni i minuty), ale proponuję ją odłożyć — anulowanie przez `usunUrlop` i nowy wniosek jest prostsze.

---

## Faza 4 — potwierdzenie (connector → tenant)

```json
{ "typ": "wynik",
  "requestId": "wns_000123",
  "status": "zapisanoZOstrzezeniem",
  "kod": "OSTRZ_PRZELOM_MIESIACA",
  "komunikatR2": "Urlop na przełomie miesiąca. Wpis zostanie rozbity. (Ostrzeżenie)",
  "czesci": [
    { "r2Id": 12315, "od": "2026-10-29", "do": "2026-10-31", "dni": 2, "minuty": 960 },
    { "r2Id": 12316, "od": "2026-11-01", "do": "2026-11-03", "dni": 2, "minuty": 960 } ],
  "dniRazem": 4, "minutyRazem": 1920,
  "czas": "2026-09-19T10:09:57+02:00" }
```

`status`: `zapisano` | `zapisanoZOstrzezeniem` | `blad`.

> **Wersja dla tenanta (0.2):** doszedł `status: bladPrzejsciowy`; `R2_KONTO_ZAJETE` i ewentualny brak licencji zgłaszamy jednym kodem `R2_NIEDOSTEPNE` — tenant nie musi znać przyczyny, szczegół tylko w logu connectora.

R2 nie ma kodów błędów — zwraca tylko tekst. Connector nadaje kody na podstawie treści i **zawsze** przekazuje oryginalny komunikat w `komunikatR2`:

| `kod` | Komunikat R2 | `status` |
|---|---|---|
| `OSTRZ_PRZELOM_MIESIACA` | „Urlop na przełomie miesiąca. Wpis zostanie rozbity. (Ostrzeżenie)" | zapisano z ostrzeżeniem |
| `OSTRZ_LIMIT_PRZEKROCZONY` | „{NAZWISKO IMIĘ}: limit urlopu wypoczynkowego za rok {R} został przekroczony (Ostrzeżenie)" | zapisano z ostrzeżeniem |
| `OSTRZ_NA_ZADANIE_WYKORZYSTANY` | „{NAZWISKO IMIĘ}: limit urlopu na żądanie (max.4 dni) został wykorzystany (Ostrzeżenie)" | zapisano z ostrzeżeniem |
| `R2_KOLIZJA` | „Urlop pokrywa się z wcześniejszymi okresami" | błąd |
| `R2_NA_ZADANIE_PRZEKROCZONY` | „Limit urlopu na żądanie (max.4 dni) został przekroczony" | błąd |
| `R2_KONTO_ZAJETE` | „Błąd przy logowaniu użytkownika X (Użytkownik X jest zalogowany na …)" | błąd przejściowy |
| `R2_KONFIGURACJA` | „Problem z odczytem licencji z plików …" / „… ustawień z pliku …" | błąd |
| `R2_NIEZNANY` | dowolny inny tekst | błąd |
| `CONNECTOR_TIMEOUT` | — (proces roboczy przerwany po limicie czasu) | błąd przejściowy |

**Komunikaty R2 zawierają imię i nazwisko pracownika** — to dane osobowe także w tym polu.

Przy znanym `requestId` connector **nie zapisuje ponownie**, tylko odsyła istniejący wynik. Dodatkowe zabezpieczenia: `requestId` w `OPIS` wpisu oraz odrzucenie przez R2 zapisu na te same daty jako kolizji.
