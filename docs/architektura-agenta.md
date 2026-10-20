> **Przeniesione z `C:\Symfonia\_projekt-urlopy` 20.10.2026.** Nazwiska, konta kadrowych, nazwy firm,
> NIP-y i nazwa hosta zastąpione wartościami fikcyjnymi — zasada projektu: do repozytorium nie trafiają
> prawdziwe dane osobowe ani dane klienta (`docs/stan.md`, sekcja 6). Numeracja rozdziałów bez zmian,
> bo kod odwołuje się do niej (np. „22.23 ustaleń").

# Architektura connectora wezurlop ↔ R2Płatnik

**Wersja 0.2, 19.09.2026. Do zatwierdzenia.** Zastępuje wersję 0.1 z 13.09 (kopia poprzedniej: `ARCHITEKTURA-agenta-v0.1.md.bak`).

Connector działa w sieci klienta, na serwerze z R2Płatnikiem. Czyta dane kadrowe z bazy SQL, wysyła zmiany do aplikacji wezurlop klienta, pobiera z niej zaakceptowane wnioski i zapisuje je do karty urlopowej R2 przez bibliotekę producenta. Konfiguruje się go w oknie z ikoną w zasobniku.

Źródła: `USTALENIA-2026-09-13-srodowisko-i-API.md` (pomiary i testy), `API-TENANTA-v1.md` + `PRZYKLADY-API-TENANTA.md` (protokół uzgodniony z programistą aplikacji), `KSZTALT-DANYCH-R2.md` (rekordy).

---

## 0. Co się zmieniło od wersji 0.1

| Było | Jest | Dlaczego |
|---|---|---|
| ryzyko modalnych okien R2 — warunek wstępny budowy | **zamknięte** — ostrzeżenia wracają jako tekst | test przekroczenia limitu i przełomu miesiąca (19.09) |
| biblioteka R2 z katalogu BIN instalacji | **własny, wersjonowany magazyn bibliotek** | serwer z R2 nie jest regularnie aktualizowany, stacje podnoszą wersję bazy |
| własna propozycja protokołu (`/agent/sync`) | **protokół tenanta v1** — 7 operacji, cykl per firma | uzgodniony z programistą aplikacji |
| jeden adres serwera | **adres = domena klienta**, jeden klucz na connector | aplikacja wezurlop instalowana u każdego klienta osobno |
| znaczniki czasu R2 „bezużyteczne" | pomocnicze; podstawą porównanie sum SHA-256 | usunięcia nie zostawiają śladu (22.15) |
| wykrywanie zmian — koncepcja | **zmierzone**: 12 tys. rekordów w 35 ms | 22.15 |
| saldo — hipoteza | **formuła potwierdzona** na 11 osobach i 36/36 etatowcach | 22.12 |
| licencja — ryzyko | API nie zajmuje stanowiska przy teście z jednej stacji | 22.14 (test z różnych stacji otwarty) |

---

## 1. Stos technologiczny

| Element | Technologia | Uzasadnienie |
|---|---|---|
| **Proces roboczy** (zapis do R2) | C#, **.NET Framework 4.8, x86** | biblioteka R2 jest 32-bitowa; Framework jest na każdym Windows Server; kod z `diag\Diag.cs` przetestowany |
| **Usługa Windows** | C#, **.NET 10 (LTS), x64**, self-contained | nowoczesny HTTP, gzip, JSON; nic do instalowania na serwerze. **Nie .NET 8** — wsparcie kończy się 10.11.2026; .NET 10 wspierany do 11.2028 i to jedyne SDK na maszynie |
| **Okno konfiguracji + zasobnik** | C#, **WinForms, .NET 10, x64** | natywny zasobnik; wygląd wezurlop.pl (rozdz. 8) |
| **Magazyn lokalny** | **SQLite** | stan wysłanych rekordów, rejestr zadań, kolejka wyników |
| Odczyt R2 | `Microsoft.Data.SqlClient` | bezpośrednio do MSSQL, bez sesji R2 |
| Instalator | MSI (WiX) | usługa + skróty + katalog `ProgramData` z ACL |

**Kod: `C:\WU_Connector`** (decyzja użytkownika 19.09.2026). Dokumentacja zostaje w `C:\Symfonia\_projekt-urlopy`.

**Narzędzia sprawdzone 19.09.2026** próbną kompilacją: .NET SDK 10.0.401; `net48` x86 buduje się przez pakiet `Microsoft.NETFramework.ReferenceAssemblies` (Developer Pack niepotrzebny); NuGet dostępny (Microsoft.Data.Sqlite, Microsoft.Data.SqlClient, Hosting.WindowsServices); WinForms net10 OK; git 2.55. WiX — z NuGet przy kroku 7.

**Bez Pythona.** Biblioteka R2 wymaga precyzyjnego marshallingu (Unicode, 1-bajtowe `Boolean`), a błąd nie zgłasza się, tylko daje ciche śmieci.

---

## 2. Trzy procesy

```
┌─ USŁUGA WINDOWS (x64) ──────────────────────────────┐
│  harmonogram cyklu, odczyt SQL, porównanie          │
│  HTTPS do aplikacji wezurlop klienta                │
│  SQLite: stan, rejestr zadań, kolejka wyników       │
│  NIGDY nie ładuje biblioteki R2                     │
└───────────────┬─────────────────────────────────────┘
                │ uruchamia na każde zadanie, twardy limit czasu
                ▼
┌─ PROCES ROBOCZY (x86) ──────────────────────────────┐
│  JEDNO zadanie → zapis w R2 → JSON na stdout        │
│  jedyny, który dotyka R2PApi10.dll                  │
│  ubijany przez watchdog bez ceregieli               │
└─────────────────────────────────────────────────────┘

┌─ OKNO + ZASOBNIK (x64) ─────────────────────────────┐
│  konfiguracja, status, dziennik, podgląd wysyłki    │
│  named pipe z ACL → usługa                          │
└─────────────────────────────────────────────────────┘
```

**Dlaczego osobny proces roboczy — trzy niezależne powody, wszystkie zmierzone:**

1. **Nieprzechwytywalna awaria.** Błędne wywołanie biblioteki kończy proces przez `AccessViolationException`, której .NET nie pozwala złapać (22.x, `GetGodzUrlPozostalo` → exit 139 mimo `try/catch`). W usłudze zabiłoby to całą synchronizację.
2. **Zawieszenie.** Biblioteka zostawia wątki; bez `ApiFinalize` proces wisi albo pada przy zamknięciu. Zawieszona próba nigdy nie wraca — tylko osobny proces da się ubić.
3. **Architektura.** Biblioteka jest x86, usługa x64.

**Okno jest osobnym procesem**, bo usługa działa w sesji 0 i nie może pokazać okna. Komunikacja przez named pipe — bez portu, bez reguły zapory.

---

## 3. Konfiguracja

```
Connector
├── serwer          { adres: https://urlopy.firma-klienta.pl, klucz (DPAPI), interwałMin: 30 s }
└── Instalacje[]                               ← katalog BIN instalacji R2
    ├── nazwa             "alfa"
    ├── katalogBin        C:\...\NowyR2Platnik.10_alfa\BIN\
    ├── sql               z RPlatnik.ini: serwer, baza _bin (wykrywane)
    ├── wersjaSchematu    BIN.VERSION (odczytywana co cykl)
    ├── kontoR2           { login, hasło (DPAPI) }      ← dedykowane, osobne w każdej instalacji
    └── Firmy[]                                ← z tabeli FIRM
        ├── firmIde       → ConnectDaneX(firmIde)
        ├── nip           klucz firmy w protokole
        ├── bazaDane      ..._dane_N
        ├── aktywna       czy synchronizujemy
        └── stanUTenanta  ok | niezatwierdzona | odmowa
```

**Jeden adres i jeden klucz na cały connector** — obejmują wszystkie instalacje R2 i firmy tego klienta. Klucz jest wymieniany z 30-dniową nakładką; connector przeładowuje konfigurację bez restartu.

### Wykrywanie zamiast wpisywania

1. Administrator wskazuje katalog — connector znajduje instalacje po `BIN\RPlatnik.ini`.
2. Z `.ini`: serwer SQL i nazwy baz.
3. Z bazy `_bin`: wersja schematu i lista firm (`FIRM`: nazwa, NIP).
4. Administrator zaznacza firmy i podaje konto R2.
5. **Testy przy zapisie konfiguracji**, każdy osobno z czytelnym wynikiem: połączenie SQL, logowanie do R2 (proces roboczy w trybie testu), połączenie z serwerem wezurlop (`start` bez wysyłki danych).

### Sekrety

Hasło konta R2 i klucz API — **DPAPI w zakresie maszyny** (zapisuje okno w sesji użytkownika, czyta usługa jako `LocalSystem`). Plik w `C:\ProgramData\wezurlop-connector\`, ACL: `SYSTEM` + `Administratorzy`. Klucz nigdy w logu ani w adresie URL.

### Konto usługi

Domyślnie `LocalSystem` z logowaniem Windows do SQL (tak działa na maszynie testowej). Alternatywa dla klientów z twardszą polityką: dedykowany login SQL tylko do odczytu (`db_datareader` na bazach R2).

---

## 4. Odczyt i wykrywanie zmian

**Odczyt wyłącznie przez SQL** — bez sesji R2, więc bez wpływu na licencję i bez ryzyka biblioteki.

W każdym cyklu, dla każdej firmy:

1. Jedno zapytanie czyta wszystkie rekordy wymiany (pracownik, zatrudnienie, limit, nieobecność, słownik, kalendarz) i liczy ich `suma` (SHA-256).
2. Porównanie z **ostatnim stanem potwierdzonym przez serwer** (SQLite):

| Sytuacja | Wysyłamy |
|---|---|
| nowy `id` | pełny rekord |
| inna `suma` | pełny rekord (serwer nadpisuje) |
| `id` zniknął | `{ "id": "…", "usuniety": true }` |
| bez zmian | nic |

3. Stan w SQLite aktualizujemy **dopiero po potwierdzeniu paczki** przez serwer.

Zmierzone: ALFA 12 021 rekordów — odczyt 35 ms; BETA 3 875 — 11 ms.

**Zabezpieczenie przed fałszywym masowym usunięciem** (np. po wskazaniu złej bazy):
- błąd odczytu → cykl bez porównania,
- nagle dużo usunięć (> 5% zbioru albo > 20 rekordów) → usunięcia wstrzymane, alarm dla administratora, reszta danych płynie dalej.

**Saldo** liczy connector formułą z 22.12 (minuty; korekty kadr uwzględnione) i wysyła w rekordzie `limit`. Saldo narastające R2 służy do kontroli — rozbieżność idzie do dziennika jako ostrzeżenie.

**Nie czytamy i nie wysyłamy:** PESEL (tylko `HMAC` jako `osobaId`), danych płacowych, kodów chorób, numerów zwolnień.

---

## 5. Wymiana z aplikacją wezurlop

Protokół: `API-TENANTA-v1.md`. **Jeden wspólny cykl dla wszystkich firm**, firmy obsługiwane kolejno:

```
dla każdej aktywnej firmy:
  start       ← stan connectora + kursory; dostaję przebiegId, tryb per zbiór, listę wycofanych
  dane        ← paczki ≤ 500 rekordów / 2 MB, gzip, per zbiór (tryb "zmiany" albo "pelny")
  przeglad    ← tylko gdy serwer poprosi (id + suma całego zbioru, domknięcie "ostatnia: true")
  zadania     ← GET, do 20 zadań
  [wykonanie zadań — rozdz. 6]
  wyniki      ← ponawiane aż do potwierdzenia
  koniec      ← podsumowanie; dostaję nastepnyInterwal
następny cykl = najkrótszy nastepnyInterwal, nie krócej niż 30 s
```

**Obsługa odpowiedzi:**

| Odpowiedź | Reakcja |
|---|---|
| `401` | **cały connector staje**, alarm („klucz nieważny") |
| `403` | **tylko ta firma staje**; przy `FIRMA_NIEZATWIERDZONA` — jednorazowe `zgloszenie-firmy`, potem próba co cykl |
| `409` | nowy przebieg; przy niezgodnej wersji protokołu — alarm |
| `413` | paczka dzielona na pół, ponowienie |
| `422` | usunięcie wskazanych rekordów z paczki, ponowienie reszty (nowy `X-Idempotency-Key`), odrzucone w `koniec` i dzienniku |
| `429` / `503` | `Retry-After`, bez niego wykładniczo do kilku minut |
| `5xx`, brak sieci | ponowienie z rosnącym odstępem; **wyniki zadań czekają w SQLite** — żaden wynik nie ginie |

`X-Idempotency-Key` każdej paczki zapisujemy przed wysłaniem — ponowienie po zerwanym łączu idzie z tym samym kluczem.

---

## 6. Wykonywanie zadań zapisu

**Jedno zadanie = jedno uruchomienie procesu roboczego.** Wynik jako JSON na stdout, niezależnie od kodu wyjścia.

### Przed uruchomieniem (usługa)

1. `requestId` na liście `wycofane` i niewykonany → wynik `wycofano`, bez uruchamiania.
2. `requestId` już w rejestrze z wynikiem → odesłanie zapisanego wyniku, bez ponownego zapisu.
3. **Kontrola po SQL**: czy w R2 jest już wpis z tym `requestId` w `OPIS` pokrywający zakres dat (zapis mógł się udać, a proces zostać ubity przed zgłoszeniem) → wynik z faktycznymi wpisami.
4. Operacja `zmiana` → wynik `blad` / `OPERACJA_NIEOBSLUGIWANA` (w etapie 1 serwer jej nie wydaje).

### Proces roboczy

```
SetDllDirectory(magazyn\wersja) → ConnectBin(BIN instalacji) → LoginUser(konto) → ConnectDaneX(firmIde)
→ PRACOWNICY → AKTUALNE ZATRUDNIENIE (pozycjonowanie po zatrudnienieId) → KARTA URLOPOWA
→ zapis:     TableInsert, R_DNIA, Od, Do, STATUS=0, OPIS=requestId, WYPL_NA_KOL_L=false → TablePost
  usunięcie: TableLocate(X_I) → TableDelete (brak wpisu = już usunięty = sukces)
→ po nieudanym TablePost: TableCancel
→ CloseAllTables → LogoffUser → FreeAllDataModules → ApiFinalize
```

- **Flaga „uwzględnij na kolejnej liście wynagrodzeń" zawsze wyłączona** — decyzja z 19.09, poza protokołem (22.17).
- Urlopu na przełomie miesiąca **nie dzielimy** — robi to R2; w wyniku kilka `zapisy`.
- **Wynik: `zapisy` = faktyczny stan w R2 po zadaniu**, odczytany po SQL, niezależnie od statusu.
- Komunikat R2 mapujemy na `kod` (tabela w `KSZTALT-DANYCH-R2.md`); oryginał idzie w `komunikat`.

### Watchdog i szeregowanie

- **Jeden proces roboczy naraz na instalację** (mutex) — praca pozycyjna, sesja związana z wątkiem.
- Limit czasu: **60 s** na zadanie (typowo 1,4–3,5 s). Po ubiciu: `bladPrzejsciowy` / `CONNECTOR_TIMEOUT`. Następne logowanie trwa ~15 s dłużej (R2 sprząta martwą sesję) — uwzględnione w limicie.
- Rezerwacja zadania po stronie serwera to 15 min — z zapasem wobec limitu i kilku zadań w kolejce.

### Błędy logowania do R2

| Komunikat R2 | Wynik | Stan firmy w `start` |
|---|---|---|
| „Użytkownik X jest zalogowany" | `bladPrzejsciowy`, `R2_NIEDOSTEPNE` | `zapisWstrzymany` + alarm, jeśli trwa > 1 h |
| złe hasło / brak konta | `blad`, `R2_KONFIGURACJA` | `zapisWstrzymany` + alarm od razu |
| brak licencji (jeśli wystąpi) | `bladPrzejsciowy`, `R2_NIEDOSTEPNE` | `zapisWstrzymany` |
| brak `.ini` / `.lic` / `.prt` | `blad`, `R2_KONFIGURACJA` | `zapisWstrzymany` + alarm |

**Ponawianie** jest w większości po stronie serwera: `bladPrzejsciowy` oddaje zadanie do puli i serwer wyda je w kolejnym cyklu. Connector ponawia sam tylko wysyłkę wyników i paczek.

---

## 7. Biblioteka R2 — magazyn wersji

- Connector trzyma biblioteki w `ProgramData\…\r2api\<wersja>\R2PApi10.dll`. **Pliki `.ini`, `.lic`, `.prt` zawsze z instalacji R2, nigdy kopiowane.**
- Wersję dobiera do **wersji schematu bazy** (`BIN.VERSION`), odczytywanej co cykl — aktualizację R2 na stacjach widzi w ciągu jednego cyklu.
- Brak pasującej wersji → pobranie z **naszego** serwera (wezurlop.pl, poza protokołem tenanta):
  - weryfikacja **podpisu Authenticode „Symfonia Sp. z o.o."** (plik producenta, niezmieniony),
  - weryfikacja **podpisanego przez nas manifestu** `wersja → SHA-256` (ochrona przed podsunięciem starszej wersji),
  - wpis w dzienniku: skąd, kiedy, SHA-256, sygnatariusz.
- Zapamiętuje **ostatnią wersję, z którą zapis się udał** — przy problemie komunikat dla administratora wskazuje konkretną parę wersji.
- Rozjazd wersji sam w sobie **nie blokuje** zapisu (biblioteka .32 poprawnie zapisywała do bazy .33); blokuje dopiero brak biblioteki albo nieudany test.

---

## 8. Okno konfiguracji i zasobnik

**Wygląd nawiązuje do wezurlop.pl** (grafika w `grafika-wezurlop\`):

| Element | Wartość ze strony |
|---|---|
| granat (nagłówki, tekst) | `#082B63`, tekst `#17345E`, drugorzędny `#60718E` |
| zieleń (akcje, „OK") | `#10A76B`, ciemniejsza `#07905A` |
| pomarańcz (ostrzeżenia) | `#FF9E11` |
| koral (błędy) | `#FF5F45` |
| tła kart | miętowe `#E8FAEF`, niebieskie `#EEF7FF`, żółte `#FFF8E9`, różowe `#FFF0F3` |
| linie | `#E4EBF3` |
| czcionka | Inter (zapas: Segoe UI) |
| kształty | zaokrąglone karty (promień 12–24 px), przyciski z zielonym gradientem |
| logo | `logo.png`; ikona zasobnika z „uśmiechu" logo; maskotka na ekranie powitalnym |

Układ jak panel aplikacji: menu po lewej, treść w kartach.

| Ekran | Zawartość |
|---|---|
| **Status** | stan całości (zielony / pomarańczowy / czerwony), ostatni cykl, per firma: wysłane, zadania, błędy |
| **Instalacje i firmy** | wykryte instalacje, wersje (program / baza / biblioteka), firmy z NIP — zaznaczanie do synchronizacji, stan u serwera (np. „czeka na zatwierdzenie") |
| **Połączenie** | adres serwera, klucz (ukryty), „Testuj" |
| **Konto R2** | login i hasło per instalacja, „Testuj logowanie" |
| **Podgląd wysyłki** | co poszłoby w najbliższym cyklu — bez wysyłania (liczby per zbiór, przykładowe rekordy) |
| **Dziennik** | zdarzenia i błędy, `zdarzenieId` serwera do przekazania wsparciu |

Ikona w zasobniku zmienia kolor wraz ze stanem; dymek przy błędzie wymagającym człowieka.

---

## 9. Czego świadomie nie robimy

- **Zapisu po SQL** — nigdy, także awaryjnie. R2 sam wylicza dni i godziny, rozbija przełom miesiąca i aktualizuje kartę pracy; w bazie nie ma kluczy obcych.
- **Automatycznej aktualizacji connectora** — okno informuje o nowej wersji, decyduje człowiek.
- **Operacji `zmiana`** w etapie 1.
- **Sterowania flagą wypłaty** przez aplikację.
- **Czytania przez API R2** — wolniejsze i otwiera sesję.
- **Zmian w bazie producenta** (Change Tracking, triggery, własne tabele).

---

## 10. Kolejność budowy

1. **Proces roboczy** — `Diag.cs` → docelowy program: wejście JSON, wyjście JSON, zapis / usunięcie / test logowania. Niesie całe ryzyko współpracy z biblioteką.
2. **Odczyt i porównanie** — zapytania, sumy, SQLite, formuła salda; test na obu bazach.
3. **Atrapa serwera wezurlop** — z `PRZYKLADY-API-TENANTA.md`, łącznie z błędami 401/403/409/413/422/429.
4. **Usługa** — cykl, wymiana z atrapą, kolejka wyników, watchdog.
5. **Okno i zasobnik** — konfiguracja z wykrywaniem, testy, status, podgląd, dziennik.
6. **Magazyn bibliotek** — pobieranie, podpis, manifest.
7. **Instalator** i podłączenie do instancji testowej tenanta (gdy ją udostępni).

---

## 11. Otwarte

| Sprawa | Wpływ | Kiedy potrzebne |
|---|---|---|
| instancja testowa tenanta + OpenAPI | test na prawdziwym serwerze | krok 7 |
| licencja przy osobach zalogowanych z **innych stacji** | komunikaty, instrukcja wdrożenia | przed wdrożeniem |
| grupa minimalna uprawnień konta R2 (eksport grupy) | instrukcja wdrożenia | przed wdrożeniem |
| **kilka firm w jednej instalacji** (`ConnectDaneX(n≠1)`) — niesprawdzone | konfiguracja, proces roboczy | przed pierwszym takim klientem |
| hosting bibliotek na wezurlop.pl + klucz podpisu manifestu | krok 6 | krok 6 |
| certyfikat do podpisu connectora (exe, MSI) | instalator | krok 7 |
