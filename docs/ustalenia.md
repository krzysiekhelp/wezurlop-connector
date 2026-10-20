> **Przeniesione z `C:\Symfonia\_projekt-urlopy` 20.10.2026.** Nazwiska, konta kadrowych, nazwy firm,
> NIP-y i nazwa hosta zastąpione wartościami fikcyjnymi — zasada projektu: do repozytorium nie trafiają
> prawdziwe dane osobowe ani dane klienta (`docs/stan.md`, sekcja 6). Numeracja rozdziałów bez zmian,
> bo kod odwołuje się do niej (np. „22.23 ustaleń").

﻿# Rozpoznanie środowiska i korekty ustaleń API

**Data: 13.09.2026. Maszyna: `SERWER-DEMO`.**

Dokument zamyka **Fazę 0** planu z `PELNY-ZAPIS-USTALEN.md` §33 i koryguje ustalenia, które okazały się nieścisłe po zetknięciu z rzeczywistą instalacją i przykładami producenta.

Dokumenty źródłowe z fazy analitycznej leżą w `dokumenty-zrodlowe\` i **nie były zmieniane**. Tam, gdzie ten dokument mówi co innego, obowiązuje ten dokument.

---

## 1. Stan środowiska

### Maszyna i serwer bazy

| | |
|---|---|
| Host | `SERWER-DEMO`, Windows 11 Pro 10.0.26200 |
| Serwer SQL | `SERWER-DEMO\OPTIMA` — **lokalny**, uruchomiony |
| Wersja | Microsoft SQL Server 2022 (RTM-CU22) 16.0.4225.2 |
| Uwierzytelnianie | **logowanie Windows działa**, konto `SERWER-DEMO\admin` ma `sysadmin` |

Na tej samej instancji stoją bazy Comarch Optima (`CDN_*`) — nie nasze, nie ruszamy.

**Konsekwencja:** nie musimy odszyfrowywać hasła `sa` z `RPlatnik.ini`. Do odczytu wystarczy uwierzytelnianie NT.

### Trzy instalacje w `C:\Symfonia`

Stan na koniec dnia 13.09.2026, **po doinstalowaniu API** (patrz rozdział 3):

| Katalog | `RPlatnik10.exe` | `R2PApi10.dll` | Baza | Wersja |
|---|---|---|---|---|
| **`NowyR2Platnik.10_alfa`** | 10.1.0.33 | **10.1.0.33** ✅ | `R2P_platnik10_ALFA` | Profesjonalna |
| `NowyR2Platnik.10_beta` | 10.1.0.33 | brak | `R2P_platnik10_beta21` | Profesjonalna |
| `R2Platnik.10x_alfa_old` | 10.1.0.32 | 10.1.0.32 | `R2P_platnik10_ALFA` | Profesjonalna |

**Do pracy używać `NowyR2Platnik.10_alfa\BIN\`** — tam program, biblioteka i schemat bazy są w jednej wersji.

Pierwotnie obie nowe instalacje były bez API, a ich katalogi `BIN` identyczne bajt w bajt poza `.ini`, `.lic`, `.prt`.

**Korekta wobec `PELNY-ZAPIS-USTALEN.md` §32.** Zapis zakładał, że dwie instancje to dwie *wersje* programu albo produkcja i test. W rzeczywistości to **dwie firmy, każda z własną pełną instalacją i własną parą baz**. To nie jest jedna instalacja z dwiema bazami `dane_N`.

**Konsekwencja architektoniczna:** dwie firmy = **dwa `ConnectBin` na dwa różne katalogi `BIN`**, a nie jedno `ConnectBin` i dwa `ConnectDaneX`. Agent musi albo działać w dwóch instancjach, albo przełączać sesje między instalacjami.

### Bazy danych

```
r2p_platnik10_alfa_bin      USERS, FIRM, VERSION, VERS, USERLOG, licencja, prawa
r2p_platnik10_alfa_dane_1   PRACOWNK, HISTORIA, KART_URL, funkcje składowane
r2p_platnik10_beta21_bin
r2p_platnik10_beta21_dane_1
```

Podział `bin` + `dane_N` z §12 **potwierdzony w całości**.

`FIRM` w bazie `alfa_bin` zawiera **jeden wiersz**:

```
X_I = 1   Nazwa = ALFA   Katalog = DANE.1
```

czyli dla tej instalacji `ConnectDaneX(1)`.

### Wersja schematu bazy

Tabela `BIN.VERSION`:

| X_I | Wersja | Data | Aktywna |
|---|---|---|---|
| 8 | 10.10.**32** | 15.07.2026 14:14:54 | — |
| 9 | 10.10.**33** | 9.09.2026 09:00:04 | tak |

Wszystkie wiersze `BIN.VERS` mają `10.10.33`. Migracja schematu przeszła **13.09.2026 o 13:09:45**, przy pierwszym starcie nowej instalacji.

Rozjazd wersji, który stąd wynikał (baza `.33` wobec biblioteki `.32`), **został usunięty** — patrz rozdział 3.

**Wartość operacyjna tej tabeli:** `VERSION` dostaje nowy wiersz przy każdej migracji schematu, więc **zwykły `SELECT` mówi agentowi, jakiej wersji R2 używają stacje robocze** — bez sesji API, bez licencji i bez dotykania stacji. To podstawa wykrywania rozjazdu wersji opisanego w `ARCHITEKTURA-agenta.md` §6a.

---

## 2. Środowisko testowe z kopią bazy produkcyjnej

> **KOREKTA, 13.09.2026.** Pierwotnie uznałem to środowisko za produkcyjne na podstawie zawartości dziennika R2. Użytkownik potwierdził, że **`SERWER-DEMO` to maszyna testowa, a bazy to kopie produkcji**. Dziennik i ślady pracy kadr przyszły razem z kopią. **Zapis przez API jest dozwolony.** Poniższa analiza pozostaje w dokumencie, bo opisuje, skąd wzięły się dane i dlaczego wyglądają na żywe — ale wniosek „to produkcja" jest nieaktualny.

### Ślad, który o tym świadczy

Rozstrzygnięte na podstawie dziennika programu (`BIN.USERLOG`, **311 816 wpisów**):

```
2026-09-09 13:56  KADRY1  Begin Trans DaneX / Commit Trans DaneX   KARTA PRACY
2026-09-09 14:05  KADRY1  F6 / F7                                  KARTA URLOPOWA
2026-09-11 13:18  KADRY2    Logged On
2026-09-13 14:02  ADMIN    Logged On / Firm Change / Logged Off
```

Te ślady to **historia produkcji przeniesiona wraz z kopią bazy**, a nie dowód, że pracujemy na żywym systemie.

- **Odczyt SQL** — bez ograniczeń; `SELECT` niczego nie zmienia i nie trafia nawet do dziennika R2.
- **Zapis przez API** — **dozwolony**, to środowisko jest do tego przeznaczone (zapis wykonany i zweryfikowany, rozdział 14).
- Nadal obowiązuje: w bazie są prawdziwe dane osobowe. Bez PESEL, bez danych płacowych, nic nie wynosimy poza tę maszynę.

**Znalezisko przydatne projektowo:** R2 prowadzi własny dziennik operacji na poziomie tabel, z użytkownikiem i granicami transakcji. Zapisy agenta będą w nim widoczne i przypisane do jego konta — gotowy dowód rozliczalności przy RODO i kontroli PIP, bez budowania własnego mechanizmu.

---

## 3. Dlaczego nowe instalacje nie mają API — rozstrzygnięte

> **KOREKTA.** Pierwotnie napisałem, że API nie wchodzi w skład pakietu aktualizacyjnego, opierając się na braku ciągu „R2PApi" w `update.exe`. **Rozumowanie było błędne** — `update.exe` jest pobieraczem, nie paczką, więc nazw pobieranych plików nie musi mieć zaszytych. Poniżej wersja potwierdzona eksperymentem.

### Reguła

**`update.exe` aktualizuje to, co już jest w katalogu BIN. Nie dodaje komponentu, którego nigdy nie zainstalowano.**

Spójne ze wszystkimi obserwacjami:

| Instalacja | Miała API? | Efekt |
|---|---|---|
| `R2Platnik.10x_alfa_old` | tak | updater utrzymywał zgodność: `.29` → `.31` → `.32` (stare spychane na `._ll`) |
| `NowyR2Platnik.10_*` | nie | updater jej nie doda, choćby uruchamiać go wielokrotnie |

### Dowód eksperymentalny (13.09.2026, ~15:20)

Uruchomiono `update.exe` w `NowyR2Platnik.10_alfa\BIN`. **Żaden plik w całej instalacji nie został zmodyfikowany** — ostatni znacznik czasu to 13:13:58, sprzed uruchomienia. Brak `R2PApi10.dll`, brak logu, brak pobrań.

Przyczyna: instalacja **jest już w najnowszym wydaniu** `10.1.0.33` (9.09.2026), więc updater nie miał czego pobierać.

### Co mówi producent

Dokumentacja: *„Wymagana biblioteka API (plik `R2PApi.dll`) pobierana jest wraz z aktualizacją programu do podkatalogu BIN"*. Prawdziwe — ale **dotyczy instalacji, które ten komponent mają**. To nie jest sposób na jego dodanie.

API i przykłady to **osobny komponent instalatora**, dostępny przy wyborze INSTALACJI SZCZEGÓŁOWEJ i zaznaczeniu składnika *Demo API*.

### ROZWIĄZANIE — dwa różne updatery

> **To jest odpowiedź na pytanie „jak dodać API do istniejącej instalacji". Sprawdzone i działa.**

Na `https://pobierzprogram.symfonia.pl/R2Pcurrent` są **dwa różne pliki aktualizacyjne**:

| Plik | Rozmiar | Opis producenta | Zawiera API? |
|---|---|---|---|
| `NOWYupdate.exe` | 62,38 MB | aktualizacja **NOWEJ** Symfonia R2Płatnik | **NIE** |
| `update.exe` | 80,18 MB | aktualizacja Symfonia R2Płatnik | **TAK** |

Nowe instalacje mają w `BIN` domyślnie `NOWYupdate.exe` (65 406 704 B = 62,4 MB) — i dlatego jego uruchomienie niczego nie dało.

**Pobranie i uruchomienie `update.exe` (80 MB) w katalogu BIN nowej instalacji dostarczyło komplet:**

```
R2PApi10.dll       10.1.0.33   33 061 456 B   SHA256 A46B39A7B15A9753F573F94321F60945E2D843E0BE1C5BCE1111BA7E84B99790
R2PApi.dll          3.3.8.62   16 112 208 B   (API poprzedniej generacji)
KPApi.exe           1.0.0.0     8 778 264 B
SymfHRService.exe   1.0.0.4     3 286 552 B
SSAPI.exe           1.0.0.0        15 920 B
```

**Hipoteza, że nowa linia nie zawiera API, była błędna.** API jest dostępne dla 10.10.33 — trzeba tylko użyć właściwego updatera.

### Stan po wyrównaniu

```
RPlatnik10.exe   10.1.0.33
R2PApi10.dll     10.1.0.33     <- zgodne
schemat bazy     10.10.33      <- zgodne
```

Zweryfikowane pełnym przejściem diagnostycznym na nowej instalacji: logowanie, `ConnectDaneX(1)`, odnalezienie pracownika, 141 rekordów karty urlopowej. Identycznie jak na `.32`.

### Wniosek wdrożeniowy

- Do dodania API do istniejącej instalacji: **`update.exe` 80 MB**, nie `NOWYupdate.exe`.
- Dedykowaną instalację agenta na produkcji stawiać i aktualizować **tym samym updaterem**, żeby biblioteka nie wypadła przy aktualizacji.
- Kompletu integracyjnego (`KPApi`, `SSAPI`, `SymfHRService`) nie używamy, ale przychodzi w tej samej paczce.

### Istnieje jawny komunikat o niezgodności wersji

Dokumentacja producenta opisuje komunikat: *„Wersja programu nie odpowiada wersji biblioteki API — zaktualizuj program i bibliotekę R2PApi.dll"*.

Czyli przy rozjeździe biblioteka **odmawia z czytelnym powodem**, a nie psuje dane po cichu. Agent dostanie to przez `GetLastError`. To domyka strategię z rozdziału 6a architektury: wykrywanie z wyprzedzeniem jest optymalizacją, a nie filarem — ostateczną siatką bezpieczeństwa jest własna kontrola wersji w R2.

---

## 4. Biblioteka z raportu technicznego — zidentyfikowana

Po samoaktualizacji starej instalacji:

```
C:\Symfonia\R2Platnik.10x_alfa_old\BIN\R2PApi10.dll
FileVersion : 10.1.0.32
Rozmiar     : 33 052 240 B
SHA-256     : 740B9D8F03989D5604C2FA32197B643112A435AB4D85AC66EB013402CDFF0D53
```

Skrót zgadza się **co do bitu** z raportem technicznym (§7). Data nagłówka PE z raportu (15.07.2026 12:14:20 UTC = 14:14 lokalnego) odpowiada co do minuty wierszowi `10.10.32` w `BIN.VERSION`.

**Domniemana rozbieżność wersji z §111 nie istniała.** Raport opisywał wydanie 10.10.32 i był poprawny.

Pozostałe pliki API w tym samym katalogu:

| Plik | Wersja | Uwaga |
|---|---|---|
| `R2PApi10._ll` | 10.1.0.31 | poprzednia, dezaktywowana zmianą rozszerzenia |
| `R2PApi.dll` | 3.3.8.62 | API poprzedniej generacji (R2Płatnik 3.x), nieprzydatne |

---

## 5. Przykłady producenta — `C:\Symfonia\przykłady`

To jest poszukiwany katalog `DEMOS`, pod inną nazwą. Komplet w trzech językach, wszystkie projekty budowane jako **x86**:

| Ścieżka | Zawartość |
|---|---|
| `C#\RplatnikAPI\` | **główny przykład** — `MainForm`, `KartaUrlopowa`, `KartaPracy`, `DanePersonalne`, `RejestracjaCzasuPracy`, `EwidencjaKosztow` |
| `C#\RplatnikAPI\R2PAPI.cs` | deklaracje `DllImport` |
| `C#\Import_UI\Import_UI\R2PAPI.cs` | **najpełniejszy zestaw deklaracji C#** |
| `Delphi\RplatnikAPI\R2PHeader.pas` | **najpełniejszy zestaw sygnatur** — komplet z UI i kolejką komunikatów |
| `VB\RPlatnikAPI\` | ten sam przykład w VB |

Luka „znamy nazwy 359 funkcji, sygnatury kilkunastu" (§9) jest zamknięta dla wszystkiego, co dotyczy tego projektu.

---

## 6. Korekty ustaleń o API

Każdy punkt to błąd, który powstałby w kodzie pisanym według dotychczasowej dokumentacji.

### 6.1. `R_DNIA` to indeks listy, nie kod słownikowy

`KartaUrlopowa.cs:37` wypełnia listę rozwijaną przez `GetTableFieldStrings(h, "R_DNIA", buf)`, `:80` odczytuje `SelectedIndex = GetTableFieldAsInteger(h, "R_DNIA")`, `:124` zapisuje z powrotem `SelectedIndex`.

`R_DNIA` jest **zerowym indeksem w liście zwracanej przez API**. Pełne mapowanie odczytamy programowo jednym wywołaniem, razem z opisami — **pytanie otwarte nr 1 nie wymaga zgadywania**.

Potwierdza też, że `0` = pierwsza pozycja = urlop wypoczynkowy.

### 6.2. Daty przechodzą jako `double`

```
function GetTableFieldAsDate(Handle:integer; Name:PChar):Double;
function SetTableFieldAsDate(Handle:integer; Name:PChar; Value:Double):Boolean;
```

To format OLE Automation. W C# obowiązkowo `DateTime.FromOADate()` przy odczycie i `.ToOADate()` przy zapisie — producent robi dokładnie to (`R2PAPI.cs:103-125`). Przekazanie `DateTime` przez `DllImport` przepuści śmieci **bez zgłoszenia błędu**.

### 6.3. Pole `L` w karcie urlopowej — brak w specyfikacji

`L` jest typu `Boolean`. Gdy `L = true`, wiersz jest **wpisem limitu, a nie okresem urlopu**, a pole `Do` jest bez znaczenia (`KartaUrlopowa.cs:55,82`).

Odczyt karty musi te wiersze rozpoznawać, inaczej limity zostaną policzone jako urlopy. **Uzupełnia §15.**

### 6.4. Pisownia `Od`/`Do` nie ma znaczenia

Producent w tym samym pliku czyta `"OD"`/`"DO"` i zapisuje `"Od"`/`"Do"` na tej samej tabeli. Ostrzeżenie z dotychczasowego `CLAUDE.md` było nadmiarowe.

### 6.5. Do iteracji służy `TableDisableMasterDetail`

`MainForm.cs:115-133` — wzorzec producenta przy przechodzeniu po `PRACOWNICY`:

```csharp
TableDisableMasterDetail(PracT, true);
try   { /* TableFirst … TableNext … TableEOF */ }
finally { TableDisableMasterDetail(PracT, false); }
```

To jest **bezpieczna** dźwignia wydajności. `TableDisableEvents`, przed którym ostrzega §10, to zupełnie inna funkcja i zakaz jej użycia przy karcie urlopowej pozostaje w mocy.

### 6.6. `LConnectBin` — połączenie bez pliku `.ini`, z uwierzytelnianiem NT

```csharp
bool LConnectBin(byte DatabaseKind, string SQLDatabase, string SQLServer,
                 string SQLPassword, string SQLUser, bool SQLNTLog);
```

Agent nie musi czytać zaszyfrowanego hasła `sa` ani być przywiązany do katalogu konkretnej instalacji. Przy dwóch firmach na dwóch instalacjach znacząco upraszcza konfigurację.

### 6.7. `LoginUserAsProgram()` — logowanie bez poświadczeń

```csharp
int LoginUserAsProgram();
```

Dodatek producenta `KPApi.exe` **używa właśnie tej funkcji**, nie `LoginUser`. To jego własna odpowiedź na pytanie, jak uwierzytelnia się integracja techniczna. Do zweryfikowania w Fazie 1; jeśli zadziała, znika problem przechowywania hasła konta technicznego.

### 6.8. Inspektor tabel istnieje jako funkcja API

```pascal
function GetTableFieldCount(Handle:integer):integer;
function GetTableFieldDefs(Handle:integer; Struct:Pointer):integer;
```

Obecne wyłącznie w nagłówku Delphi, brak w przykładach C#. Narzędzie do Fazy 2.

### 6.9. Kolejka komunikatów — możliwe rozwiązanie problemu modalnych okien

```pascal
function  InitMessageQueue:Boolean;
function  GetMessage(P:Pointer):Boolean;
procedure SendMessage(P:Pointer);
procedure DestroyMessageQueue;
procedure AddonIsWorking;
```

ze strukturą `CONTROLMSG` (`Form`, `Name`: 36 znaków; `Msg`, `WParam`, `LParam`; `SParam`: 256 znaków).

`KPApi.exe` stoi na tym mechanizmie w wersji produkcyjnej. To pierwszy trop do sprawdzenia przy ryzyku z §8 — możliwe, że okno da się **przejąć**, zamiast czekać, aż ktoś kliknie. Osobny proces roboczy z watchdogiem pozostaje jako zabezpieczenie niezależnie od wyniku.

Ryzyko samo w sobie potwierdzone: w nagłówku są `MessageBox(Ide, Caption)`, `ConfirmBox(Caption, Param)`, `BeginProgress` / `GoProgress` / `EndProgress`.

### 6.10. Ładowanie DLL z właściwego katalogu

`DllImport` wymaga ścieżki jako stałej kompilacji — przykłady mają zaszytą ścieżkę maszyny deweloperskiej producenta. Żeby spełnić zasadę „ładuj DLL z `BIN` **aktualnej** instalacji" przy **dwóch** instalacjach, agent musi przed pierwszym wywołaniem ustawić katalog przez `SetDllDirectory` albo `NativeLibrary.SetDllImportResolver`.

### 6.11. Drobiazgi z nagłówka

- `MaxLenStr = 16384` (bufor `GetLastError`), `MaxLenMemo = 131072`
- `TableLocate(Handle, KeyFields, KeyValues: Variant, Ref)` — wiele pól przez `"Nazwisko;Imie"` + `object[]`
- Kilkanaście funkcji ma w nagłówku Delphi dopisek `//no` (m.in. `GetTableDBName`, `GetTableFieldName`, `GetTableFieldAsMemo`, `GetZatrIndParams`, `GetGodzUrlPozostalo`). **Interpretacja niepotwierdzona** — prawdopodobnie „niewspierane". Do potwierdzenia u producenta, zanim cokolwiek na nich zbudujemy; `GetGodzUrlPozostalo` byłaby wygodna przy saldach.

---

## 7. Dodatek KPApi

`C:\Symfonia\R2Platnik.10x_alfa_old\BIN\KPApi.exe`, 8,8 MB, 5.09.2024.

**To nie jest drugie API.** To dodatek producenta **zbudowany na** `R2PApi10.dll`, który importuje i eksportuje **karty pracy z/do Excela** (`KP` = karta pracy). Rejestruje się w ustawieniach programu, pojawia w menu Zbiory → Import danych, wymaga zainstalowanego Excela.

Weryfikacja binarna — importuje `R2PApi10.dll` i woła:

```
ConnectBin → LoginUserAsProgram → ConnectDaneX
OpenTable / OpenTableEx
TableInsert / TablePost / TableCancel / GetLastError
GetKartaPracy / GetKPDay / SetKPDay
InitMessageQueue / GetMessage / AddonIsWorking
AddMenu / AddButton / ShowForm
```

**Wartość dla projektu:** nie jako narzędzie — kart pracy nie ruszamy. Jako **dowód, że planowany wzorzec działa produkcyjnie**: osobny proces x86 łączący się przez tę samą bibliotekę i wykonujący masowy odczyt oraz zapis do ewidencji. Nie budujemy niczego egzotycznego.

W tym samym katalogu są jeszcze `SSAPI.exe` (15 KB) i `SymfHRService.exe` (3,3 MB, 1.0.0.4) — nieprzeanalizowane, nieobecne w nowych instalacjach.

---

## 8. Moduł WWW

- Obie nowe instalacje mają w `RPlatnik.ini` `Profesjonalny=1` — **warunek wstępny modułu www spełniony**.
- Na dysku **nie ma** `wR2platnik.exe` ani `R2router.exe` — moduł nie jest zainstalowany ani uruchomiony.
- `RPlatnik.lic` jest zaszyfrowany, listy modułów nie da się odczytać z pliku.

**Decyzja z §35 pkt 1 nadal otwarta.** Sprawdzenie możliwe wyłącznie w programie: **Pomoc → Licencja**.

---

## 9. Narzędzia

Zainstalowane 13.09.2026:

| Narzędzie | Wersja |
|---|---|
| .NET SDK | 10.0.401 |
| Git for Windows | 2.55.0.3 |

Zastane:

| Narzędzie | Lokalizacja / wersja |
|---|---|
| `sqlcmd` | `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn`, 16.0.4225.2 |
| Sterowniki ODBC | ODBC Driver 17 for SQL Server, SQL Server Native Client 11.0 |
| `csc.exe` | `C:\Windows\Microsoft.NET\Framework\v4.0.30319` (.NET Framework, build x86 bez instalacji) |

**Python nie jest potrzebny.** Do MSSQL wystarcza PowerShell z `System.Data.SqlClient` albo `sqlcmd`. `pyodbc` korzystałby z tego samego sterownika ODBC — zero zysku, jedna zależność więcej na serwerze produkcyjnym.

Pamiętać: agent i program diagnostyczny kompilowane jako **x86**, nie AnyCPU.

---

## 10. Stan pytań otwartych

Stan na 19.09.2026.

| # | Pytanie | Stan |
|---|---|---|
| 1 | Mapowanie `R_DNIA` | ✅ **ZAMKNIĘTE** — pełny słownik 0–8, patrz 12.3 |
| 2 | Klucz obcy `KART_URL` → `HISTORIA` | ✅ **ZAMKNIĘTE** — `X_I_HISTORII`, patrz 12.2 |
| 3 | Czy `X_IPrzelozony` i `Email_sluzb` wypełnione | ✅ **ZAMKNIĘTE** — nie są (2 e-maile na 358); nieistotne po zawężeniu zakresu (13) |
| 4 | Wielokrotne aktualne zatrudnienia | ✅ **ZAMKNIĘTE** — tak, 7 przypadków, patrz 12.4 |
| 5 | Zapis urlopu na żądanie | ✅ **ZAMKNIĘTE** — osobny `R_DNIA = 5` |
| 6 | Kalendarz R2 zamiast własnej tabeli świąt | ➖ **nieaktualne** — kalendarz robimy sami (13.2) |
| 7 | Minimalne uprawnienia konta integracyjnego | ⬜ otwarte — działa `ADMIN`; minimum nie wyznaczone |
| 8 | Czy API `.32` zapisuje do bazy `.33` | ✅ **ZAMKNIĘTE** — tak (14.6); rozjazd i tak usunięty (rozdział 3) |
| 9 | Skąd bierzemy roczny wymiar urlopu / czy saldo z SQL jest poprawne | ✅ **ZAMKNIĘTE 19.09.2026** — formuła potwierdzona ekranem programu (`23D 6G`), patrz 18.5 |
| 10 | Czy przy ostrzeżeniu R2 zwraca komunikat, czy pokazuje modalne okno | ✅ **ZAMKNIĘTE 19.09.2026** — przełom miesiąca i przekroczenie limitu wracają jako tekst, bez okna (20.10) |
| 11 | Czy sesja ubita przez watchdog blokuje stanowisko licencyjne | ⬜ otwarte — patrz `ARCHITEKTURA-agenta.md` §6 |
| 12 | Czy `Zatwierdzil_wniosek` da się nadpisać naszym przełożonym | ⬜ otwarte — R2 wpisuje tam login sesji API (14.3) |
| 13 | Co API zgłasza przy wyczerpanych licencjach | ⬜ otwarte — nie udało się wysycić puli (14.9) |

**Dziesięć z trzynastu zamkniętych.** Pytanie 10 rozstrzygnięte — watchdog jest zabezpieczeniem, nie warunkiem działania.

---

## 11. Następne kroki

### Wykonane 13.09.2026

- ✅ Faza 0 — rozpoznanie środowiska
- ✅ Faza 1 — połączenie przez API (`ConnectBin` → `LoginUser` → `ConnectDaneX(1)`)
- ✅ Faza 2 — inspekcja tabel, `GetTableSQL`, struktura `KART_URL`
- ✅ Faza 3 — odczyt karty urlopowej, pełny słownik `R_DNIA`
- ✅ Faza 4 — **pierwszy zapis urlopu przez API, zweryfikowany po SQL**
- ✅ Rekonesans bazy, reguła salda, zakres integracji
- ✅ Wyrównanie wersji: program, biblioteka i schemat bazy na `10.10.33`
- ✅ Architektura agenta i kontrakt API — dokumenty osobne

### Do zrobienia

1. **Test zapisu ponad saldo** — pytanie 10. Kowalski ma 25,75 dnia; wniosek na ~40 dni roboczych powinien wywołać ostrzeżenie. Rozstrzyga, czy `GetLastError` po udanym `TablePost` zwraca treść i czy pojawia się modalne okno. **Warunek wstępny budowy procesu roboczego.**
2. **Usunięcie wpisu testowego** `X_I = 12314` (`OPIS = TEST-API-20260913-1450`) przez API — `TableLocate` + `TableDelete`, nie po SQL (14.12).
3. **Potwierdzić z kadrami** regułę salda i źródło rocznego wymiaru — pytanie 9. Otworzyć w R2 kartę Siwca i sprawdzić, czy program pokazuje 25,75 dnia.
4. **Uzgodnić kontrakt API** z zespołem serwera — `KONTRAKT-API-agent-serwer.md`, rozdział 12 zawiera sześć sytuacji brzegowych do wspólnej decyzji.
5. **Uzgodnić pętlę agenta** — rozdział 15 ma status szkicu.
6. **Pomoc → Licencja** — czy moduł `www` producenta jest opłacony. Wciąż najważniejsza decyzja biznesowa, poprzedzająca budowę czegokolwiek.

---

## 12. Rekonesans bazy (13.09.2026, wyłącznie `SELECT`)

Wykonany na `r2p_platnik10_alfa_*`. Bazy Comarch Optima (`CDN_*`) na tej samej instancji **nie były odpytywane**.

### 12.1. Skala

Baza `..._dane_1`: **191 tabel, 71 funkcji skalarnych, 5 procedur, 0 widoków.**

| Tabela | ALFA | BETA |
|---|---|---|
| `PRACOWNK` | 353 | 86 |
| `HISTORIA` (`AktZatrudnienie=1`) | 358 | 86 |
| **`KART_URL`** | **8 573** | **2 515** |
| `UMPRACA` | 1 060 | 447 |
| `NKART_PR` | 10 927 | 2 994 |
| `OKRESHIS` | 70 | 14 |
| `POZ_BO` | 989 | 409 |
| `DZIAL` | 25 | 9 |

**Stan zatrudnienia w ALFA** — filtr ma znaczenie, warianty dają skrajnie różne wyniki:

| Filtr | Osób |
|---|---|
| `Skasowany=0` | 157 |
| `Skasowany=0 AND Zwolniony=0` | **45** |
| jw. + aktualne zatrudnienie (`AktZatrudnienie=1`) | **45** |

**Faktycznie zatrudnionych: 45.** Zgodne z założeniem ~40 z analizy kosztowej. Pominięcie `Zwolniony` zawyża wynik ponad trzykrotnie — w bazie siedzi cała historia kadrowa od 1992 roku.

Struktura tych 45 osób:

| Grupa | Osób | Umowa o pracę | Zlecenie | O dzieło |
|---|---|---|---|---|
| z `Identyfikator` | 36 | **36** | 11 | 0 |
| bez `Identyfikator` | 9 | 0 | 4 | 5 |

**Użytkownikami systemu urlopowego jest 36 osób** — te z umową o pracę. Pozostałych 9 to wyłącznie zlecenia i umowy o dzieło, którym urlop wypoczynkowy nie przysługuje. Potwierdza to niezależnie karta urlopowa: **dokładnie 36 osób ma wpisy za 2026**.

**Uwaga licencyjna:** 36 etatów + 11 zleceń przy etatach + 4 zlecenia + 5 umów o dzieło = **56 umów**. Cennik R2 liczy każdą umowę osobno, a próg „do 50 pracowników" jest właśnie przekroczony. Dotyczy to licencji R2Płatnika, nie naszego systemu — ale warto to zweryfikować przy rozmowie o module www.

`KALENDAR` i `LATA_KAL` są obecne w bazie firmy.

### 12.2. `KART_URL` ma 48 kolumn, nie 14

Specyfikacja (§15) opisywała 14. Pola pominięte, a istotne:

| Pole | Typ | Znaczenie |
|---|---|---|
| **`X_I_HISTORII`** | int | **klucz obcy do `HISTORIA.X_I`** |
| **`L`** | bit | wiersz **limitu**, nie okresu urlopu |
| `ODGODZ`, `DOGODZ` | datetime | urlop godzinowy |
| **`X_IZastepca`** | int | **osoba zastępująca — pole już istnieje w R2** |
| **`Warning`** | varchar(255) | **R2 sam przechowuje ostrzeżenie do wpisu** |
| `KOREKTA`, `KOR`, `KOR_POZOST`, `KOR_POZOST_GODZ` | | mechanizm korekt |
| `EKWIWALENT`, `KONTYNUACJA` | bit | |
| `R_DOD`, `X_L`, `X_IURL`, `WYJ` | | nierozpoznane |

Złączenie `KART_URL.X_I_HISTORII` → `HISTORIA.X_I` → `PRACOWNK.X_IPracownik` działa: 8 573 wiersze, 131 pracowników, **10 wierszy osieroconych**.

### 12.3. Słownik `R_DNIA` — odczytany z bazy

Przez `r2p_platnik10_alfa_bin.dbo.fn_Kod('KART_URL','R_DNIA', n)`:

| `R_DNIA` | Znaczenie | Użyć (L=0) |
|---|---|---|
| **0** | urlop wypoczynkowy | 7 212 |
| 1 | urlop okolicznościowy | 101 |
| 2 | urlop-opieka nad dzieckiem | 40 |
| 3 | urlop dodatkowy | — |
| 4 | nieobecność płatna | 1 |
| **5** | **urlop wyp. na żądanie** | 15 |
| 6 | ekwiwalent za url. wypocz. | 74 |
| 7 | urlop szkoleniowy | — |
| 8 | ekwiwalent za url. dod. | — |

Lista kończy się na `8` — zgodne z modelem „indeks listy" z 6.1. Wiersze limitu (`L=1`) występują tylko dla `R_DNIA` 0 (1 099) i 2 (31).

**Urlop na żądanie to osobny `R_DNIA = 5`**, nie flaga przy wypoczynkowym.

### 12.4. Wielozatrudnienie — potwierdzone

| Aktualnych zatrudnień | Pracowników |
|---|---|
| 1 | 343 |
| 2 | 6 |
| 3 | 1 |

Ostrzeżenie ze specyfikacji było zasadne. `HISTORIA.X_Default` istnieje i to jego używamy; przypadki wielokrotne zgłaszamy adminowi zamiast zgadywać.

### 12.5. Struktura przełożonych — niewypełniona

```
X_IPrzelozony w PRACOWNK        112 / 353
fn_Przelozony(X_I,1,1) nazwisko 143 / 358
fn_Przelozony(X_I,1,0) e-mail     2 / 358
HISTORIA.Email_sluzb              2 / 358
```

**Odwraca optymistyczny wniosek z §17.** Struktura jest wypełniona w ~40%, ale **e-maili służbowych są dwa na 358**. Importowanie struktury akceptacji z R2 nie ma podstaw w danych.

`X_IPrzelozony` istnieje w **obu** tabelach — `PRACOWNK` i `HISTORIA`.

### 12.6. Saldo — `POZOSTALO` nie jest wiarygodnym źródłem

§15 określał `POZOSTALO` jako „źródło salda dla aplikacji". Dane temu przeczą:

- wartości bywają **większe niż `LIMIT`** (zaległy urlop z lat poprzednich) i **ujemne** (`-54`, `-1`)
- kolumny dniowe i godzinowe się nie zgadzają: przy `POZOSTALO = 15` jest `POZOSTALO_GODZ = 12960`, co przy 480 (8 h × 60) daje 27 dni
- `LIMIT_GODZ` wygląda na **minuty**: `LIMIT = 9` ↔ `LIMIT_GODZ = 4320` = 9 × 8 × 60
- **wiersze limitu na 2026 istnieją tylko dla 43 zatrudnień ze 163**

Wykorzystany urlop liczymy przez `fn_KP_SumDPx`, źródła limitu nie zakładamy — patrz pytanie otwarte nr 9.

### 12.7. `STATUS` ma w całej bazie jedną wartość

Wszystkie 8 573 wiersze mają `STATUS = 0` („zatwierdzony"). Kadry **nigdy nie używały** stanu „planowany". Wpisywanie wniosków ze `STATUS = 1` (decyzja §35 pkt 2) wprowadziłoby do ewidencji stan, którego ci użytkownicy nie znają.

Podobnie nieużywane: `ANULUJ` (0 wystąpień), `X_IZastepca` (0), `Warning` (0). `KOREKTA` — 2 wiersze.

`Zatwierdzil_wniosek` jest wypełnione we **wszystkich** 8 573 wierszach — zawartości nie sprawdzano (dane osobowe).

### 12.8. W bazie nie ma ani jednego klucza obcego

`sys.foreign_keys` zwraca **0**. Integralność referencyjną pilnuje wyłącznie aplikacja — stąd 10 osieroconych wierszy w `KART_URL`.

To najmocniejszy argument za zasadą „zapis wyłącznie przez API": **baza sama niczego nie obroni**.

### 12.9. Drobne

`PRACOWNK` ma `Login`, `Password`, `X_FirstLogin` — konta pracownicze modułu WWW. Potwierdza §25.

---

## 13. Zawężenie zakresu integracji (decyzja klienta, 13.09.2026)

### 13.1. Zakres

**Z bazy R2 pobieramy wyłącznie dwie rzeczy:**

1. **pracowników** — identyfikacja i przypisanie do zatrudnienia
2. **ich urlopy** — zawartość `KART_URL`

**Do R2 zapisujemy wyłącznie jedną rzecz:**

3. **urlop do karty urlopowej** — przez API

**Wszystko pozostałe budujemy po swojej stronie:** strukturę akceptacji, adresy e-mail, kalendarz świąt i dni roboczych, działy, role, powiadomienia, reguły biznesowe.

### 13.2. Co to eliminuje

| Zagadnienie | Status |
|---|---|
| Import struktury przełożonych i `fn_Przelozony` | **poza zakresem** — dane i tak niewypełnione (12.5) |
| `Email_sluzb` z R2 | **poza zakresem** — adresy zbieramy sami |
| Kalendarz R2 (`KALENDAR`, `LATA_KAL`) | **poza zakresem** — własna tabela świąt |
| `DZIAL`, `UMPRACA`, `OKRESHIS`, `NKART_PR`, `POZ_BO` | **poza zakresem** |
| Pytania otwarte 3 i 6 | **nieaktualne** |

Synchronizacja sprowadza się do **dwóch zapytań**, a nie do odwzorowywania modelu danych R2. Agent przestaje być zależny od funkcji składowanych producenta, więc znika też ryzyko, że aktualizacja R2 zmieni ich zachowanie.

### 13.3. Co ta decyzja otwiera — pytanie nr 9

Jeśli „urlopy" rozumiemy jako **całą** zawartość `KART_URL`, to razem z okresami (`L=0`) dostajemy **wiersze limitu (`L=1`), czyli roczny wymiar urlopu** — bez dodatkowego kosztu, tą samą kwerendą, z tej samej tabeli.

Rekomendacja: **pobierać oba rodzaje wierszy.** Alternatywa to ręczne wprowadzanie wymiaru dla ~240 osób co styczeń i rozjazd z tym, co widzą kadry.

Zastrzeżenie z 12.6 pozostaje w mocy: wiersze limitu na 2026 są tylko dla 43 ze 163 zatrudnień, a kolumny dniowe i godzinowe bywają niespójne. Zanim oprzemy na nich saldo, trzeba to skonfrontować z kadrami.

### 13.4. Ryzyko do świadomego przyjęcia — patrz też 14.6

Licząc saldo samodzielnie, przejmujemy odpowiedzialność za zgodność z tym, co pokazuje R2. §5 mówi „przy rozbieżnościach R2 zawsze wygrywa" — przy własnym liczeniu rozbieżności będą się pojawiać i trzeba zdecydować, co wtedy widzi pracownik. Minimum: przy saldzie zawsze pokazujemy datę synchronizacji i traktujemy je jako informację pomocniczą, nie jako wiążące zobowiązanie.

---

## 14. Pierwszy zapis przez API — wykonany i zweryfikowany (13.09.2026)

Program `diag\Diag.cs` (.NET Framework, `csc /platform:x86`) wykonał pełną ścieżkę: `ConnectBin` → `LoginUser` → `ConnectDaneX(1)` → `PRACOWNICY` → `AKTUALNE ZATRUDNIENIE` → `KARTA URLOPOWA` → `TableInsert` → `TablePost`.

**Zapisano:** Jan Kowalski (`X_IHistorii = 207`), `R_DNIA = 0`, 2026-09-09 .. 2026-09-10, `STATUS = 0`, `OPIS = TEST-API-20260913-1450`. Nadany `X_I = 12314`.

### 14.1. Dwa błędy w przykładach producenta, które blokują integrację z C#

Oba dają objawy bez komunikatu błędu — `GetLastError` milczy. Bez ich znajomości integracja nie ruszy.

**Biblioteka jest UNICODE.** `PChar` w Delphi XE4+ to `PWideChar`. Deklaracje muszą mieć `CharSet = CharSet.Unicode`. W eksportach **nie ma wariantów `…W`**, więc dodatkowo `ExactSpelling = true`.

*Objaw przy ANSI:* `ConnectBin` zwraca `false`, a `GetLastError` oddaje **pojedynczą literę** (tekst UTF-16 urwany na pierwszym bajcie zerowym).

**Delphi `Boolean` ma 1 bajt.** `bool` w `DllImport` marshalluje się domyślnie jako 4-bajtowy `BOOL`. Delphi zwraca wynik w `AL`, starsze bajty `EAX` bywają śmieciem. Każda funkcja zwracająca `Boolean` musi mieć:

```csharp
[return: MarshalAs(UnmanagedType.U1)]
```

*Objaw bez tego:* `TableEOF` potrafi od razu zgłosić koniec tabeli — pętla nie wykonuje ani jednego obrotu, `TableRecordCount` pokazuje 140 rekordów, a przejście znajduje zero. Milcząco fałszywe wyniki, nie wyjątek.

**Przykłady producenta w C# zawierają oba te błędy.** Ich kod używa domyślnego ANSI i gołego `bool`. Traktować je jako źródło **sygnatur i kolejności wywołań**, nie atrybutów marshallingu.

### 14.2. `LoginUserAsProgram` — niedeterministyczne, nie opierać na nim agenta

> **KOREKTA.** Pierwotnie zapisałem „droga zamknięta dla samodzielnego procesu". To było przedwczesne — oparte na jednym uruchomieniu.

Zachowanie zależy od stanu maszyny:

| Sytuacja | Wynik |
|---|---|
| „zimny" start, brak wcześniejszego logowania | zwraca `0`, **bez komunikatu błędu** |
| istnieje kontekst wcześniejszego/równoległego logowania | **zwraca `USERS.X_I` — logowanie bez hasła** |

Zmierzone w teście 12 równoległych procesów (15.09–15.10): proces nr 1 musiał użyć `LoginUser(ADMIN)`, **procesy 2–12 zalogowały się przez `LoginUserAsProgram`**, nie znając hasła.

**Konsekwencja dla agenta:** funkcja zadziała na maszynie deweloperskiej, gdzie ktoś przed chwilą pracował w R2, i **zawiedzie po restarcie serwera**. Agent musi używać `LoginUser` z kontem technicznym. Pytanie otwarte nr 7 o minimalne uprawnienia pozostaje aktualne.

**Uwaga bezpieczeństwa:** to ścieżka logowania z pominięciem hasła, dostępna dla dowolnego procesu x86 na tej maszynie, gdy tylko istnieje aktywny kontekst. Warto o tym wiedzieć przy zabezpieczaniu serwera produkcyjnego.

### 14.3. Co R2 wypełnia samo — nie ustawiać tego ręcznie

Ustawiliśmy wyłącznie `R_DNIA`, `Od`, `Do`, `STATUS`, `OPIS`. Reszta pochodzi od programu:

| Pole | Wartość | Skąd |
|---|---|---|
| `X_I_HISTORII` | 207 | **z kontekstu master-detail** — nigdy nie ustawiamy |
| `IL_DNI` | **2** | wyliczone z kalendarza przez warstwę logiczną |
| `IL_GODZ` | **960** | 2 × 480 — potwierdza, że **480 minut = 1 dzień** |
| `Zatwierdzil_wniosek` | **`ADMIN`** | **login z sesji API**, nie nasz przełożony |
| `Data_zatw` | 2026-09-13 14:50:54 | znacznik czasu zapisu |
| `Warning` | `''` | puste, bez ostrzeżeń |

**`IL_DNI` policzone przez program to dowód, dla którego zasada „zapis wyłącznie przez API" nie jest przesadą.** Bezpośredni `INSERT` zostawiłby to pole puste i rozjechał ewidencję aż do naliczania listy płac.

**`Zatwierdzil_wniosek` wypełnia się automatycznie loginem sesji API.** To wyjaśnia, dlaczego w 12.7 pole było uzupełnione we wszystkich 8 573 wierszach. Plan z §15, żeby mapować tam przełożonego z naszej aplikacji, wymaga sprawdzenia — albo nadpisujemy tę wartość, albo identyfikator osoby akceptującej idzie do `OPIS`.

### 14.4. Karta pracy została zaktualizowana

`fn_KP_SumDPx(12, 30, 207, …)` za 2026: **11 dni przed zapisem, 13 po zapisie.** Suma `IL_DNI` z karty urlopowej również 13.

Warstwa logiczna propaguje urlop do karty pracy. Tego bezpośredni SQL by nie zrobił.

### 14.5. Reguła salda potwierdzona eksperymentem

Wiersz limitu na 2026 (`X_I = 11957`) **nie zmienił się po zapisie**: `POZOSTALO = -64`, `POZOSTALO_GODZ = 18600` — identycznie jak przed.

To potwierdza regułę z 12.6 w sposób kontrolowany:

> `POZOSTALO_GODZ` na wierszu `L=1` za rok Y to **stan na początek roku**, nie licznik bieżący.
>
> **Pozostało dziś = `POZOSTALO_GODZ`(Y) − suma `IL_GODZ` wpisów z roku Y**

Dla Siwca: 18 600 − 6 240 = **12 360 minut = 25,75 dnia** (przed zapisem było 27,75).

Kolumna dniowa `POZOSTALO` pozostaje niewiarygodna — patrz 12.6.

### 14.6. Pytanie otwarte nr 8 — zamknięte

**API w wersji 10.10.32 poprawnie zapisuje do bazy w schemacie 10.10.33.** Zapis, wyliczenia pochodne i aktualizacja karty pracy przebiegły bez błędu i bez ostrzeżenia.

Nie zwalnia to z doinstalowania komponentu API do instalacji `.33` przed wdrożeniem — jedno udane wywołanie nie jest dowodem zgodności całego interfejsu — ale ryzyko schodzi z „nieznane" na „niskie".

### 14.7. Modalne okno się nie pojawiło

Przy poprawnym zapisie proces nie zawisł. **To nie znaczy, że ryzyko z §8 nie istnieje** — nie sprowokowaliśmy jeszcze sytuacji ostrzegawczej, np. przekroczenia limitu. Osobny proces roboczy z watchdogiem pozostaje w projekcie.

Test do wykonania: zapis urlopu przekraczającego dostępne saldo i obserwacja, czy `TablePost` wraca, czy czeka na kliknięcie.

### 14.8. Porównanie binarne 10.10.32 → 10.10.33

| | 10.10.32 | 10.10.33 |
|---|---|---|
| Rozmiar | 33 052 240 B | 33 061 456 B (**+9 216 B**) |
| Data nagłówka PE | 15.07.2026 13:14:20 UTC | 09.09.2026 05:13:12 UTC |
| SHA-256 | `740B9D8F03989D5604C2FA32197B643112A435AB4D85AC66EB013402CDFF0D53` | `A46B39A7B15A9753F573F94321F60945E2D843E0BE1C5BCE1111BA7E84B99790` |
| **Eksportów** | **359** | **359** |

**Tablice eksportów identyczne** — zero dodanych, zero usuniętych, zero zmienionych nazw.

Rozkład różnicy na sekcje PE:

| Sekcja | Różnica | Znaczenie |
|---|---|---|
| `.text` | **+8 192 B** | kod maszynowy — jedyna realna zmiana |
| `.reloc` | +512 B | pochodna zmiany kodu |
| `.rsrc` | +512 B | zasoby |
| `.data` | **0** | **brak nowych struktur danych** |
| `.idata` | **0** | **brak nowych zależności** |
| `.edata` | **0** | **powierzchnia API nietknięta** |

**Wnioski:**

1. Deklaracje `DllImport` pozostają ważne — API się nie zmieniło.
2. Osiem kilobajtów kodu to poziom poprawki błędu, nie nowej funkcjonalności.
3. **Wyjaśnia, dlaczego `.32` poprawnie zapisało do bazy `.33`** (14.6) — między tymi wydaniami nie ma czego rozjeżdżać. Test nie był szczęśliwym trafem.

**Zastrzeżenie:** to jeden krok wersji. Wniosek „aktualizacje R2 są nieszkodliwe dla integracji" byłby nieuprawniony — przy przejściu na inną linię lub wersję 11 może być inaczej. Kontrola wersji w agencie zostaje.

**Obserwacja o naturze biblioteki:** sekcja `.rsrc` ma **14,5 MB na 33 MB pliku** — osadzony komplet formatek i zasobów programu. `R2PApi10.dll` to w praktyce cały R2Płatnik wystawiony przez API, a nie cienka warstwa integracyjna. **Stąd podsystem GUI i stąd ryzyko modalnego okna.**

### 14.9. Licencje — test wysycenia (13.09.2026, 15:09–15:12)

**Pytanie:** czy da się ustalić po SQL, przed otwarciem sesji API, czy jest wolne stanowisko? I co API zgłasza, gdy go nie ma?

**Metoda:** 12 równoległych procesów `Diag.exe --czekaj 60`, startowanych co 2,5 s, każdy pełny łańcuch `ConnectBin` → logowanie → `ConnectDaneX(1)` → otwarcie tabel → 60 s podtrzymania.

**Wynik: wszystkie 12 sesji zalogowało się i zadziałało. Żadnej odmowy.**

#### Czego w bazie NIE ma

| Trop | Stan przy 12 aktywnych sesjach |
|---|---|
| `LICSRVCONN` | **0** |
| `LICSERVER` | 0 — serwer licencji niekonfigurowany |
| `LOCKS`, `MODULES`, `SERVER` | 0 |
| procedury składowane w BIN | dziewięć, **żadna nie dotyczy licencji** |
| `LICENCE.Licence` | zaszyfrowany blob, 16 473 B |

**`LICSRVCONN` nie jest rejestrem stanowisk w tej konfiguracji.** Tabele `LICSERVER`/`LICSRVCONN` obsługują scentralizowane licencjonowanie; bez skonfigurowanego serwera licencji nikt do nich nie pisze. Na produkcji może być inaczej — **sprawdzić tam osobno**.

**Wniosek: licznika stanowisk nie da się odczytać z bazy.**

#### Co z tego wynika dla zasady „nie czytamy przez API"

Uzasadnienie licencyjne tej zasady **osłabło**. Dwanaście równoległych sesji API nie napotkało limitu.

Ale to **nie jest dowód, że sesje API są nielimitowane**. Wszystkie 12 logowało się na **to samo konto `ADMIN`**, a R2 może liczyć stanowiska per użytkownik, nie per połączenie. Rozstrzygnięcie wymagałoby równoległych sesji na **różnych kontach** (KADRY1, KADRY2, KADRY3, KADRY4) — nie mamy ich haseł.

Przesłanka, że sesje API/WWW są nielimitowane, ma niezależne wsparcie w dokumentacji producenta (§25: portal podłącza się „jako jeden użytkownik specjalny, nielimitowany") oraz w eksporcie `SetFreeWWWUser`.

**Argument wydajnościowy pozostaje w mocy i wystarcza.** Zasada „odczyt po SQL" zostaje bez zmian.

#### Czego nadal nie wiemy

**Co API robi przy wyczerpanych licencjach — nierozstrzygnięte.** Nie udało się wysycić puli. Kluczowe pytanie to nie treść komunikatu, tylko **czy wraca błąd, czy pojawia się modalne okno** — w drugim przypadku agent w usłudze zawiśnie.

#### Praktyczne przybliżenie, jeśli mimo wszystko chcemy liczyć

```sql
SELECT COUNT(*) AS ZajeteStanowiska
FROM   sys.dm_exec_sessions
WHERE  is_user_process = 1
  AND  program_name = N'R2płatnik'
  AND  DB_NAME(database_id) = N'<baza>_bin';
```

Wymaga `VIEW SERVER STATE`. **Uwaga na kodowanie** — polska litera w `program_name` psuje porównanie przy przekazywaniu zapytania przez powłokę; bezpieczniej `program_name LIKE 'R2p%atnik'`.

#### Rekomendacja projektowa

Nie budować zabezpieczenia na sprawdzaniu z góry. Nawet idealny licznik ma wyścig: między sprawdzeniem a logowaniem ktoś zajmie ostatnie stanowisko. **Agent musi obsłużyć nieudane logowanie niezależnie od licznika.**

Osobny proces roboczy z watchdogiem i twardym limitem czasu obsługuje jednym mechanizmem: brak licencji, modalne okno, zawieszenie biblioteki i wątki blokujące zakończenie procesu (14.10). Zapytanie o stanowiska zostaje ewentualnie jako tania optymalizacja, nie jako zabezpieczenie.

### 14.10. Zakończenie procesu — `ApiFinalize()`

> **KOREKTA, 13.09.2026 wieczorem.** Wcześniej zalecałem tu `TerminateProcess` jako obejście. **Znalazło się właściwe rozwiązanie** — nieudokumentowany eksport `ApiFinalize`. Poniżej wersja aktualna; opis objawów zachowany, bo tłumaczy mechanizm.

#### Rozwiązanie

```csharp
[DllImport(DLL, CharSet = CharSet.Unicode, ExactSpelling = true)]
public static extern void ApiFinalize();
[DllImport(DLL, CharSet = CharSet.Unicode, ExactSpelling = true)]
public static extern void FreeAllDataModules();

// w finally, po zamknieciu sesji:
CloseAllTables();
LogoffUser(user);
FreeAllDataModules();
ApiFinalize();          // <- brakujace ogniwo
// zwykly powrot z Main
```

**`ApiFinalize` nie występuje w nagłówku producenta `R2PHeader.pas`** ani w przykładach C#. Znaleziona przez przegląd tablicy 359 eksportów. Sąsiednie kandydatki: `FreeAllDataModules`, `FreeDataModule`, `CloseBinTables`, `CloseDaneXTables`.

#### Pomiar kontrolny

| Wariant | Kod wyjścia | Czas |
|---|---|---|
| `ApiFinalize` + normalny powrót | **0** | 3,6 s |
| `ApiFinalize` + normalny powrót (powtórka) | **0** | 3,3 s |
| **bez `ApiFinalize`** | **−1073741819** = `0xC0000005` | 6,8 s |

Powtarzalne. `ApiFinalize` wykonuje się w ~20 ms.

#### Dlaczego bez niej się psuje

`CloseAllTables` i `LogoffUser` zamykają **sesję, nie bibliotekę**. `R2PApi10.dll` to praktycznie cały R2Płatnik (14,5 MB zasobów, podsystem GUI) i przy ładowaniu uruchamia własne wątki, które po zakończeniu pracy dalej czekają w gotowości.

Gdy proces zaczyna się kończyć, Windows odłącza biblioteki (`DLL_PROCESS_DETACH`), sprzątając kod i pamięć DLL **spod jej własnych, wciąż żywych wątków**. Wątek sięga po coś, czego już nie ma → naruszenie ochrony pamięci.

Objawy różniły się w zależności od wariantu i wersji biblioteki: przy `.32` bez jawnego zakończenia proces **wisiał bezterminowo** (12 wątków w `Wait`, CPU ~0, `MainWindowHandle = 0`, czyli **nie** modalne okno); przy `.33` kończył się **naruszeniem dostępu**. Wspólna przyczyna: niezamknięta biblioteka.

#### Dlaczego to było krytyczne

Naruszenie ochrony pamięci **nadpisuje kod wyjścia procesu**. Nadzorca traci wtedy jedyny sygnał o wyniku zadania i nie odróżni sukcesu od katastrofy.

#### Zalecenie dla agenta — trzy warstwy

1. **`ApiFinalize()` w `finally`** — ścieżka właściwa, proces kończy się sam z prawidłowym kodem
2. **wynik zadania również jako JSON na stdout** — kod wyjścia zostaje kanałem pomocniczym, nie jedynym
3. **watchdog z twardym limitem czasu** — siatka bezpieczeństwa, gdyby `ApiFinalize` kiedyś zawiodło lub zawisło

`TerminateProcess(GetCurrentProcess(), kod)` pozostaje w `Diag.cs` jako awaryjne wyjście (domyślne; `--miekkie` wyłącza) — ale **nie jest już mechanizmem podstawowym**.

### 14.11. Connection stringi z API — gotowe źródło konfiguracji

`GetBinConnectionString` / `GetDaneXConnectionString` zwracają pełne parametry połączenia:

```
Provider=SQLOLEDB.1; User ID=sa; Password=***;
Initial Catalog=R2P_platnik10_ALFA_bin;
Data Source=SERWER-DEMO\OPTIMA;
Packet Size=24576; Use Encryption for Data=False
```

Trzy wnioski:

1. **Agent nie musi mieć nazw baz w konfiguracji** — może je odczytać z API przy pierwszym uruchomieniu (baza `_bin` i `_dane_N` z jednej sesji).
2. **API łączy się jako `sa`** hasłem z `RPlatnik.ini`. Nasze własne odczyty idą przez uwierzytelnianie Windows i tego nie potrzebują.
3. **`Use Encryption for Data=False`** — ruch do SQL bez szyfrowania. Nieistotne przy bazie na tym samym hoście, do rozważenia, gdyby serwer SQL był zdalny.

### 14.12. Sprzątanie

Wpis testowy `X_I = 12314` z `OPIS = TEST-API-20260913-1450` **pozostaje w bazie**. Usunięcie musi pójść przez API (`TableLocate` + `TableDelete`) albo ręcznie w programie — **nie przez SQL**, bo trzeba cofnąć także wpis w karcie pracy.

---

## 15. Model wykonania zadania zapisu — SZKIC

> **Status: szkic do ustalenia.** Sama pętla agenta, strategia ponawiania i odstępy między próbami **nie zostały uzgodnione** — wracamy do nich osobno. Poniżej zapis stanu rozmowy z 13.09.2026, żeby nie odtwarzać go od zera.
>
> Ustalone i wynikające z pomiarów są natomiast punkty 15.2–15.4 — one nie zależą od kształtu pętli.

### 15.1. Zarys (do uzgodnienia)

Podejście omawiane: nie sprawdzać licencji z góry, tylko **próbować się zalogować**; przy niepowodzeniu zapisać przyczynę i ponowić w kolejnej rundzie.

```
usluga (nadzorca)                 proces roboczy (x86, JEDNO zadanie)
─────────────────                 ──────────────────────────────────
pobierz zadanie z serwera WWW
  |
  '-> uruchom proces z limitem  -> ConnectBin
                                   LoginUser        <- tu moze zabraknac licencji
                                   ConnectDaneX
                                   sprawdz idempotencje (SQL)
                                   TableInsert / TablePost
                                   Environment.Exit(kod)
  |
  '-> zinterpretuj kod wyjscia:
        sukces                  -> oznacz wpisane + zapisz ostrzezenie z R2
        brak licencji           -> zostaw w kolejce, ponow
        bledne haslo            -> NIE ponawiaj, alarm do admina
        blad zapisu z R2        -> sekcja "Bledy" w panelu
        timeout / ubity         -> jak brak licencji, ale licz proby
```

**Do ustalenia:** odstępy między próbami, limit prób, moment eskalacji do panelu, czy nadzorca jest usługą Windows czy aplikacją w sesji użytkownika.

### 15.2. Osobny proces z twardym limitem czasu — wymóg, nie preferencja

> **UZUPEŁNIENIE 19.09.2026 — drugi, niezależny powód.** Błędne wywołanie API kończy proces **natychmiast** przez `AccessViolationException`, której w .NET 4+ **nie da się przechwycić** (potwierdzone na `GetGodzUrlPozostalo`: exit 139 mimo `try/catch`). Osobny proces chroni więc nie tylko przed zawieszeniem, ale i przed twardą awarią — patrz 18.3.

Schemat „zapisz porażkę i ponów" działa **wyłącznie wtedy, gdy nieudana próba wraca**. Jeśli biblioteka przy braku licencji lub przy ostrzeżeniu pokaże modalne okno, `LoginUser`/`TablePost` nigdy nie wróci — nie ma czego zapisać i nie ma kolejnej rundy, jest zawieszony proces.

Osobny proces z limitem czasu sprowadza zawieszenie do **zwykłego rodzaju porażki**, którą nadzorca obsługuje tak samo jak każdą inną. Ten sam mechanizm pokrywa naraz:

- brak licencji (zachowanie nieznane — 14.9)
- modalne okno (ryzyko §8, niesprawdzone)
- wątki blokujące zakończenie procesu (**zmierzone** — 14.10)

### 15.3. Idempotencja — obowiązkowa

Proces roboczy może zostać ubity przez watchdog **po udanym `TablePost`, a przed zgłoszeniem sukcesu**. Kolejna runda wpisałaby ten sam urlop drugi raz.

**Mechanizm:** identyfikator wniosku trafia do pola `OPIS` (varchar 50), a proces roboczy **przed zapisem sprawdza po SQL**, czy wiersz z tym identyfikatorem już istnieje w `KART_URL`. Sprawdzenie po SQL, nie przez API — tanie i bez sesji.

Tak właśnie oznaczono wpis testowy `TEST-API-20260913-1450`.

### 15.4. Rozróżnianie porażek przejściowych od trwałych

`LoginUser` zwraca `0` **zarówno** przy braku licencji, **jak i** przy błędnym haśle. Jedyna różnica to treść z `GetLastError` — trzeba ją zapisywać przy zadaniu, bo inaczej nie da się tych przypadków odróżnić.

Brak licencji ponawiamy. Błędne hasło ponawiane w kółko zaśmieci dziennik i może zablokować konto.

### 15.5. Sprawdzanie licencji z góry — odłożone

Nie daje gwarancji: między sprawdzeniem a logowaniem stan może się zmienić, a pełna obsługa porażki i tak musi istnieć. Ewentualnie jako optymalizacja ograniczająca liczbę nieudanych prób — nie jako zabezpieczenie. Zapytanie w 14.9.
---

## 16. Ustalenia z 19.09.2026

### 16.1. Urlop na przełomie miesięcy wymaga DWÓCH wpisów

**Zgłoszone przez użytkownika, potwierdzone w danych.**

```sql
SELECT COUNT(*) AS WpisowRazem,
       SUM(CASE WHEN MONTH(OD)<>MONTH(DO) OR YEAR(OD)<>YEAR(DO) THEN 1 ELSE 0 END) AS PrzezPrzelom
FROM KART_URL WHERE L=0;
```

| WpisowRazem | PrzezPrzelom |
|---|---|
| 7 444 | **0** |

**Ani jeden z 7 444 wpisów nie przekracza granicy miesiąca.** To nie jest zwyczaj kadr — to wymóg R2, wynikający z tego, że karta urlopowa wchodzi w rozliczenie płacowe **za konkretny miesiąc** (`NR_LISTY_P`, `DATA_WYPL`, `X_I_LISTY`).

**Konwencja podziału** — widoczna w danych:

| Przykład | `IL_DNI` |
|---|---|
| `2026-01-29` .. `2026-01-31` | 2 |
| `2026-06-29` .. `2026-06-30` | 2 |
| `2025-12-29` .. `2025-12-31` | 3 |
| `2025-09-16` .. `2025-09-30` | 11 |

Wpis **kończy się ostatnim dniem kalendarzowym miesiąca**, nawet gdy to sobota (`2026-01-31` to sobota, a `IL_DNI = 2`), i kolejny zaczyna się pierwszego. Dni wolne w zakresie są dopuszczalne — R2 i tak liczy tylko robocze.

### ROZSTRZYGNIĘCIE: **R2 rozbija zakres SAM** (test 19.09.2026)

Pytanie „czy agent ma dzielić, czy R2 zrobi to za nas" rozstrzygnięte eksperymentem. Przekazano **jeden** wpis `2026-10-29 .. 2026-11-03`:

```
[ok]   TablePost — ZAPISANO
       OSTRZEZENIE Z R2: Urlop na przełomie miesiąca. Wpis zostanie rozbity. (Ostrzeżenie)

X_I=12315  2026-10-29 .. 2026-10-31  IL_DNI=2
X_I=12316  2026-11-01 .. 2026-11-03  IL_DNI=2
```

**Agent przekazuje pełny zakres i nie dzieli niczego.** R2 sam tworzy dwa rekordy, ustala granicę na końcu miesiąca i wylicza `IL_DNI` osobno dla każdej części. Po zapisie w bazie nadal **0 z 7 446** wpisów przekracza przełom — niezmiennik jest pilnowany przez program, nie przez konwencję kadr.

> **KOREKTA.** Wcześniej zapisałem tu, że „agent musi rozbić zadanie na N wpisów". **To było błędne** — oparte na obserwacji danych, bez sprawdzenia zachowania API.

**Konsekwencje, które pozostają w mocy:**

- **jedno zadanie może dać więcej niż jeden `r2Id`** — kontrakt API musi to przewidzieć (`r2Id` jako tablica)
- **idempotencja nie może opierać się na „czy istnieje wiersz z tym znacznikiem"** — obie części dostają **ten sam `OPIS`**. Sprawdzenie musi porównywać pokryty zakres dat albo liczbę części, nie samą obecność znacznika.
- agent nie musi znać granic miesięcy ani konwencji podziału

### To pierwszy potwierdzony przypadek ostrzeżenia po UDANYM `TablePost`

Scenariusz opisywany w dokumentacji producenta (§10 zapisu ustaleń) jako teoretyczny — **zaobserwowany w praktyce**. `GetLastError` zwrócił treść mimo `TablePost = true`, a zapis był w pełni skuteczny.

**Dwa wnioski:**

1. Obsługa wyłącznie `ok` / `błąd` **zgubiłaby informację, że wpis został rozbity**. Status `okZOstrzezeniem` w kontrakcie API jest niezbędny, nie ozdobny.
2. **Ostrzeżenie przyszło jako tekst, nie jako modalne okno.** To częściowa odpowiedź na pytanie otwarte nr 10 — przynajmniej dla tej klasy ostrzeżeń biblioteka nie blokuje procesu. Nie zamyka to pytania dla ostrzeżeń innego rodzaju (np. przekroczenie salda), bo tam R2 może użyć `ConfirmBox` wymagającego decyzji.
Próba `LoginUser("ADMIN", …)`, gdy ADMIN jest już zalogowany w programie:

```
[BLAD] LoginUser: Błąd przy logowaniu użytkownika ADMIN
       (Użytkownik ADMIN jest zalogowany na SERWER-DEMO)
```

**Zwrócony jako czytelny błąd, nie jako modalne okno** — jedna z niewielu sytuacji, w których zachowanie biblioteki przy odmowie jest już znane.

**Konsekwencje dla agenta:**

1. **Konto techniczne musi być dedykowane.** Jeśli ktoś z kadr zaloguje się w R2 na to samo konto, agent przestanie działać — i to nie po cichu, tylko na każdym zadaniu.
2. To jest **błąd przejściowy**, nie trwały — ponawiać, ale z komunikatem dla admina, bo sam nie minie, dopóki człowiek się nie wyloguje.
3. `USERLOG` pokazuje pary `Logged On` / `Logged Off`; sesja bez pary to sesja otwarta. Daje to tani sposób zdiagnozowania blokady **po SQL, bez próby logowania**.
4. Otwarte pytanie: czy sesja ubita przez watchdog (bez `LogoffUser`) zostawia użytkownika „zalogowanego" i **blokuje samą siebie przy ponowieniu**. Jeśli tak, watchdog musi to uwzględnić.

### 16.3. Kalendarz R2 da się odczytać po SQL — `LATA_KAL`

`fn_KP_DP` zwraca `NULL` dla miesięcy, dla których nie ma jeszcze karty pracy — czyli **dla przyszłości jest bezużyteczna**, a wnioski urlopowe dotyczą właśnie przyszłości.

Rozwiązanie: tabela `LATA_KAL`, blob per miesiąc, **2 bajty na dzień, little-endian**:

| Wartość | Znaczenie |
|---|---|
| `480` | dzień roboczy (8 h × 60 min) |
| `0` | dzień wolny (sobota) |
| `9999` | niedziela / święto |

Dzień `n` to bajty `2n-1` i `2n`. Kalendarz pracownika wskazuje `HISTORIA.X_IKalendarz` → `KALENDAR.X_I`.

```sql
SELECT CAST(SUBSTRING(k.PAZDZIERNIK, 2*@n-1, 1) AS int)
     + 256*CAST(SUBSTRING(k.PAZDZIERNIK, 2*@n, 1) AS int)
FROM LATA_KAL k WHERE k.X_I_KALENDARZA = @kal AND k.ROK = @rok;
```

**Wartość praktyczna:** agent może policzyć dni robocze wniosku **przed zapisem**, zgodnie z kalendarzem tego konkretnego pracownika, i pokazać użytkownikowi tę samą liczbę, którą wyliczy potem R2. Bez tego liczba dni we wniosku i w ewidencji mogą się różnić.

**Uwaga:** to obejście przez odczyt struktury binarnej producenta. Format nie jest udokumentowany i może się zmienić przy aktualizacji R2 — przy rozbieżności źródłem prawdy pozostaje `IL_DNI` wyliczone przez R2 po zapisie.

---

## 17. `WYPL_NA_KOL_L`, edycja i usuwanie wpisów (19.09.2026)

> **Wszystkie operacje z tego rozdziału wykonano WYŁĄCZNIE przez API.**
> SQL posłużył jedynie do niezależnego odczytu kontrolnego — ani jednego `INSERT`, `UPDATE` czy `DELETE` na tabelach R2.
>
> | Operacja | Jak | Dowód |
> |---|---|---|
> | dodanie urlopu z odznaczoną flagą | `TableInsert` + `SetTableFieldAsBoolean` + `TablePost` | wpis `12317` powstał z `WYPL_NA_KOL_L = 0` |
> | późniejsze zaznaczenie flagi | `TableLocate` + `TableEdit` + `SetTableFieldAsBoolean` + `TablePost` | wpis `12316`: `0 → 1`, potem `1 → 0` |
> | usunięcie wpisu | `TableLocate` + `TableDelete` | `12314` i `12317` usunięte, karta pracy cofnięta |

### 17.1. Pole „uwzględnij na kolejnej liście wynagrodzeń"

**`KART_URL.WYPL_NA_KOL_L`** (bit). To ten znacznik z formatki R2.

**Domyślnie przy wstawianiu przez API ustawia się na `1`** — sprawdzone na trzech niezależnych wpisach, których tego pola nie dotykaliśmy (12314, 12315, 12317 przed jawnym ustawieniem).

### 17.2. Dodanie urlopu z odznaczoną flagą

`SetTableFieldAsBoolean` **przed** `TablePost`, w tym samym bloku co pozostałe pola:

```csharp
TableInsert(h);
SetTableFieldAsInteger(h, "R_DNIA", 0);
SetDate(h, "Od", od);
SetDate(h, "Do", do_);
SetTableFieldAsInteger(h, "STATUS", 0);
SetTableFieldAsBoolean(h, "WYPL_NA_KOL_L", false);   // <- odznaczone
TablePost(h);
```

Zweryfikowane: wpis `12317` (14–15.12.2026) powstał z `WYPL_NA_KOL_L = 0`.

**Uwaga na marshalling:** `Boolean` w Delphi ma 1 bajt także w **parametrze**, nie tylko w wartości zwracanej:

```csharp
[DllImport(DLL, CharSet = CharSet.Unicode, ExactSpelling = true)]
[return: MarshalAs(UnmanagedType.U1)]
public static extern bool SetTableFieldAsBoolean(int Handle, string Name,
                          [MarshalAs(UnmanagedType.U1)] bool Value);
```

### 17.3. Zmiana flagi na istniejącym wpisie

```csharp
TableLocate(h, "X_I", 12316, false);       // pozycjonowanie na rekordzie
TableEdit(h);                               // TableState = 2 (dsEdit)
SetTableFieldAsBoolean(h, "WYPL_NA_KOL_L", true);
TablePost(h);
GetLastError(buf);                          // takze po sukcesie
```

Zweryfikowane w obie strony na wpisie `12316`: `0 → 1`, potem `1 → 0`. Bez ostrzeżeń, `IL_DNI` niezmienione.

**`TableLocate` po `X_I` działa** — rekord musi być w bieżącym zbiorze detail, czyli wcześniej trzeba ustawić pozycję na właściwym pracowniku i zatrudnieniu.

Po nieudanym `TablePost` obowiązuje ta sama zasada co przy wstawianiu: **`TableCancel`**.

### 17.4. `TableDelete` działa i cofa skutki uboczne

```csharp
TableLocate(h, "X_I", 12317, false);
TableDelete(h);
GetLastError(buf);
```

Usunięto `12317` i `12314`. Bez ostrzeżeń.

**Najważniejsze:** po usunięciu `fn_KP_SumDPx(12, 30, 207, …)` spadło z 17 na **15 dni**, zgadzając się co do dnia z sumą `IL_GODZ` z karty. **R2 cofnął też wpisy w karcie pracy** — tak samo, jak je zakłada przy dodawaniu.

To domyka argumentację za zasadą „zapis wyłącznie przez API" **również dla usuwania**. `DELETE` po SQL zostawiłby kartę pracy z urlopem, którego już nie ma w ewidencji.

### 17.5. `KONTYNUACJA` ustawia R2, nie my

Przy rozbiciu urlopu na przełomie miesiąca **druga część dostaje `KONTYNUACJA = 1`** — ustawione przez program, nie przez nas. Potwierdzone także na parach historycznych (`12173`, `12149`).

Nie ustawiać tego pola ręcznie.

### 17.6. `X_F7DataCzas` nie zmienia się przy edycji pola
> **UWAGA — WNIOSEK PONIŻEJ JEST BŁĘDNY.** `X_F7DataCzas` to znacznik **utworzenia**, więc nie ma prawa zmieniać się przy edycji. Znacznik modyfikacji to `X_F6DataCzas` i **działa** — patrz 20.1.

Użytkownik odznaczył `WYPL_NA_KOL_L` na wpisie `12316` ręcznie w programie (dziennik: logowania o 10:13 i 10:16, po zapisie API o 10:09). **`X_F7DataCzas` pozostało `10:09:56`** — z chwili zapisu przez API.

Kolejny dowód, że **znaczniki czasu w R2 nie nadają się do wykrywania zmian**. Potwierdza wybór metody „lokalna migawka + porównanie" z `ARCHITEKTURA-agenta.md` §4.

### 17.7. Nowe przełączniki `Diag.exe`

| Przełącznik | Działanie |
|---|---|
| `--wyplata 0\|1` | ustawia `WYPL_NA_KOL_L` przy wstawianiu lub edycji |
| `--edytuj <X_I>` | pozycjonuje na wpisie, `TableEdit` → zmiana → `TablePost` |
| `--usun <X_I>` | pozycjonuje na wpisie i wykonuje `TableDelete` |
| `--od` / `--do` / `--rodzaj` / `--opis` | parametry wstawianego urlopu |

Przykłady:

```
Diag.exe --user ADMIN --pass X --zapisz --od 2026-10-29 --do 2026-11-03 --wyplata 0
Diag.exe --user ADMIN --pass X --edytuj 12316 --wyplata 1
Diag.exe --user ADMIN --pass X --usun 12317
```

---

## 18. Odczyt salda: SQL, nie API (19.09.2026)

### 18.1. Jak czytamy saldo

Wyłącznie zwykłymi `SELECT`-ami — bez sesji API i bez zajmowania stanowiska licencyjnego.

| Wielkość | Źródło |
|---|---|
| stan na początek roku | `KART_URL.POZOSTALO_GODZ` z wiersza `L = 1` |
| wykorzystane | `SUM(IL_GODZ)` z wierszy `L = 0` danego roku |
| kontrolnie | `fn_KP_SumDPx(12, 30, X_IHistorii, …)` |
| dni robocze wniosku | blob z `LATA_KAL` (16.3) |

Dla Siwca na 19.09.2026: `18 600 − 7 200 = 11 400` minut = **23,75 dnia**.

### 18.2. Próba weryfikacji przez API — `GetGodzUrlPozostalo` WYWALA PROCES

W nagłówku producenta funkcja ma dopisek `//no`. Sprawdzono, czy da się jej użyć jako testu krzyżowego dla formuły z SQL:

```csharp
[DllImport(DLL, CharSet = CharSet.Unicode, ExactSpelling = true)]
public static extern int GetGodzUrlPozostalo(int Rodzaj, int Year, StringBuilder Godz8Days);
```

Wynik:

```
System.AccessViolationException: Nastąpiła próba odczytu lub zapisu pamięci chronionej.
   w R2.GetGodzUrlPozostalo(Int32 Rodzaj, Int32 Year, StringBuilder Godz8Days)
Segmentation fault, EXIT = 139
```

**Dopisek `//no` w `R2PHeader.pas` oznacza „nie używać".** Wcześniej była to moja interpretacja (6.11) — teraz potwierdzona doświadczalnie. Dotyczy także `GetTableDBName`, `GetTableFieldName`, `GetTableFieldAsMemo`, `GetZatrIndParams`.

### 18.3. Wniosek architektoniczny — ważniejszy niż sama funkcja

**`AccessViolationException` nie daje się przechwycić** w .NET 4 i nowszych. Blok `try/catch` wokół wywołania **nie zadziałał** — proces zginął natychmiast, z kodem 139.

Dotąd uzasadnialiśmy osobny proces roboczy **zawieszeniem** (modalne okno, wątki blokujące zakończenie). Ten test pokazuje drugi, niezależny powód:

> **Błędne wywołanie API zabija proces natychmiast i żadna obsługa wyjątków tego nie powstrzyma.**

Gdyby agent wołał API we własnym procesie, jedno nietrafione wywołanie ubiłoby całą usługę razem z kolejką zadań i połączeniem do serwera. W osobnym procesie roboczym to zwykła porażka zadania: nadzorca widzi kod wyjścia `139`, oznacza zadanie i pracuje dalej.

**To przesuwa osobny proces z „zalecany" na „konieczny".**

### 18.4. Co pozostaje niezweryfikowane

Formuła salda jest sprawdzona na czterech przejściach rocznych i potwierdzona kontrolnym zapisem oraz usunięciem. **Nie została natomiast porównana z liczbą, którą R2 pokazuje na ekranie** — a droga przez API właśnie odpadła.

Pozostaje najprostsze: otworzyć w R2 kartę urlopową Siwca i sprawdzić, czy program pokazuje **23,75 dnia**. Jedno spojrzenie zamyka pytanie otwarte nr 9.

### 18.5. ROZSTRZYGNIĘTE — formuła potwierdzona ekranem programu

Użytkownik odczytał w R2 saldo Krzysztofa Siwca: **`23D 6G`**.

Formuła z SQL dała `11 400` minut:

```
11 400 ÷ 60        = 190 godzin
190 ÷ 8            = 23,75 dnia
23 dni + 0,75 × 8h = 23 dni 6 godzin      →  23D 6G   ✅
```

**Zgodność co do godziny. Pytanie otwarte nr 9 zamknięte.**

**Potwierdzone na większej próbce (19.09.2026).** Użytkownik sprawdził w programie także pozostałych pracowników wyliczonych tą samą formułą:

| Pracownik | `ZatrId` | Formuła SQL | Ekran R2 |
|---|---|---|---|
| KOWALSKI JAN | 207 | `23D 6G` | ✅ zgodne |
| NOWAK PIOTR | 968 | `26D 5G` | ✅ zgodne |
| ZIELIŃSKI PIOTR | 225 | `18D 0G` | ✅ zgodne |
| WIŚNIEWSKI MIROSŁAW | 209 | `11D 5G` | ✅ zgodne |
| SZYMAŃSKI MARIUSZ | 145 | `20D 0G` | ✅ zgodne |
| DĄBROWSKI MARIUSZ | 257 | `22D 0G` | ✅ zgodne |
| MAZUR MAREK | 131 | `18D 0G` | ✅ zgodne |
| JANKOWSKI MAREK | 24 | `21D 0G` | ✅ zgodne |
| WÓJCIK WOJCIECH | 1136 | `8D 0G` | ✅ zgodne |

Dziewięć osób, w tym przypadki z pełnymi dniami i z resztą godzinową, z saldami od 8 do 26 dni. **Formuła nie jest dopasowana do jednego przypadku.**

#### Zapytanie produkcyjne

```sql
DECLARE @zatr int = 207, @rok int = 2026;

WITH norma AS (            -- minut na dzien urlopu, z kalendarza pracownika
    SELECT k.DZIEN * 60 AS MinNaDzien
    FROM   HISTORIA h
    JOIN   KALENDAR k ON k.X_I = h.X_IKalendarz
    WHERE  h.X_I = @zatr
),
lim AS (                   -- stan na poczatek roku
    SELECT TOP 1 POZOSTALO_GODZ AS StartMin
    FROM   KART_URL
    WHERE  X_I_HISTORII = @zatr AND L = 1 AND YEAR(OD) = @rok
),
wyk AS (                   -- wykorzystane w tym roku
    SELECT ISNULL(SUM(IL_GODZ), 0) AS WykMin
    FROM   KART_URL
    WHERE  X_I_HISTORII = @zatr AND L = 0 AND YEAR(OD) = @rok
)
SELECT  lim.StartMin - wyk.WykMin                              AS PozostaloMin,
        (lim.StartMin - wyk.WykMin) / norma.MinNaDzien         AS Dni,
        ((lim.StartMin - wyk.WykMin) % norma.MinNaDzien) / 60  AS Godzin
FROM lim CROSS JOIN wyk CROSS JOIN norma;
```

#### Jednostki

| | |
|---|---|
| `POZOSTALO_GODZ`, `IL_GODZ` | **minuty**, mimo nazwy sugerującej godziny |
| dzień urlopu | `KALENDAR.DZIEN` godzin — **w tej bazie 8 h = 480 min we wszystkich kalendarzach** |
| prezentacja | `D` = minuty ÷ 480, `G` = (minuty mod 480) ÷ 60 |

**Nie wpisywać 480 na sztywno.** Pole `KALENDAR.DZIEN` istnieje właśnie po to i w innym wdrożeniu może mieć inną wartość. Uwaga na rozróżnienie: `DZIEN` to **norma dnia urlopowego**, a `DobowaNormaCzPr` to długość zmiany — w kalendarzu „Równoważny (12)" wynoszą odpowiednio 8 i 12 godzin, i dla urlopu liczy się **`DZIEN`**.

#### Dlaczego weryfikacja poszła przez ekran, a nie przez API

Droga przez `GetGodzUrlPozostalo` odpadła — funkcja wywala proces (18.2). Porównanie z tym, co program pokazuje użytkownikowi, okazało się **jedyną dostępną i zarazem najbardziej miarodajną** metodą: sprawdza dokładnie tę liczbę, którą zobaczą kadry.

---

## 19. Minimalny zestaw plików do pracy przez API (19.09.2026)

**Pytanie:** czy na serwerze potrzebna jest cała instalacja R2Płatnika, czy wystarczy biblioteka?

### 19.1. Wynik — cztery pliki

| Plik | Rozmiar | Rola |
|---|---|---|
| `R2PApi10.dll` | 33 061 456 B | biblioteka API |
| `RPlatnik.ini` | 1 393 B | ustawienia, w tym połączenie do SQL |
| `RPlatnik.lic` | 16 596 B | licencja |
| `RPlatnik.prt` | 16 473 B | licencja, druga część |

**Razem ~32 MB wobec 365 MB i 131 plików pełnego katalogu `BIN`.**

**`RPlatnik10.exe` nie jest potrzebny.** Zweryfikowano pełną ścieżkę z katalogu zawierającego wyłącznie te cztery pliki: `ConnectBin` → `LoginUser` → `ConnectDaneX(1)` → odczyt → `TableInsert` → `TablePost` → `TableDelete`. Zapis `12318` powstał z poprawnie wyliczonym `IL_DNI = 2`, czyli **warstwa logiczna działa w komplecie**.

### 19.2. Każdy z trzech plików konfiguracyjnych jest wymagany

Test przez usuwanie pojedynczych plików:

| Brakuje | Komunikat z `ConnectBin` |
|---|---|
| `RPlatnik.prt` | „Problem z odczytem licencji z plików …`RPlatnik.prt` lub …`RPlatnik.lic`" |
| `RPlatnik.lic` | jak wyżej |
| `RPlatnik.ini` | „Problem z odczytem ustawień z pliku …`RPlatnik.ini`" |

Komunikaty są czytelne i wracają jako **błąd, nie modalne okno** — agent może je przekazać do panelu.

Zależności biblioteki to **wyłącznie DLL-e systemowe Windows** (`kernel32`, `user32`, `ole32`, `oleaut32`, `wininet`, `crypt32`, …) plus `cnclrhostlibrary32.dll`, której w `BIN` nie ma i która najwyraźniej nie jest potrzebna do naszej ścieżki. `borlndmm.dll` i `midas.dll`, kopiowane przez producenta obok przykładów, **nie okazały się wymagane**.

### 19.3. Mimo to na produkcji zalecam pełną instalację

Cztery pliki wystarczą technicznie, ale operacyjnie pełna instalacja jest mniej krucha:

1. **Aktualizacje.** `update.exe` utrzymuje zgodność `R2PApi10.dll` z programem automatycznie (rozdział 3). W katalogu z czterema plikami nie ma czego aktualizować — bibliotekę trzeba podmieniać ręcznie przy każdym wydaniu R2.
2. **Diagnostyka.** Gdy coś nie działa, można otworzyć program na tej samej instalacji i zobaczyć to samo, co widzi agent — nieoceniona przy zgłoszeniach do producenta.
3. **Naturalne miejsce plików.** `ini`, `lic` i `prt` żyją tam, gdzie ich oczekuje instalator i procedura odtwarzania licencji.

**Minimalny zestaw traktować jako wiedzę o tym, co faktycznie jest potrzebne** — przydatną przy diagnozie, konteneryzacji albo gdy klient nie zgadza się na instalację programu na serwerze. Nie jako domyślny sposób wdrożenia.


### 19.4. Licencja jest przechowywana W BAZIE (potwierdzone 19.09.2026)

Hipoteza użytkownika — sprawdzona i **potwierdzona co do bajtu**.

```
BAZA  LICENCE.Licence                : fcf6bea245e29d01d63b867eb3de3aea0b497c93660f85c44510d324b3c10c55
PLIK  R2Platnik.10x_alfa_old\BIN\RPlatnik.prt : fcf6bea245e29d01d63b867eb3de3aea0b497c93660f85c44510d324b3c10c55
```

Blob `LICENCE.Licence` (16 473 B) jest **identyczny z plikiem `RPlatnik.prt`** starej instalacji. Licencja żyje w bazie `..._bin`, a `.prt` jest jej lokalną kopią.

Tabela `LICENCE` zawiera też pola jawne: `ClientNumber`, `LicenceNumber` (`R2P-102353`), `ClientNIP`, `TimeSign`.

**Ślepy trop, który po drodze odrzuciłem:** tabela `PRT` mimo nazwy nie ma z licencją nic wspólnego — trzyma pliki `.DBT` (memo). Rozmiar 16 473 B okazał się standardowym rozmiarem bloku w tym systemie, a nie poszlaką.

#### Czego nie wiemy

- **`RPlatnik.prt` nowej instalacji ma inny hash** (`949a2e6e…`) niż blob w bazie, przy identycznym rozmiarze. Nie wiadomo, w którą stronę idzie synchronizacja ani co decyduje o rozbieżności.
- **`RPlatnik.lic` (16 596 B) nie znalazł odpowiednika w bazie** i różni się między instalacjami. Jego rola pozostaje nieustalona.

#### Co z tego wynika praktycznie

1. **Kopia bazy niesie ze sobą licencję.** Dlatego odtworzone tu bazy produkcyjne działają bez żadnych zabiegów licencyjnych.
2. **Biblioteka API i tak wymaga lokalnych PLIKÓW.** `ConnectBin` bez `.prt` lub `.lic` zwraca „Problem z odczytem licencji" (19.2) — **nie sięga po licencję do bazy**. Minimalny zestaw z 19.1 pozostaje w mocy.
3. Przy wdrożeniu na serwer agent potrzebuje więc i dostępu do bazy, i lokalnej pary `.lic` + `.prt`.

#### Pytanie do producenta — nadal otwarte

Fakt, że licencja jest w bazie, **nie rozstrzyga, czy agent zajmuje stanowisko**. Do listy pytań dla partnera Symfonii:

> Czy agent integracyjny korzystający z `R2PApi10.dll` wymaga osobnej licencji stanowiskowej, czy mieści się w ramach istniejącej instalacji? Czy wymagana jest własna para plików `RPlatnik.lic` / `RPlatnik.prt`?

---

## 20. Weryfikacja pod pytania programisty tenanta (19.09.2026)

Pełen zestaw testów na kopii testowej: SQL + API. Wszystkie wpisy testowe usunięte, flaga `X_DBLog` przywrócona, saldo Siwca bez zmian (`23D 6G`).

### 20.1. KOREKTA: znaczniki czasu DZIAŁAJĄ — wcześniej odczytałem je odwrotnie

> **Wniosek z 17.6 („`X_F7DataCzas` nie zmienia się przy edycji → znaczniki nie nadają się do wykrywania zmian") był BŁĘDNY.** Patrzyłem na znacznik utworzenia, nie modyfikacji.

| Pole | Znaczenie |
|---|---|
| `X_F7` / `X_F7DataCzas` | **kto i kiedy UTWORZYŁ** rekord (`USERS.X_I`) |
| `X_F6` / `X_F6DataCzas` | **kto i kiedy ostatnio ZMODYFIKOWAŁ**; `NULL`, jeśli nigdy nieedytowany |

Test kontrolny: wpis utworzony przez API o 16:25:51 (`X_F6DataCzas = NULL`), po edycji dat przez API `X_F6DataCzas = 16:25:56`, `X_F7DataCzas` bez zmian. Wpisy kadr pokazują ten sam wzorzec (utworzony 13:13:23, zmodyfikowany 13:13:41).

**Ograniczenia:**

- pokrycie: `KART_URL` 8 571/8 575 ma znacznik utworzenia; `PRACOWNK` tylko 71/353, `HISTORIA` 181/1 079 — rekordy sprzed wprowadzenia mechanizmu mają `NULL`
- rozdzielczość: sekunda (`datetime`)
- **usunięcie nie zostawia śladu w wierszu** — rekord znika
- niezweryfikowane: czy znaczniki pochodzą z zegara serwera SQL, czy stacji roboczej (tu wszystko na jednej maszynie); przy rozjeździe zegarów kursor może gubić zmiany
- niezweryfikowane: czy naliczenie listy płac, uzupełniające `NR_LISTY_P`/`DATA_WYPL`, ustawia `X_F6DataCzas`

W bazie **brak** kolumn `rowversion`, triggerów, Change Tracking i CDC.

### 20.2. `USERLOG` — dziennik zmian z identyfikatorem rekordu, w tym USUNIĘĆ

```
F7  Table%KARTA URLOPOWA  FirmaIde%1  Ide%12313  Rec.owner%KADRY1 (2)  MasterIde%261
```

| Zdarzenie | Znaczenie | Liczba |
|---|---|---|
| `F7` | wstawienie | 64 912 |
| `F6` | edycja | 197 980 |
| **`F8`** | **usunięcie** | 18 865 |

`Ide` = `X_I` rekordu, `MasterIde` = rekord nadrzędny (dla karty urlopowej — zatrudnienie).

**Trzy ograniczenia dyskwalifikujące go jako jedyne źródło:**

1. **Logowanie jest per użytkownik** — `USERS.X_DBLog` (bit). Kadrowi mają `1`, ADMIN `0`. Wyłączenie flagi komukolwiek oznacza, że jego zmiany znikają z dziennika.
2. **Operacje przez API NIE są logowane w ogóle** — sprawdzone z włączoną flagą: wstawienie, edycja i dwa usunięcia przez API zostawiły wyłącznie „Logged On". Dotyczy też innych integracji (np. `KPApi.exe`).
3. Dziennik wspólny dla całej instalacji, ~310 tys. wierszy — wymaga filtrowania po `FirmaIde` i nazwie tabeli.

Wniosek: przydatna podpowiedź, **nie źródło prawdy** o usunięciach.

### 20.3. Pełne porównanie jest praktycznie darmowe

Pełny przegląd identyfikatorów z sumami SHA-256 (wszyscy pracownicy, zatrudnienia, wpisy urlopowe):

| Baza | Pracowników | Zatrudnień | Wpisów urlop. | Czas |
|---|---|---|---|---|
| ALFA | 353 | 1 079 | 8 575 | **17 ms** |
| BETA | 86 | 475 | 2 515 | < 1 ms |

**Rekomendacja:** connector trzyma lokalnie ostatnio wysłane sumy i **w każdym cyklu porównuje całość**. Wykrywa wstawienia, zmiany i usunięcia jednym mechanizmem, niezależnie od `X_DBLog`, operacji API, zegarów stacji i rekordów bez znaczników. Znaczniki `X_F6`/`X_F7` pozostają informacją pomocniczą.

### 20.4. Tożsamość: identyfikatory

| Kandydat | Ocena |
|---|---|
| `PRACOWNK.Identyfikator` (numer kadrowy) | **pusty w 222 z 353 kartotek** — nie nadaje się |
| `PRACOWNK.X_I` | stabilny w obrębie firmy, ale **5 osób ma po kilka kartotek** w tej samej firmie (ponowne zatrudnienie czasem tworzy nową) |
| `HISTORIA.X_I` (zatrudnienie) | klucz urlopu; **przy ponownym zatrudnieniu się zmienia** — 943 wpisy urlopowe wiszą pod zatrudnieniami historycznymi |
| `FIRM.X_I` | **obie firmy mają `1`** — unikalny tylko w instalacji; kluczem firmy powinien być `FIRM.NIP` |

**Między firmami brak wspólnego identyfikatora osoby.** 22 osoby występują w obu firmach; ich `X_I` nigdy się nie pokrywają. Jedyny łącznik: `PRACDANE.PESEL` (40 kartotek bez PESEL).

**PESEL a RODO:** skrót SHA-256 z PESEL **nie jest anonimizacją** — przestrzeń numerów PESEL jest mała i ustrukturyzowana (data urodzenia + numer + suma kontrolna), więc da się go odwrócić przeglądem zupełnym. To dalej dana osobowa. Rekomendacja: connector liczy **`osobaId = HMAC-SHA256(sekret_klienta, PESEL)`** — sekret nie opuszcza connectora, tenant nie dostaje ani PESEL, ani odwracalnego skrótu.

### 20.5. Etat — w `UMPRACA`, nie w `HISTORIA`

`HISTORIA.Etat` pusty u wszystkich 36 etatowców. Właściwe źródło: `UMPRACA.Etat_licznik / Etat_mianownik`, powiązanie `UMPRACA.X_IAktZatrudnienie = HISTORIA.X_I`, ostatnia umowa wg `Data_obowiaz_nowych_war` / `Data_zawarcia_umowy`.

Rozkład u 36 etatowców: `8/8` × 29, `1/1` × 1, `6/8` × 2, `4/8` × 3, `1/2` × 1. **Ta sama wartość bywa zapisana różnie** (`1/1` = `8/8`, `1/2` = `4/8`) — normalizować do ułamka dziesiętnego.

### 20.6. Kalendarz — 99,7% zgodności z R2

Dla wszystkich 719 wpisów wypoczynkowych i na żądanie z lat 2025–2026: dni robocze policzone z `LATA_KAL` zgodne z `IL_DNI` wyliczonym przez R2 w **717 przypadkach**. Dwie niezgodności — harmonogramy indywidualne (`GRAFIKPDZIEN` 14 390 wierszy, `KALENDAR.Indywidualne = 1` dla kalendarzy 2 i 33).

**Godzin NIE da się policzyć jako dni × 8.** Minuty na dzień urlopu wg danych:

| Etat | Min/dzień | Wpisów |
|---|---|---|
| 1 | 480 | 477 |
| ½ | 240 | 115 |
| 1 | 720 | 48 |
| ¾ | 360 | 46 |
| 1 | 240, 600, 560, … | kilkanaście |

Zależą od etatu i od harmonogramu (systemy równoważne, doby 12 h). Liczba dni i godzin **rozstrzygająca** — wyłącznie ta zwrócona przez R2 po zapisie.

### 20.7. KOREKTA formuły salda — filtr po rodzaju urlopu

Dotychczasowa formuła brała `TOP 1` wiersz limitu i odejmowała **wszystkie** wpisy roku. Dwa błędy:

- wiersze limitu istnieją także dla `R_DNIA = 2` (urlop opiekuńczy) — Samojlik ma w 2026 dwa limity
- od puli wypoczynkowego odejmowany był urlop okolicznościowy (Lewandowski, `R_DNIA = 1`)

**Formuła poprawiona:**

```sql
-- limit:        L = 1 AND R_DNIA = 0
-- wykorzystane: L = 0 AND R_DNIA IN (0, 5)   -- wypoczynkowy + na żądanie
```

Dziewięć zweryfikowanych osób nie miało innych rodzajów urlopu — ich wyniki bez zmian.

### 20.8. ~~NIEZWERYFIKOWANE~~ ZAMKNIĘTE: saldo przy niepełnym etacie — patrz 22.12

**Wszystkie 9 osób zweryfikowanych ekranem programu było na pełnym etacie.** Przy niepełnym etacie nie wiadomo, czy R2 pokazuje dni jako minuty ÷ 480, czy minuty ÷ (480 × etat).

Wskazówka z danych: Samojlik (½ etatu) ma `LIMIT = 13`, `LIMIT_GODZ = 6240` = 13 × 480 — **limit w dniach jest wyrażony w dniach 8-godzinnych**, podczas gdy jeden dzień jej urlopu zużywa 240 minut.

Do sprawdzenia w programie — dwie osoby rozstrzygają wszystko:

| Osoba | Etat | Minuty | Jeśli ÷ 480 | Jeśli ÷ (480 × etat) |
|---|---|---|---|---|
| KAMIŃSKI KAMIL | 4/8 | 7 440 | `15D 4G` | `31D 0G` |
| LEWANDOWSKI TOMASZ | 6/8 | 6 540 (po korekcie) | `13D 5G` | `18D 1G` |

U Lewandowskiego dodatkowo: gdyby program pokazywał `12D 1G`, korekta z 20.7 byłaby błędna.

### 20.9. Zachowanie R2 przy zapisie — trzy nowe fakty

**Kolizja — R2 odrzuca:**

```
[BLAD] TablePost: Urlop pokrywa się z wcześniejszymi okresami
```

Nic nie zapisane, `TableCancel` przywraca stan. Działa jako **naturalna ochrona przed podwójnym zapisem** tego samego wniosku.

**Przekroczenie salda — R2 ZAPISUJE i tylko ostrzega:**

```
[ok] TablePost — ZAPISANO
OSTRZEZENIE: KOWALSKI JAN: limit urlopu wypoczynkowego za rok 2026 został przekroczony (Ostrzeżenie)
```

**Blokadę przekroczenia musi zrobić tenant.** Okno modalne się nie pojawiło.

**Zmiana dat istniejącego wpisu — działa:** `TableEdit` → `SetDate(Do)` → `TablePost`; R2 przelicza `IL_DNI` (2 → 4) i `IL_GODZ` (960 → 1920).

### 20.10. PYTANIE OTWARTE NR 10 — ZAMKNIĘTE

Obie sprowokowane klasy ostrzeżeń — **rozbicie na przełomie miesiąca** i **przekroczenie limitu** — wróciły jako **tekst przez `GetLastError`**, bez okna, proces zakończył się normalnie. Watchdog pozostaje zabezpieczeniem przed zawieszeniem i przed nieprzechwytywalnym `AccessViolationException` (18.3), ale **nie jest już warunkiem działania**.

### 20.11. Komunikaty R2 — brak kodów, tylko tekst

| Sytuacja | Komunikat | Skutek |
|---|---|---|
| kolizja dat | „Urlop pokrywa się z wcześniejszymi okresami" | **nie zapisano** |
| przekroczenie limitu | „{NAZWISKO IMIĘ}: limit urlopu wypoczynkowego za rok {R} został przekroczony (Ostrzeżenie)" | zapisano |
| przełom miesiąca | „Urlop na przełomie miesiąca. Wpis zostanie rozbity. (Ostrzeżenie)" | zapisano, 2 rekordy |
| konto zajęte | „Błąd przy logowaniu użytkownika X (Użytkownik X jest zalogowany na HOST)" | przejściowy |
| brak plików licencji | „Problem z odczytem licencji z plików …" | konfiguracja |
| brak `.ini` | „Problem z odczytem ustawień z pliku …" | konfiguracja |

Kody błędów nadaje connector na podstawie treści; surowy tekst przekazywany dalej.

### 20.12. Wydajność i skala

| | ALFA | BETA |
|---|---|---|
| zatrudnionych | 45 | **53** |
| kartotek razem | 353 | 86 |
| wpisów urlopowych razem | 8 575 | 2 515 |
| urlopów w 2025 / 2026 | 417 / 314 | 481 / 386 |

> **KOREKTA:** wcześniej podawałem BETA jako 86 osób — to liczba kartotek; zatrudnionych jest 53.

Sesja API z pojedynczym zapisem (połączenie + logowanie + zapis + `ApiFinalize`): **~1,7–2,2 s**. Pełny odczyt SQL z sumami kontrolnymi: **17 ms**.

---

## 21. Nieobecności, uprawnienia konta technicznego (19.09.2026)

### 21.1. Zmiana zakresu — decyzja klienta

**Odczyt obejmuje teraz WSZYSTKIE nieobecności**, nie tylko urlopy. Zapis bez zmian: wyłącznie karta urlopowa.

Rodzaje pochodzące z ZUS (zwolnienia lekarskie, zasiłki) są w aplikacji **tylko do odczytu** — pracownik ich nie wnioskuje, trzeba je wykluczyć z formularza wniosku.

### 21.2. Trzy tabele, trzy słowniki

Słowniki odczytane funkcją `<BIN>.fn_Kod(tabela, pole, wartość)`. Liczby wystąpień: ALFA.

**A. `KART_URL.R_DNIA` — karta urlopowa (zapis + odczyt)**

| Kod | Rodzaj | Wpisów | Uwagi |
|---|---|---|---|
| 0 | urlop wypoczynkowy | 7 212 | wniosek, etap 1 |
| 1 | urlop okolicznościowy | 101 | |
| 2 | urlop — opieka nad dzieckiem (art. 188 KP) | 40 | osobny limit roczny (`L=1`, `R_DNIA=2`) |
| 3 | urlop dodatkowy | 0 | |
| 4 | nieobecność płatna | 1 | |
| 5 | urlop wypoczynkowy na żądanie | 15 | wniosek, etap 1; zużywa pulę wypoczynkowego |
| 6 | ekwiwalent za urlop wypoczynkowy | 74 | **nie jest nieobecnością** — rozliczenie płacowe |
| 7 | urlop szkoleniowy | 0 | |
| 8 | ekwiwalent za urlop dodatkowy | 0 | jak 6 |

**B. `OKRESHIS.Rodzaj_nieobecnosci` — nieobecności kadrowe (tylko odczyt)**

| Kod | Rodzaj | Wpisów |
|---|---|---|
| 0 | urlop bezpłatny | 45 |
| 1 | urlop wychowawczy | 4 |
| 2 | służba wojskowa | 2 |
| 3 | nieobecność usprawiedliwiona | 7 |
| 4 | nieobecność nieusprawiedliwiona | 12 |

Kolumny: `X_IHistoria`, `X_IPracownik`, `Od`, `Do`, `IlDni`, `IL_GODZ`, `Status`, `Anuluj`, znaczniki `X_F6`/`X_F7`. Uwaga na nazwy: `X_IHistoria`, a nie `X_I_HISTORII` jak w `KART_URL`. Zapisu przez API **nie testowano** — urlop bezpłatny i wychowawczy są w praktyce wnioskowane przez pracownika, ale ich zapis to osobna, nieprzetestowana ścieżka.

**C. `KART_ZAS.R_NIEOB` — karta zasiłkowa, 🏥 WSZYSTKO Z ZUS (tylko odczyt)**

| Kod | Rodzaj | Wpisów |
|---|---|---|
| 0 | chorobowe | 718 |
| 1 | chorobowe 100% | 1 |
| 2 | chorobowe wypadkowe | 39 |
| 3 | świadczenie rehabilitacyjne | 38 |
| 4 | świadczenie rehabilitacyjne wypadkowe | 0 |
| 5 | opieka nad dzieckiem (zasiłek opiekuńczy) | 24 |
| 6 | opieka — inne | 5 |
| 7 | macierzyński | 7 |
| 8 | chorobowe szpitalne | 45 |
| 9 | niepłatne — inne | 0 |
| 10 | świadczenie rehabilitacyjne 100% | 0 |
| 11 | ojcowski | 7 |
| 12 | niepłatne z tytułu nienabycia prawa do zasiłku | 0 |
| 13 | macierzyński 80% | 0 |
| 14 | rodzicielski | 0 |
| 15 | rodzicielski 80% | 0 |
| 16 | dodatkowy macierzyński | 0 |
| 17 | dodatkowy macierzyński 80% | 0 |
| 18 | zagraniczne | 0 |

Kolumny istotne: `X_I_HISTORII`, `OKRES_OD`, `OKRES_DO`, `R_NIEOB`, `IL_DNI_ZAS`, `E_ZLA`. **516 z 884** wpisów przyszło z e-ZLA.

**Dane szczególnie wrażliwe w tej tabeli — NIGDY nie przesyłać:** `KOD_CHOR1`, `KOD_CHOR2` (kody chorób), `SERIA`, `NR` (numer zwolnienia), wszystkie kwoty (`ZAS_*`, `PODST_*`, `WYR_MAC`), `Uwagi`. Connector wysyła **wyłącznie rodzaj i daty**.

Rekomendacja dla tenanta: współpracownicy i przełożeni widzą w kalendarzu zespołu tylko „nieobecny", konkretny rodzaj — wyłącznie kadry. Fakt przebywania na zwolnieniu lekarskim to dana o zdrowiu (art. 9 RODO).

**Kody nakładają się między tabelami** (0 = wypoczynkowy / bezpłatny / chorobowe) — w wymianie zawsze para `źródło + kod`.

**Poza zakresem:** `EZLA` — surowe e-zwolnienia z ZUS (kody A–E, wskazania lekarskie, data urodzenia osoby pod opieką); przetworzony wpis jest w `KART_ZAS`. `KART_SWIAD` — karta świadczeń płacowych, nie nieobecności.

### 21.3. Czy konto agenta musi mieć włączone logowanie (`X_DBLog`)? **NIE.**

Dwa niezależne dowody:

1. Wszystkie zapisy, edycje i usunięcia z 13 i 19.09.2026 wykonano na koncie ADMIN z `X_DBLog = 0` — **działały bez zarzutu**.
2. Z włączoną flagą operacje API **i tak nie trafiły do `USERLOG`** (20.2). Flaga nie ma na API żadnego wpływu.

Synchronizacja opiera się na porównaniu sum kontrolnych (20.3), nie na dzienniku — więc logowanie nie jest też potrzebne pośrednio.

### 21.4. Model uprawnień R2

- **`RIGHTS`** — grupy uprawnień: Administratorzy, Płace, Płace — ograniczony dostęp, Kadry — definiowanie, Kadry — wprowadzanie, Zarząd — definiowanie, Zarząd — przeglądanie, Serwery R2P, **Użytkownicy WWW**, Administratorzy SQL
- **`USERS.X_IRights`** — przypisanie użytkownika do grupy
- **`TBRIGHTS`** — prawa grupy do każdej tabeli aplikacji (1 025 wierszy):

| Kolumna | Znaczenie |
|---|---|
| `F7` | dodawanie |
| `F6` | poprawianie |
| `F8` | usuwanie |
| `F6A`, `F8A` | *prawdopodobnie* poprawianie/usuwanie rekordów utworzonych przez innych — **niezweryfikowane** |
| `FILTR` | ograniczenie widocznych rekordów (warunek) |
| `Z`, `W`, `F` | *prawdopodobnie* dostęp/widoczność — **niezweryfikowane** |

Kody `F6`/`F7`/`F8` są te same co zdarzenia w `USERLOG`.

### 21.5. Dedykowane konto agenta — wymagania

Konto **musi** być dedykowane: R2 nie pozwala zalogować tego samego użytkownika dwa razy (16.2), więc człowiek na tym koncie blokuje agenta.

**Potrzebne uprawnienia** (zakres zapisu: tylko karta urlopowa):

| Tabela aplikacji | Odczyt | `F7` | `F6` | `F8` | Po co |
|---|---|---|---|---|---|
| `FIRMY` | ✓ | | | | `ConnectDaneX` |
| `PRACOWNICY` | ✓ | | | | pozycjonowanie przed zapisem |
| `AKTUALNE ZATRUDNIENIE` | ✓ | | | | pozycjonowanie — karta urlopowa jest jego detailem |
| **`KARTA URLOPOWA`** | ✓ | **✓** | **✓** | **✓** | zapis; edycja (flaga wypłaty, zmiana dat); usunięcie (anulowanie) |

**Pułapka:** mimo że cały odczyt robimy po SQL, **ścieżka zapisu przez API otwiera `PRACOWNICY` i `AKTUALNE ZATRUDNIENIE`**. Bez prawa odczytu do nich zapis nie ruszy. Dodatkowo `PRACOWNICY` przez API pokazuje pracowników **zgodnie z prawami zalogowanego użytkownika** (komentarz w przykładzie producenta) — `FILTR` dla grupy agenta musi być pusty, inaczej część pracowników będzie dla agenta niewidoczna i zapis urlopu im się nie uda.

**Do rozważenia:** jeśli `F6A`/`F8A` rzeczywiście oznaczają edycję cudzych rekordów, ustawienie ich na `0` ograniczyłoby agenta do modyfikowania wyłącznie wpisów, które sam utworzył — nie ruszyłby urlopu wpisanego ręcznie przez kadry. Do sprawdzenia testem.

**Nazwa konta ma znaczenie:** R2 wpisuje ją w `Zatwierdzil_wniosek` każdego zapisanego urlopu (14.3), więc kadry będą ją widzieć na karcie. Proponowana nazwa: np. `WNIOSKI_DEMO`.

Punkt wyjścia do utworzenia grupy: skopiować „Zarząd — przeglądanie" (odczyt, bez zapisu) i nadać `F7`/`F6`/`F8` na `KARTA URLOPOWA`.

### 21.6. Do zrobienia

Po utworzeniu konta w R2: test logowania, zapisu, edycji i usunięcia oraz próba zapisu przez API do tabeli, do której konto nie ma praw — żeby zobaczyć, jaki komunikat zwraca R2 przy braku uprawnień. To zamknie pytanie otwarte nr 7.

---

## 22. Drugi mail programisty tenanta — weryfikacja (19.09.2026)

### 22.1. Licencja: API ją pobiera — POTWIERDZONE PRZEZ UŻYTKOWNIKA
> **NIEAKTUALNE — patrz 22.14.** Test przy obu zajętych stanowiskach: API zalogowało się, zapisało i usunęło urlop, nikogo nie wylogowując.

Użytkownik sprawdził i potwierdził: **sesja API zajmuje licencję R2.** Zamyka to hipotezę z 14.9 i pytanie do producenta w tej części.

Co wiemy o **czasie zajęcia** (z pomiarów):

| | |
|---|---|
| sesja przy zapisie jednego urlopu | 1,4–2,2 s |
| odczyt | **nie otwiera sesji** — idzie po SQL |
| sesja ubita przez watchdog (bez `LogoffUser`) | konto **nie zostaje zablokowane**; kolejne logowanie przechodzi, ale trwa ~16,7 s zamiast ~1,4 s — R2 sprząta martwą sesję ok. 15 s |
| 12 równoległych sesji z jednej maszyny (14.9) | wszystkie przeszły |

**Wniosek: licencja zajęta tylko na czas sesji zapisu, nie na stałe.** Ostatni wiersz tabeli sugeruje liczenie **per stanowisko (komputer)**, a nie per sesję — liczby stanowisk w licencji nie znamy (blob zaszyfrowany), więc to hipoteza.

**Konsekwencja dla watchdoga:** po ubiciu procesu roboczego następna próba logowania potrzebuje ~15 s więcej — limit czasu musi to uwzględniać, inaczej watchdog ubije też próbę naprawczą.

### 22.2. R2 PILNUJE limitu 4 dni urlopu na żądanie — twardo i narastająco

> **KOREKTA.** W `KONTRAKT-API-agent-serwer.md` napisałem, że „R2 sam tego nie kontroluje w sposób, na którym można polegać". **Niezweryfikowane i błędne.**

| Test | Wynik |
|---|---|
| 5 dni na żądanie jednym wpisem | **odrzucone**: „Limit urlopu na żądanie (max.4 dni) został przekroczony" |
| 4 dni na żądanie | zapisane z ostrzeżeniem: „… limit urlopu na żądanie (max.4 dni) został wykorzystany (Ostrzeżenie)" |
| kolejny, piąty dzień osobnym wpisem | **odrzucone** jak wyżej |

R2 sumuje dni `R_DNIA = 5` w roku. **Asymetria:** limit na żądanie — twarda blokada; limit wypoczynkowego — tylko ostrzeżenie (20.9).

### 22.3. Biblioteka jest podpisana cyfrowo przez producenta

Wszystkie sprawdzone pliki (`R2PApi10.dll` 10.10.32 i 10.10.33, `RPlatnik10.exe`, `update.exe`):

```
Status:     Valid
Podpisał:   CN=Symfonia Sp. z o.o., O=Symfonia Sp. z o.o., L=Warszawa, C=PL, SERIALNUMBER=0000228956
Wystawca:   DigiCert Trusted G4 Code Signing RSA4096 SHA384 2021 CA1   (certyfikat z weryfikacją rozszerzoną)
Ważny do:   2027-09-04, ze znacznikiem czasu
```

Connector może przed użyciem sprawdzić (`WinVerifyTrust`), że plik jest **oryginałem producenta** — niezależnie od tego, skąd go pobrał. Znacznik czasu zapewnia ważność podpisu także po wygaśnięciu certyfikatu.

**Czego podpis nie chroni:** podmiany na **starszą, oryginalną** wersję (atak przez cofnięcie). Potrzebny dodatkowo podpisany manifest `wersja → SHA-256` po naszej stronie.

### 22.4. Biblioteka może leżeć w osobnym katalogu connectora — ZWERYFIKOWANE

Nowy przełącznik `Diag.exe --dll <katalog>`: `SetDllDirectory(katalog_biblioteki)`, a `ConnectBin(katalog_instalacji)`.

| Test | Wynik |
|---|---|
| konfiguracja z ALFA (ma własną DLL 10.10.33), biblioteka 10.10.32 z magazynu | załadowana **z magazynu** (potwierdzone ścieżką modułu), pełny odczyt karty |
| **BETA — instalacja w ogóle bez API**, biblioteka 10.10.33 z magazynu | logowanie, `ConnectDaneX(1)`, otwarcie `PRACOWNICY` — **działa** |

**Licencja (`.lic`, `.prt`) i ustawienia (`.ini`) zostają w instalacji R2 i nigdy nie są kopiowane.** Connector podmienia tylko bibliotekę.

To umożliwia pomysł użytkownika: serwer z R2 nie jest regularnie aktualizowany, więc connector sam dociąga bibliotekę zgodną z wersją schematu bazy (`BIN.VERSION`), gdy stacje robocze podniosą wersję.

> **ZMIANA ZASADY.** Dotychczas: „ładuj DLL z katalogu BIN aktualnej instalacji". Teraz: **biblioteka z wersjonowanego magazynu connectora, dobrana do wersji schematu bazy; konfiguracja i licencja z instalacji R2.**

Warunki tego mechanizmu (zgłoszone przez programistę tenanta, wszystkie zasadne):

1. **podpis** — weryfikacja Authenticode (Symfonia Sp. z o.o.) + podpisany manifest wersji
2. **wersjonowanie i wycofanie** — katalog na wersję, dziennik: skąd, kiedy, SHA-256, sygnatariusz; poprzednia wersja zostaje
3. **prawo do redystrybucji** — **decyzja użytkownika (19.09.2026):** biblioteka pobierana z oficjalnej strony Symfonii i hostowana na `wezurlop.pl` w niezmienionej postaci, dostęp wyłącznie dla connectorów u licencjonowanych klientów R2. Argument: bez plików licencyjnych klienta biblioteka nie działa, a każdy klient może ją pobrać od producenta sam — automatyzowane jest tylko dostarczenie.

### 22.5. Limit na nowy rok — kiedy powstaje

| Rok | Wierszy limitu | Najwcześniej utworzony | Uwagi |
|---|---|---|---|
| 2023 | 55 | 10.01 | |
| 2024 | 54 | 11.01 | |
| 2025 | 45 | 10.01 | |
| 2026 | 43 | 30.12.2025 (1 wpis) | **25 z 43 utworzone 22.01.2026** |

**Przez pierwsze ~3 tygodnie stycznia większość pracowników nie ma limitu na bieżący rok.** Późniejsze wpisy (marzec–wrzesień) to nowo zatrudnieni.

Rozróżnienie jest proste: brak wiersza `L=1, R_DNIA=0` na rok Y = **limit jeszcze nienaliczony**; wiersz istnieje = limit naliczony. **Limitu równego zero nie ma w bazie ani razu** (`LIMIT_GODZ = 0` — 0 wierszy w latach 2023–2026).

W okresie przejściowym aplikacja może pokazać pozostałość z poprzedniego roku z adnotacją.

### 22.6. Rozwiązanie stosunku pracy

R2 **rozróżnia** dwa niezależne stany kartoteki:

| Stan | Pole | Liczba | Znaczenie |
|---|---|---|---|
| zwolniony | `PRACOWNK.Zwolniony = 1` | 114 | rozwiązany stosunek pracy |
| skasowany | `PRACOWNK.Skasowany = 1` | 196 | kartoteka usunięta / nieaktywna |

Data końca: `PRACOWNK.Data_zwolnienia` i `UMPRACA.Data_zakonczenia_pracy` — obie wypełnione u 89 zwolnionych, zgodne w 81 przypadkach. `HISTORIA.Data_Do` nieużywane. **31 zwolnionych nie ma daty nigdzie** (stare kartoteki).

**Niezweryfikowane:** czy przy wpisaniu zwolnienia z datą przyszłą (okres wypowiedzenia) flaga `Zwolniony` ustawia się od razu, czy dopiero po dacie — w danych nie ma ani jednego takiego przypadku. Rekomendacja: opierać się na **dacie**, flagę traktować jako stan.

Dla osób z kilkoma zatrudnieniami koniec pojedynczego zatrudnienia — `UMPRACA.Data_zakonczenia_pracy` tego zatrudnienia.

### 22.7. Kartoteki bez PESEL — problem znika

**Wszystkie 41 kartotek bez PESEL to kartoteki skasowane** (40 etat, 1 inna umowa). Wśród aktywnych pracowników — **zero**. Ekran ręcznego łączenia osób nie jest potrzebny dla aktywnych użytkowników.

Awaryjnie (cudzoziemcy bez PESEL w przyszłości): `PRACDANE` ma `Paszport`, `NrDokNIP`, `Data_urodzenia`, `NIP`.

### 22.8. Edycja wpisu przez kadry — ten sam identyfikator

Potwierdzone dwoma drogami: ręczna edycja wpisu `12316` w programie (odznaczenie flagi wypłaty) i edycja dat przez API (`12319`) — **`X_I` bez zmian**, zmieniają się dane i `X_F6DataCzas`. Porównanie sum wykryje to jako **zmianę**, usunięcie — jako **zniknięcie identyfikatora**.

Niezweryfikowane: co się dzieje, gdy kadrowa wydłuży urlop **przez przełom miesiąca** — R2 może wtedy utworzyć nowy rekord dla drugiego miesiąca.

### 22.9. Jakość danych nieobecności

- `KART_ZAS`: brak korekt (`KOREKTA`, `X_KORZAS` = 0), **brak nakładających się okresów**; ale **591 z 884 to kontynuacje** (`KONTYNUACJA = 1`) — jedno długie zwolnienie = kilka kolejnych wpisów L4. Do prezentacji sklejać.
- `OKRESHIS`: brak anulowań (`Anuluj = 0`, `Status = 0` we wszystkich 70).
- `KART_URL` (okresy): `ANULUJ = 0`, `KOREKTA = 0` we wszystkich.

### 22.10. Odczyt przez SQL — potwierdzone jako docelowe

Odczyt zostaje przy SQL: 17 ms, bez sesji API, a więc **bez zajmowania licencji** — to po 22.1 argument rozstrzygający.

Poświadczenia: osobny login SQL tylko do odczytu (`db_datareader` na bazach `_bin` i `_dane_N`), albo uwierzytelnianie Windows konta usługi connectora.

**Uwaga bezpieczeństwa:** biblioteka API łączy się z SQL poświadczeniami z `RPlatnik.ini` — u tego klienta to **`sa`** (14.11). To konfiguracja R2, nie nasza, ale maszyna z connectorem przechowuje (zaszyfrowane) poświadczenia z pełnym dostępem do bazy. Do opisania w dokumentacji wdrożenia.

### 22.11. Licencja = liczba jednoczesnych połączeń (informacja użytkownika, 19.09.2026)
> **NIEAKTUALNE — patrz 22.14.** Test przy obu zajętych stanowiskach: API zalogowało się, zapisało i usunęło urlop, nikogo nie wylogowując.

Licencja R2 na maszynie testowej: **2 jednoczesne połączenia.** Gdy oba zajmują osoby zalogowane w programie, **logowanie przez API się nie udaje.** Tak użytkownik zweryfikował, że API pobiera licencję (22.1).

Pogodzenie z testem 14.9 (12 równoległych sesji API przeszło): wszystkie były z **jednej maszyny, na tym samym koncie** — najpewniej liczone jako jedno połączenie. Hipoteza: licencja liczy stanowiska (maszyny/konta), nie sesje.

**Skutek operacyjny — najpoważniejsze ryzyko wdrożeniowe z dotychczas znanych:** u klienta z małą liczbą stanowisk kadrowe pracujące w programie przez cały dzień mogą **zablokować zapis urlopów na cały dzień**. Mitygacje:

1. brak licencji = **błąd przejściowy** — zadanie w kolejce, ponawianie, u tenanta stan „oczekuje na zapis w kadrach"
2. **okna czasowe zapisu** — np. przed i po godzinach pracy kadr
3. **dodatkowe stanowisko dla connectora** — koszt po stronie klienta, do wpisania w wymaganiach wdrożenia

**Otwarte:** dokładna treść komunikatu przy braku wolnej licencji i — kluczowe — **czy to błąd, czy modalne okno.** Do przechwycenia testem na docelowym koncie przy zajętych obu stanowiskach, pod ochroną watchdoga.

### 22.12. FORMUŁA SALDA — OSTATECZNA (zamyka 20.8)

Weryfikacja ekranem programu dla osób na niepełnym etacie:

| Osoba | Etat | Program | Formuła z 20.7 | Formuła ostateczna |
|---|---|---|---|---|
| KAMIŃSKI KAMIL | ½ | **15D 4G** | 15D 4G ✓ | 15D 4G ✓ |
| LEWANDOWSKI TOMASZ | ¾ | **13D 7G** | 13D 5G ✗ (−120 min) | **13D 7G ✓** |

**Przyczyna rozjazdu u Lewandowskiego:** wpis `11918` (1–7.01.2026) ma `KOR_POZOST = 1`, `KOR_POZOST_GODZ = 120` — **ręczną korektę salda** wprowadzoną przez kadry. Formuła jej nie uwzględniała.

Korekty w bazie: 4 w 2026, historycznie kilkadziesiąt, **wartości dodatnie i ujemne** (od −17 760 do +32 160 min).

**Formuła ostateczna:**

```sql
pozostalo_min =
    POZOSTALO_GODZ            -- wiersz L=1 AND R_DNIA=0 roku Y (stan na 1 stycznia: wymiar + zaległy)
  - SUM(IL_GODZ)              -- wiersze L=0 AND R_DNIA IN (0,5) roku Y
  + SUM(KOR_POZOST_GODZ)      -- te same wiersze, gdzie KOR_POZOST = 1
```

**Prezentacja:** `D = minuty ÷ 480`, `G = (minuty mod 480) ÷ 60` — **także przy niepełnym etacie.** Kamiński (½ etatu, 7 440 min) widzi `15D 4G`, nie `31D`. Dzień w saldzie to zawsze 8 h, choć jeden dzień jej urlopu zużywa 240 min.

**Saldo narastające R2.** Wiersze okresów (`L = 0`) mają własne `POZOSTALO_GODZ` — saldo po danym wpisie, liczone przez program, z korektami. Wiersze innych rodzajów (np. okolicznościowy) mają tam `NULL`. Saldo bieżące = `POZOSTALO_GODZ` ostatniego (po `OD`) wiersza `L=0, R_DNIA IN (0,5)` roku; przy braku wpisów — z wiersza limitu.

**Weryfikacja zbiorcza: formuła = saldo narastające R2 u 36 z 36 etatowców.** Łącznie z ekranem programu: 11 osób potwierdzonych bezpośrednio.

**Rekomendacja dla connectora:** liczyć formułą (nie zależy od kolejności wpisów), saldem narastającym sprawdzać kontrolnie i rozbieżność logować jako ostrzeżenie. Niezweryfikowane: czy R2 przelicza saldo narastające kolejnych wierszy po wstawieniu urlopu „wstecz", przed istniejącymi wpisami.

Na marginesie: pole `OLD_IL_GODZ` bywa różne od `IL_GODZ` (np. 1 920 wobec 1 560) — R2 liczy saldo z `IL_GODZ`.

### 22.13. Test na dedykowanym koncie `WNIOSKI_DEMO` (19.09.2026)

Konto założone przez użytkownika w instalacji ALFA: `USERS.X_I = 6`, grupa **„Kadry — wprowadzanie"** (8), `X_DBLog = 1`. W instalacji BETA konta nie ma.

> **To nie jest zestaw minimalny.** Grupa 8 ma prawo zapisu do 77 tabel i `F6A`/`F8A = 1`. Test potwierdza, że **konto działa**, nie że działa **minimum** z 21.5. Do zrobienia: grupa minimalna, a na niej test `F6A`/`F8A`.

| Krok | Wynik |
|---|---|
| `LoginUser("WNIOSKI_DEMO", …)` | ✓ `USERS.X_I = 6` |
| zapis 14–15.12.2026 | ✓ `X_I = 12324`, `IL_DNI = 2`, `IL_GODZ = 960`; 3,1 s (pierwsze uruchomienie) |
| edycja `WYPL_NA_KOL_L` | ✓ |
| usunięcie | ✓ |
| dziennik R2 (`X_DBLog = 1`) | tylko „Logged On" / „Logged Off" — **potwierdza po raz drugi: operacje API nie trafiają do dziennika** |

**Rozpoznawanie własnych rekordów.** Zapis ustawił `X_F7 = 6` (kto utworzył), edycja `X_F6 = 6` (kto zmienił). Connector może więc bez żadnego własnego oznaczania odróżnić:

- wpisy utworzone przez siebie (`X_F7 = id konta connectora`) od wpisów kadr,
- **edycję swojego wpisu przez kadrową** (`X_F7 = connector`, `X_F6 ≠ connector`).

Dodatkowa, niezależna od `OPIS` ścieżka powiązania wpisu z connectorem.

**Pole „zatwierdził" (`Zatwierdzil_wniosek`) = `USERS.Nazwisko` + `Imie`**, a gdy puste — login:

| Konto | Nazwisko / Imię | Na karcie urlopowej |
|---|---|---|
| ADMIN | — / — | `ADMIN` |
| KADRY1 | Kadrowa / Anna | `Kadrowa Anna` |
| WNIOSKI_DEMO | WWW / WNIOSKI | **`WWW WNIOSKI`** |

Rekomendacja: ustawić nazwisko i imię konta tak, żeby kadrowa rozumiała wpis bez wyjaśnień (np. „System wniosków urlopowych").

**Wciąż otwarte:**

1. **brak wolnej licencji — błąd czy modalne okno?** Wymaga zajęcia obu stanowisk przez inne konta i próby logowania `WNIOSKI_DEMO`
2. zestaw minimalny i znaczenie `F6A`/`F8A` — wymaga przepięcia konta do grupy z 21.5

### 22.14. KOREKTA 22.1 i 22.11: zajęta licencja NIE blokuje API — test (19.09.2026, 17:38–17:45)

> **22.1 („API pobiera licencję — potwierdzone przez użytkownika") i 22.11 („gdy stanowiska zajęte, API się nie zaloguje") są NIEAKTUALNE.** Rozstrzygnął test na docelowym koncie.

**Układ testu:** licencja na 2 jednoczesne stanowiska. Oba zajęte przez osoby zalogowane w programie — **KADRY2** (17:38:20) i **KADRY4** (17:38:58), procesy `RPlatnik10.exe` na `SERWER-DEMO`. Próby wejścia do programu jako trzecia osoba (ADMIN, 17:38:07 i 17:38:42) — odrzucone komunikatem o dwóch zalogowanych użytkownikach z propozycją ich wylogowania.

**Wynik — connector `WNIOSKI_DEMO` przy obu zajętych stanowiskach:**

| Krok | Wynik |
|---|---|
| `LoginUser` | ✓ bez błędu i bez okna (skrypt `test-licencja.ps1`: proces zakończył się sam po 2,0 s) |
| `ConnectDaneX(1)` | ✓ |
| zapis urlopu `12325` | ✓ `IL_DNI = 2` — warstwa logiczna zadziałała |
| usunięcie | ✓ |
| KADRY2 i KADRY4 | **nadal zalogowane** — API nikogo nie wylogowało |

**Wniosek: limit jednoczesnych stanowisk pilnuje programu, nie API.** Brak wolnego stanowiska nie zatrzymuje zapisów z connectora. Zgodne z dokumentacją producenta (§25 źródeł: aplikacja internetowa podłącza się jako „jeden użytkownik specjalny, nielimitowany") i z testem 14.9 (12 równoległych sesji).

**Skąd wcześniejszy wniosek:** najpewniej z **blokady tego samego użytkownika** (16.2). Gdy w programie ktoś jest zalogowany jako X, logowanie API jako X zwraca „Użytkownik X jest zalogowany na …" — wygląda jak brak licencji, ale to inny mechanizm. Trafiliśmy na nią rano, gdy program był otwarty jako ADMIN.

**Konsekwencje:**

- odpadają mitygacje z 22.11 (dodatkowe stanowisko dla connectora, okna czasowe zapisu)
- zostaje wymóg **konta dedykowanego** — człowiek na koncie connectora nadal blokuje zapisy
- stan „oczekuje na zapis w kadrach" po stronie tenanta nadal potrzebny dla blokad przejściowych

**ZASTRZEŻENIE UŻYTKOWNIKA (19.09.2026) — wynik NIE jest ostateczny.** Wszystkie trzy sesje (KADRY2, KADRY4 w programie, connector przez API) działały **na tym samym komputerze** `SERWER-DEMO`. Możliwe, że R2 liczy stanowiska per stacja, a nie per sesja — wtedy test nie rozstrzyga, jak się zachowa connector, gdy stanowiska zajmą osoby z **innych komputerów** (typowy układ produkcyjny: kadrowe na stacjach, connector na serwerze). **Do wykonania: test z dwiema osobami zalogowanymi z dwóch różnych stacji.** Do tego czasu w projekcie connectora zakładamy wariant ostrożny: przejściowy błąd logowania = ponowienie później (`bladPrzejsciowy`), bez wymagań wobec tenanta.

Przesłanki: (a) za wynikiem — trzecia osoba w **programie** (ADMIN) została odrzucona **z tego samego komputera**, więc program liczy stanowiska per sesja, nie per stacja; (b) nierozstrzygnięte — w bazie `_bin` jest tabela `LICSRVCONN` (`Machine`, `UserName`, `Server`, `DBaseName`), pusta u klienta odniesienia: ślad trybu **serwera licencji**, w którym liczenie może wyglądać inaczej. Zajętości stanowisk **nie da się odczytać z SQL**.

**Otwarte — test odwrotny:** czy otwarta sesja API **sama** zajmuje stanowisko, tzn. czy człowiek logujący się do programu w chwili zapisu dostałby odmowę. Nawet jeśli tak — dotyczy wyłącznie 1,4–2,2 s zapisu.

### 22.15. Wykrywanie zmian i usunięć — pomiar (19.09.2026)

**Jak R2 usuwa:**

| Tabela | Usunięcie | Dla connectora |
|---|---|---|
| `KART_URL`, `OKRESHIS`, `KART_ZAS` | **fizyczne** — wiersza nie ma (`12325` po `TableDelete` zniknął; w `KART_URL` 3 489 dziur w `X_I` na 8 575 wierszy). `ANULUJ`/`Anuluj` nieużywane (zawsze 0) | usunięcie = brak `id` względem migawki → rekord `usuniety` |
| `PRACOWNK`, `HISTORIA` | **flaga `Skasowany`** (196/353 i 195/1079 = 1), wiersz zostaje | zmiana rekordu (`skasowany: true`), nie usunięcie |

**Pomiar** (`diag\pomiar-delta.ps1`): SqlClient, `id + HASHBYTES('SHA2_256', …)` z 6 tabel, słownik w pamięci, porównanie z migawką z symulowaną 1 nową, 1 zmienioną i 1 usuniętą pozycją — wszystkie trzy wykryte.

| Firma | Rekordów | Odczyt z SQL | Porównanie |
|---|---|---|---|
| ALFA | 12 021 | 35 ms | 29 ms (PowerShell; w C# rząd mniej) |
| BETA | 3 875 | 11 ms | 3 ms |

Wnioski: pełny odczyt w każdym cyklu jest tani — i **konieczny**, bo usunięcia inaczej wykryć się nie da. Przyrost danych ~1 tys. wierszy rocznie na firmę.

**Zabezpieczenie obowiązkowe:** błędny albo niepełny odczyt (zła baza po zmianie konfiguracji, przerwane zapytanie, pusty wynik) wyglądałby jak masowe usunięcie. Connector: (1) przy błędzie odczytu nie liczy różnic wcale; (2) odczyt wszystkich tabel jednym zapytaniem (bez `SNAPSHOT` — wymagałby `ALTER DATABASE` na bazie producenta); rekord zmieniony w trakcie odczytu wyrówna się w następnym cyklu; (3) **próg** — jeśli usunięć w cyklu jest więcej niż np. 5% lub 20 rekordów, wstrzymuje wysyłkę usunięć i alarmuje administratora.

### 22.16. Grupa uprawnień connectora — eksport i powielanie (19.09.2026)

Informacja od użytkownika: w R2 można **utworzyć grupę uprawnień, przypisać do niej użytkownika i ograniczyć jej prawa** do wybranych tabel, a potem **grupę wyeksportować** i zaimportować w innej instalacji. Grupę minimalną dla connectora robimy więc **raz** i powielamy u kolejnych klientów zamiast ustawiać uprawnienia ręcznie.

**Stan:** grupę przygotuje użytkownik — **nie teraz, później**. Do tego czasu `WNIOSKI_DEMO` zostaje w grupie „Kadry — wprowadzanie" (8).

**Zawartość grupy (propozycja z 21.5):** przeglądanie `Firmy`, `Pracownicy`, `Aktualne zatrudnienie`; przeglądanie + dodawanie + poprawianie + usuwanie na `Karta urlopowa`; **bez filtra** widocznych pracowników.

**Po utworzeniu:**
1. przepiąć `WNIOSKI_DEMO` do nowej grupy i powtórzyć test: zapis, edycja `WYPL_NA_KOL_L`, usunięcie;
2. sprawdzić znaczenie `F6A`/`F8A` (zmiana/usunięcie cudzych wpisów — czy connector ma móc ruszać wpisy kadrowej);
3. sprawdzić brak uprawnień (np. odebrany zapis) — jaki komunikat zwraca API;
4. wyeksportować grupę i dołączyć plik do pakietu wdrożeniowego connectora (instrukcja: import grupy → utworzenie konta → przypisanie do grupy).

Do sprawdzenia przy eksporcie: czy import działa między wersjami R2 i czy przenosi prawa per tabela (`TBRIGHTS`) razem z filtrem.

### 22.17. DECYZJA: „Uwzględnij na kolejnej liście wynagrodzeń" zawsze WYŁĄCZONA (19.09.2026)

Decyzja użytkownika: **connector przy każdym zapisie urlopu ustawia `WYPL_NA_KOL_L = false`** (`SetTableFieldAsBoolean(h, "WYPL_NA_KOL_L", false)` przed `TablePost`; domyślnie R2 wstawia `1`). Dotyczy także zapisu po „zmianie" realizowanej jako usunięcie + nowy zapis i obu części urlopu rozbitego na przełomie miesiąca — **zweryfikowane 19.09.2026:** zapis 30.11–1.12.2026 z flagą `false` → R2 rozbił na `12326` i `12327` (`KONTYNUACJA = 1`), **obie części `WYPL_NA_KOL_L = 0`**. Wpisy usunięte przez `TableDelete`.

**API tenanta nie ma i nie może mieć o tej fladze wiedzy** — nie ma jej w rekordach, zadaniach ani wynikach. Z `KSZTALT-DANYCH-R2.md` usunięto `wyplataNaKolejnejLiscie` i `ustawWyplate` (wersja 0.3); w mailu 4 poproszono tenanta o ich pominięcie. Zmianę flagi robią kadry w programie. Edycja flagi przez API (22.x, `--edytuj`) zostaje wyłącznie jako zdolność diagnostyczna, nieużywana przez connector.

### 22.18. Protokół tenanta uzgodniony (19.09.2026)

Po 5 turach korespondencji: `API-TENANTA-v1.md` (specyfikacja, sekcja 5b = rozstrzygnięcia) + `PRZYKLADY-API-TENANTA.md` (kształt wiążący, do atrapy serwera). Kluczowe dla implementacji connectora:

- adres = domena klienta, jeden klucz na connector u klienta (oba nagłówki: `Authorization: Bearer` i `X-Api-Key`), `X-Protokol: 1`, `X-Idempotency-Key` tylko przy POST;
- cykl per firma: `start` (ze stanem connectora i `kursory`) → `dane` (paczki ≤500 rek./2 MB, usunięcia jako `{id, usuniety:true}`) → opcjonalnie `przeglad` → `GET zadania` → `wyniki` → `koniec` (zwraca `nastepnyInterwal`); jeden wspólny cykl dla wszystkich firm, ≥30 s;
- `401` zatrzymuje cały connector, `403` tylko firmę; `422` = cała paczka odrzucona z listą pozycji;
- niezatwierdzona firma: `start` → `403 FIRMA_NIEZATWIERDZONA`, zgłoszenie przez `zgloszenie-firmy`;
- **`zmiana` w etapie 1 NIE będzie wydawana** (zmiana terminu = `usuniecie` + nowy `zapis`). Decyzja connectora: gdyby przyszła — wynik `blad`, `kod: OPERACJA_NIEOBSLUGIWANA`, nic nie ruszone;
- **reguła wyniku (nasza, przyjęta):** `zapisy` = faktyczny stan w R2 po zadaniu, niezależnie od `status`;
- `zrodloWpisu` (`connector` | `kadry`) liczony z `X_F7 = id konta connectora` — tenant widzi pole, nie wie, skąd się bierze;
- flaga wypłaty: poza protokołem; connector i tak zapisuje `false` (22.17). Tenant w 5b pisze „zapis zostawia ją nietkniętą" — nie prostujemy, to po naszej stronie.

Brakuje tylko instancji testowej (adres, klucz) i OpenAPI — tenant przyśle.

### 22.19. KOREKTA FORMUŁY SALDA: kilka wierszy limitu w jednym roku (19.09.2026)

Przy budowie connectora wyszło, że zatrudnienie może mieć **kilka wierszy limitu (`L=1, R_DNIA=0`) w tym samym roku**: ALFA 22 grupy, BETA 7. Dwa wzorce:

- **dołożenie w trakcie roku** — np. 1077/2024: 1.01 limit 8 160, 13.02 dodatkowe 2 400 (przekroczenie progu stażu 20 → 26 dni);
- **urlop narastający przy pierwszej pracy** — 1/12 co miesiąc, wiersz limitu co miesiąc (np. 843/2015, 1016/2019).

`POZOSTALO_GODZ` **kolejnego** wiersza limitu to saldo **po** dotychczasowym zużyciu, nie stan na 1 stycznia. Formuła z 22.12 (jeden wiersz limitu) odejmowałaby zużycie podwójnie.

**Formuła ostateczna (uogólniona):**

```
pozostalo_min =
    POZOSTALO_GODZ pierwszego (po OD, X_I) wiersza L=1, R_DNIA=0 roku Y
  + Σ LIMIT_GODZ pozostałych wierszy L=1, R_DNIA=0 roku Y
  − Σ IL_GODZ wierszy L=0, R_DNIA IN (0,5) roku Y
  + Σ KOR_POZOST_GODZ tych wierszy, gdzie KOR_POZOST = 1
```

Przy jednym wierszu limitu sprowadza się do 22.12.

**Weryfikacja z saldem narastającym R2** (`POZOSTALO_GODZ` ostatniego wiersza roku), lata 2019–2026, oba firmy:

| | Lat (zatrudnienie × rok) | Nowa formuła zgodna | Stara (tylko pierwszy limit) |
|---|---|---|---|
| jeden limit | 710 | 709 | 709 |
| **kilka limitów** | **13** | **13** | **0** |

Jedyny rozjazd (860/2021) to artefakt kontroli, nie formuły: wiersz limitu założony po urlopie z tą samą datą `OD` wypada jako „ostatni". Formuła daje poprawne 0 (19 dni z 19). **Wniosek dla kontroli w connectorze:** saldo narastające brać z ostatniego wiersza **`L=0`** roku (jak w 22.12), a wiersze `L=1` tylko gdy wpisów `L=0` brak.

W 2026 w danych klienta takich przypadków nie ma — pojawią się przy pierwszej podwyżce stażowej albo nowym pracowniku w pierwszym roku pracy.

### 22.20. Krok 2 connectora — odczyt, rekordy, porównanie: wyniki na danych klienta (19.09.2026)

Kod: `C:\WU_Connector\src\WuConnector.Core` (odczyt SQL, budowa rekordów, saldo, magazyn stanu SQLite). Narzędzie kontrolne: `tools\Podglad`.

| | ALFA | BETA |
|---|---|---|
| rekordów (7 zbiorów) | 9 769 | 3 248 |
| rozmiar JSON | 3,2 MB | 1,0 MB |
| paczek po 500 | 23 | 11 |
| odczyt SQL + budowa | 0,4 + 0,25 s | 0,09 + 0,06 s |
| cały cykl bez zmian | 0,3 s | 0,1 s |

- **Salda = ekran R2 u 6/6 osób** (Kowalski, Nowak, Wiśniewski, Wójcik, Lewandowski, Kamiński), co do godziny.
- **PESEL nie występuje w żadnym rekordzie** (sprawdzane automatycznie przy każdym uruchomieniu podglądu).
- **Sumy stabilne**: drugi odczyt po potwierdzeniu → 0 zmian.
- **Scenariusz od końca do końca**: zapis urlopu procesem roboczym → podgląd wykrył dokładnie `urlop:12330` (nowy) + `207:2026:wypoczynkowy` (zmieniony); usunięcie → `-urlop:12330` + limit z powrotem. Wpis testowy usunięty.

**Dwie pułapki danych wykryte przy budowie:**

1. **`HISTORIA.AktZatrudnienie = 1` nie znaczy „nadal pracuje"** — to bieżący rekord zatrudnienia, mają go też zwolnieni. W ALFA 88 zwolnionych z „aktualnym" zatrudnieniem i bez limitu 2026. Rekord `naliczony: false` wysyłamy tylko dla `!Zwolniony`. Po poprawce: 0 nienaliczonych w ALFA (każdy z 36 pracujących na UoP ma limit 2026), 1 w BETA.
2. **Ta sama osoba bywa w kilku kartotekach tej samej firmy** (np. Lewandowski: bieżąca + zwolniona w 2020, obie z „aktualnym i domyślnym" zatrudnieniem). Po nazwisku nie wolno łączyć — tylko `osobaId`.

**Bezpiecznik usunięć** działa tylko na `pracownik`, `zatrudnienie`, `nieobecnosc`. Limity i kalendarze wysyłamy w oknie lat (rok −1 … +1), więc w styczniu stary rok legalnie „znika" — bezpiecznik zablokowałby każdą zmianę roku.

### 22.21. ZAKRES OSÓB: tylko zatrudnieni na umowę o pracę (decyzja użytkownika 19.09.2026)

Do tenanta idą **wyłącznie** pracownicy aktualnie zatrudnieni na umowę o pracę, ich aktualne zatrudnienia, limity i nieobecności. Reguła w kodzie: `BudowniczyRekordow.WZakresie` — `Umowa_o_prace = 1` ∧ `Skasowany = 0` ∧ (`Zwolniony = 0` ∨ `Data_zwolnienia ≥ dziś`). Nieobecności od 1 stycznia roku poprzedniego. PESEL czytany tylko dla osób potencjalnie w zakresie.

| | przed | po |
|---|---|---|
| ALFA | 353 kartoteki, 9 769 rekordów | **36 osób, 845 rekordów** |
| BETA | 86 kartotek, 3 248 rekordów | **53 osoby, 1 168 rekordów** |

W zakresie: 0/89 bez PESEL, 0/89 bez kalendarza, 1/89 bez umowy przypiętej do zatrudnienia (etat `null`).

**Bezpiecznik usunięć po zmianie zakresu.** Zwolnienie wyrzuca z zakresu osobę z całą historią — to może być kilkadziesiąt rekordów naraz. Magazyn stanu zapamiętuje przy każdym rekordzie `grupa` (id pracownika) i `koniec` (data końca). Usunięcie jest **wyjaśnione**, gdy pracownik wypadł z zakresu albo rekord kończy się przed oknem; bezpiecznik liczy tylko **niewyjaśnione**. Dodatkowo **gwałtowny spadek** (odczyt < 50% potwierdzonego stanu przy ≥ 10 rekordach) wstrzymuje usunięcia zawsze — chroni przed złą bazą, w której „wyjaśnione" byłoby wszystko.

Tenant poinformowany: `ODPOWIEDZ-7-dla-programisty-tenanta.md`, `KSZTALT-DANYCH-R2.md` v0.7.

### 22.22. Tura 7 tenanta — rozluźnienia i limity (19.09.2026)

Tenant przyjął zakres 0.7 w całości. Zmiany po jego stronie: (1) poprawiony `PRZYKLADY-API-TENANTA.md` — w przykładzie `zasilek` brakowało wymaganego `zrodloWpisu` (nasze rekordy zawsze go miały; atrapa odrzucała brak jak serwer); (2) brak klucza pola **opcjonalnego** = `null`; wymagane dalej twardo `422`; (3) brak `X-Protokol` = wersja 1; (4) kolejność zbiorów nie jest wymogiem — rekord bez rodzica czeka, a po pełnym przeglądzie trafia do konfliktów; (5) **limity: 120 żądań/min na klucz, 10 zgłoszeń firmy/h**.

Po naszej stronie: `Ogranicznik` w `KlientTenanta` — max **100 żądań / 60 s** (okno przesuwne, wspólny dla wszystkich firm jednego klucza). Uzasadnienie: cykl to ~5 żądań na firmę; przy ~20 firmach na jednym kluczu zbliżylibyśmy się do 120. Sprawdzone: wszystkie zatrudnienia w zakresie wskazują kalendarz obecny w wysyłanym oknie lat (36/36, 53/53) — rekordów „bez rodzica" nie generujemy.

### 22.23. Magazyn bibliotek R2 — zbudowany i sprawdzony (19.09.2026)

Kod: `C:\WU_Connector\src\WuConnector.Core\Biblioteki`, narzędzie wydawcy `tools\Manifest`. Commit `047fa79`.

**Dobór biblioteki** do wersji bazy (`BIN.VERSION`, czytana co cykl): katalog z konfiguracji → BIN z pasującą wersją → magazyn → pobranie → BIN w niepasującej wersji z ostrzeżeniem → brak (`bladPrzejsciowy`, stan `zapisWstrzymany`). Dobór dopiero przy zadaniu zapisu — bez zadań nic nie jest pobierane.

**Zgodność wersji:** baza `10.10.33` ↔ plik `10.1.0.33` (i `.32` ↔ `.32`) — numer główny i kompilacji; manifest jest wiążący, heurystyka tylko dla pliku w BIN.

**Trzy zabezpieczenia pobrania:**
1. **Manifest podpisany przez nas** — ECDSA P-256, klucz publiczny wbudowany w connector (`KluczManifestu`). Manifest starszy niż już widziany jest odrzucany (ochrona przed cofnięciem).
2. **SHA-256** pliku zgodna z manifestem.
3. **Podpis producenta** — `WinVerifyTrust` + certyfikat `CN=Symfonia Sp. z o.o.`, **`SERIALNUMBER=0000228956` (KRS)**. Przypięcie do KRS, nie tylko do nazwy. Sprawdzany także przy każdym użyciu pliku z magazynu. Rewokacja wyłączona (serwery klientów bywają bez dostępu do CRL).

Podpis obu bibliotek w `C:\Symfonia`: ważny, DigiCert Trusted G4 Code Signing (EV), znacznik czasu DigiCert.

**Klucz manifestu: DEWELOPERSKI** — `C:\WU_Connector_klucze\klucz-prywatny.pem` (poza repozytorium). **Przed pierwszym wdrożeniem u klienta** trzeba wygenerować klucz produkcyjny na odłączonej maszynie (`Manifest generuj-klucz`), podmienić stałą w `KluczManifestu.cs` i trzymać klucz prywatny wyłącznie u wydawcy.

**Adres serwera bibliotek — do ustalenia:** konfiguracja ma pole `adresManifestuBibliotek` (np. `https://wezurlop.pl/r2api/manifest.json`); bez niego connector nie pobiera, korzysta z BIN i magazynu. Struktura na serwerze: `manifest.json`, `manifest.json.sig`, `<wersja>/R2PApi10.dll` — wprost to, co tworzy `Manifest podpisz`.

**E2E:** BETA (bez biblioteki API) — usługa pobrała 10.1.0.33 z atrapy serwera bibliotek, zweryfikowała manifest, sumę i podpis producenta, zapisała do magazynu i uruchomiła proces roboczy na tej bibliotece (ConnectBin BETA poprawny; logowanie odrzucone, bo w BETA nie ma konta connectora — tak miało być).

**Wykryty przy tym błąd klasyfikacji:** „Nie można zlokalizować użytkownika X" (konto nie istnieje w instalacji) connector traktował jako przejściowe i ponawiałby bez końca. Poprawione na błąd konfiguracji.

### 22.24. Usługa jako SYSTEM, konto odczytu SQL, harmonogram, tura 8 (19.09.2026)

**Rejestracja usługi Windows (test, bez MSI) ujawniła: `NT AUTHORITY\SYSTEM` nie ma dostępu do baz R2** (`Login failed for user 'ZARZĄDZANIE NT\SYSTEM'`). U klientów będzie tak samo. Rozwiązanie (decyzja użytkownika — wariant 1): **osobny login SQL tylko do odczytu na każdą instalację** — `wu_<prefiks baz>` (np. `wu_r2p_platnik10_alfa`), `db_datareader` na `_bin` i `_dane_N` tej instalacji + `EXECUTE` na `_bin.dbo.fn_Kod`. Jeden wspólny login odpada: login jest serwerowy, więc założenie go dla drugiej instalacji zmieniało hasło zapisane przy pierwszej (błąd złapany w teście). Zakłada go okno („Utwórz konto odczytu" — kontem Windows albo `sa`), polecenie `--konto-sql` albo DBA skryptem (`KontoOdczytuSql.Skrypt`). Instalator MSI tego nie robi — nie zna danych administratora SQL, a SQL bywa na innej maszynie. **Po zmianie: usługa jako SYSTEM + login SQL — odczyt działa (cykle 64/53 ms).**

**Harmonogram** (decyzja użytkownika: minimum co 5 min): godziny i dni pracy, poza nimi rzadziej albo wstrzymanie. Domyślnie „Pn–Pt 06:00–20:00 co 5 min, poza tym co 60 min". Serwer może odstęp wydłużyć, nigdy skrócić. Zgłoszenia firmy najwyżej co 30 min (limit serwera 10/h przy cyklu co 5 min).

**Tura 8 tenanta** (`ODPOWIEDZ-8-od-tenanta.md`): NIP z sumą kontrolną (`422 NIP_NIEPOPRAWNY`) — connector sprawdza sam i błędnego NIP-u nie wysyła; `503 PRZERWA_TECHNICZNA` + `Retry-After: 30` — ponawiane tą samą paczką; zgłoszenie firmy: `zatwierdzona` → start w tym samym cyklu, `odrzucona` → zapamiętana w stanie, bez ponawiania. Uwaga do tenanta: `9876543210` **ma** poprawną sumę kontrolną (tenant pisał, że nie). `openapi-sync-v1.json` — użytkownik: może być nieaktualny, na razie nie używać.

**Pamięć NIP-u** w stanie: po restarcie bez dostępu do bazy connector melduje serwerowi `odczytWstrzymany` zamiast milczeć.
