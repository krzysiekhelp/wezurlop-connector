# Stan prac — WU_Connector

Dokument przeżywa wyczerpanie kontekstu sesji. Zapisuje **stan**, nie historię:
co jest zrobione, co jest pułapką i co czeka. Historia jest w `git log`.

**Ostatnia aktualizacja: 17.10.2026.** Źródła: weryfikacja w repozytorium i w systemie
(commity, wersje, usługa, rejestr) + przekazanie z równoległej sesji („AGENT B”),
która wykonała pracę nad oknem i pierwszą instalację z MSI.
Przy faktach sprawdzonych bezpośrednio stoi **(sprawdzone)**.

---

## 1. Punkt wyjścia

- Repozytorium `C:\WU_Connector`, gałąź **`main`**. **Historia zaczyna się od jednego czystego commita z 17.10.2026** — decyzja użytkownika po audycie: poprzednie 71 commitów zawierało prawdziwe dane osobowe i dane klienta, więc nie nadawały się do wypchnięcia.
- **Zdalny:** `origin` → **prywatne** repozytorium `krzysiekhelp/wezurlop-connector` na GitHubie, bez współpracowników. Pierwszy push 17.10.2026. Adres **używa aliasu SSH, nie `github.com`**:

      git@github-wezurlop-connector:krzysiekhelp/wezurlop-connector.git

  Adres zapisany wprost na `github.com` **nie zadziała** — ominąłby alias i `IdentitiesOnly`, więc SSH nie podałby klucza connectora.
- **Uwierzytelnianie: deploy key, nie konto.** Klucz ma dostęp **wyłącznie do tego jednego repozytorium**; poświadczenie konta `krzysiekhelp` zapisane w Git Credential Managerze dałoby dostęp do wszystkich repozytoriów konta i świadomie go tu nie używamy.

  | | |
  |---|---|
  | typ | ED25519, 256 bitów |
  | odcisk | `SHA256:nm4+U709QAGvdNbJNQFGXKTICPQmTLNhvhF1JAxUScY` |
  | klucz prywatny | `C:\Users\admin\.ssh\id_ed25519_wezurlop_connector`, ACL zawężone do jednego wpisu `admin:(R)`, dziedziczenie zdjęte |
  | alias | `C:\Users\admin\.ssh\config`, sekcja `Host github-wezurlop-connector` |
  | hasło klucza | **brak** — push idzie z sesji bez interakcji |

  Brak hasła to świadomy kompromis: kto odczyta ten plik, może pisać do repozytorium. ACL chroni przed innymi kontami, nie przed kimś, kto ma już konto administratora tej maszyny. Zysk względem tokena konta polega na tym, że szkoda ogranicza się do jednego repozytorium, a klucz unieważnia się natychmiast.

  **Unieważnienie:** GitHub → repozytorium `wezurlop-connector` → Settings → Deploy keys → usuń wpis `wezurlop-deploy-connector`. Push z tej maszyny przestaje działać od razu, reszta konta nietknięta.

  **Wymiana klucza** (rotacja, przenosiny na inną maszynę): `ssh-keygen -t ed25519 -C "wezurlop-deploy-connector" -f <nowa ścieżka> -N ""` → zawęź ACL pliku prywatnego → popraw `IdentityFile` w `~/.ssh/config` → dodaj nowy klucz publiczny w Deploy keys **z zaznaczonym „Allow write access"** → sprawdź `ssh -T git@github-wezurlop-connector` (odpowiedź musi witać nazwą repozytorium, nie nazwą konta) → usuń stary wpis w GitHubie i stary plik klucza. Klucza prywatnego **nigdy** nie commitujemy ani nie kopiujemy poza maszynę; `.gitignore` wyklucza `*.pem`, `*.key`, `klucz-prywatny*`.
- **Stara historia (71 commitów, gałęzie `main` i `konta-mssql`) jest zarchiwizowana lokalnie** w `C:\WU_Connector_archiwum\` — lustro `connector-historia-2026-10-17.git` i paczka `connector-historia-2026-10-17.bundle`. **Nie wolno jej kopiować poza tę maszynę ani wypychać** — zawiera dane, które właśnie z repozytorium usunięto. Narzędzie `tools\Podglad` leży tam w `poza-repo\`.
- Dokumentacja projektu leży osobno: `C:\Symfonia\_projekt-urlopy`. Numery w nawiasach (22.23, 22.24…) to rozdziały `USTALENIA-2026-09-13-srodowisko-i-API.md`.

## 2. Co jest zainstalowane na tej maszynie

Pierwsza prawdziwa instalacja z MSI jest wykonana — ręczna rejestracja usługi z `artefakty\app` została usunięta (`sc delete`).

- Usługa **`WU_Connector`**, stan `RUNNING`, obraz `C:\Program Files\WU_Connector\WuConnector.Service.exe`, auto-start, LocalSystem. **(sprawdzone)**
- Autostart okna: `HKLM\Software\Microsoft\Windows\CurrentVersion\Run\WU_Connector` = `"C:\Program Files\WU_Connector\WuConnector.Okno.exe" --zasobnik`. **(sprawdzone)**
- **Zainstalowana paczka: `0.1.4`** (17.10.2026 18:05, aktualizacja przez MSI z 0.1.3). Usługa `WU_Connector` chodzi, `Core.dll` SHA-256 `59D595ECFAB9E6E4…`. **(sprawdzone)** Ścieżka aktualizacji przetestowana na żywo — patrz 3.8.
- Katalog `worker\` na miejscu (proces roboczy net48 x86 obok okna i obok usługi).
- Po starcie usługa przeszła cykl obu firm (850 ms i 233 ms), `tenant.wezurlop.pl` odpowiada.
- Użytkownik bez uprawnień **czyta** konfigurację i dziennik; zapis podnosi uprawnienia.
  `C:\ProgramData\WU_Connector` jest niedostępny z sesji bez elewacji — ACL (SYSTEM + Administratorzy) działa. **(sprawdzone)**

## 3. PUŁAPKI — przeczytaj przed wdrożeniem

### 3.1 MSI nie nadpisuje plików o tej samej wersji — ZAŁATWIONE licznikiem (17.10.2026)

**Pułapka, o której trzeba wiedzieć, choć już nie gryzie.** MSI nie nadpisuje plików o tej samej wersji
— w dzienniku instalacji zostawia „Won't Overwrite; Existing file is of an equal version". Dopóki
wszystkie paczki nosiły numer 0.1.0, wgranie nowej **cicho zostawiało stare binaria**; przy pierwszym
wdrożeniu trzeba było odinstalować, wyczyścić `Program Files` i zainstalować od zera.

**Jak jest teraz.** Jedno źródło wersji dla całego produktu: **`build\wersja.txt`**. Czyta je
`Directory.Build.props`, więc trzy projekty (`Okno`, `Service`, `Worker`) i instalator biorą ten sam
numer — **w żadnym `.csproj` nie ma już `<Version>`**. Ten numer jedzie też do protokołu jako
`wersjaConnectora` (usługa czyta go z własnego zestawu: `SluzbaConnectora.Wersja`).

`build\publikuj.ps1` **podbija trzecie pole przy każdym budowaniu** (0.1.N → 0.1.N+1) i zapisuje je
z powrotem do `wersja.txt`. Przełączniki: `-BezPodbicia` (przebudowa bieżącego numeru — takiej paczki
nie wydawać), `-Wersja x.y.z` (wymuszenie, np. przy zmianie minor).

**Po zbudowaniu paczki trzeba zacommitować `build\wersja.txt`** — inaczej następny build na innej kopii
repozytorium wyda ten sam numer. Skrypt o tym przypomina na końcu.

**Dlaczego licznik w pliku repozytorium, a nie z liczby commitów:** plik jedzie z repozytorium, więc po
klonie licznik nie wraca do zera i nie wydamy drugi raz numeru, który stoi już u klienta. Liczba commitów
ma tę własność, ale łamie związek numer ↔ zawartość (build z brudnego drzewa albo z gałęzi dostaje numer
opisujący inną zawartość — czyli dokładnie ten błąd, który naprawiamy) i nie jest monotoniczna między
gałęziami ani po scaleniach, a `MajorUpgrade` wymaga rosnącego numeru.

**Sprawdzone na dwóch paczkach (0.1.2 i 0.1.3):** `UpgradeCode` ten sam
(`{6B0C8A5E-2F4D-4A7B-9E31-7C1D5A0F4E21}`), **`ProductCode` różny w każdej wersji** (WiX generuje go
sam), `ProductVersion` rośnie, a wszystkie trzy binaria raportują numer paczki. **(sprawdzone)**

**Nadal nieprzetestowana jest sama ścieżka aktualizacji N → N+1 na maszynie** — patrz punkt 2 listy
„co czeka". Skrypt gotowy: `build\test-aktualizacji.ps1 -Stara 0.1.2 -Nowa 0.1.3`, **uruchamia go
administrator** (zatrzymuje usługę i instaluje produkt). Sprawdza podmianę binariów po SHA-256,
przeżycie konfiguracji i sekretów w `ProgramData`, liczbę wpisów w „Programach i funkcjach", powrót
usługi, a w dzienniku MSI wyszukuje „Won't Overwrite".

### 3.2 Klucz manifestu bibliotek R2 jest DEWELOPERSKI (22.23) — blokujące

Do wymiany przed wdrożeniem u klienta.

### 3.3 Nic nie jest podpisane Authenticode

`WuConnector.Okno.exe` i paczka MSI → `Status: NotSigned`. **(sprawdzone)**

Skutek praktyczny: Windows pokazuje żółte okno UAC „Nieznany wydawca” z ikoną generyczną.
**To nie jest błąd ikony** — `ApplicationIcon` jest ustawiony, a `Grafika\ikona.ico` ma pełny zestaw
16 / 24 / 32 / 48 / 64 / 128 / 256 px. **(sprawdzone)** Ładne okno UAC z nazwą wydawcy i maskotką
wezurlop wymaga certyfikatu code signing — zakupu u CA, nie zmiany w kodzie.
**Jeden certyfikat załatwia to razem z 3.2.**

### 3.4 Usługa pracuje jako SYSTEM i nie ma dostępu do baz R2 (22.24)

Dlatego z sekcji „Połączenie z bazą programu kadrowego” usunięto wybór
„Windows — konto usługi (SYSTEM)”; zostaje login SQL (konto `wu_*`, tylko odczyt).
Konta zalogowanego człowieka nie da się tam użyć, bo usługa nigdy na nim nie pracuje.

Stare konfiguracje z pustym `LoginSql` nadal działają (`CzytnikSql` robi wtedy logowanie zintegrowane),
ale `PlikKonfiguracji.Rozwin` dopisuje ostrzeżenie do dziennika:
„instalacja X czyta bazę kontem usługi (SYSTEM) — załóż konto SQL w oknie konfiguracji”.

**Objaw zaobserwowany 16.10:** 7 błędów „odczyt SQL nieudany” między 19:53 a 21:00; ustały po
zapisaniu sekcji SQL i restarcie usługi — usługa miała nieaktualne hasło SQL.

### 3.5 Autozapis zaznaczenia firmy wywołuje UAC przy każdym kliknięciu

Zaznaczenie firmy zapisuje się od razu, bez przycisku. Bez praw administratora **każde kliknięcie
podnosi uprawnienia** (zapis w `ProgramData`). Buforowanie tych zapisów nie zostało uzgodnione.

### 3.6 Proces roboczy musi być skopiowany przy każdym buildzie

Kopiuje się do `worker\` obok okna **oraz** obok usługi. Wcześniej trafiał tam tylko przy publikacji
paczki, więc test logowania w wersji deweloperskiej kończył się „Brak procesu roboczego”.
Buduje go **zadanie MSBuild** (`Targets=Restore;Build`, `RemoveProperties=RuntimeIdentifier;SelfContained;PlatformTarget;…`),
**nie `ProjectReference`** — referencja przenosiła na niego `RuntimeIdentifier win-x64` z publikacji
i wywracała `dotnet publish`.

### 3.7 Build jest blokowany przez chodzące okno

Uruchomione `WuConnector.Okno.exe` trzyma `WuConnector.Core.dll` w katalogu wyjściowym
(`MSB3027` / `MSB3021`). Jeśli okno chodzi **z podniesionymi uprawnieniami**, sesja bez elewacji
go nie ubije („Odmowa dostępu”) — trzeba je zamknąć ręcznie. **(sprawdzone)**
Uwaga: błąd kopiowania wypada **przed** kompilacją, więc **maskuje błędy kompilacji** — po takim
niepowodzeniu nie wolno zakładać, że kod się kompiluje.

### 3.8 Instalator NIE uruchamia usługi — po aktualizacji connector stoi (wymaga decyzji)

**Wyszło przy pierwszym teście ścieżki aktualizacji, 17.10.2026. (sprawdzone na żywo, dwa razy.)**

W `Package.wxs` `ServiceControl` ma `Stop="both"` i `Remove="uninstall"`, ale **nie ma `Start`**.
`Start="auto"` w `ServiceInstall` ustawia tylko **typ** startu (automatyczny przy rozruchu), a nie
startuje usługi w trakcie instalacji. Skutek: **po każdej aktualizacji MSI usługa zostaje zatrzymana**,
dopóki ktoś jej nie uruchomi albo maszyna się nie zrestartuje.

Na pierwszej instalacji to było zamierzone i słuszne — nie ma jeszcze konfiguracji, więc usługę startuje
okno po jej zapisaniu. **Przy aktualizacji jest inaczej:** konfiguracja już jest, a mimo to synchronizacja
cicho staje. U klienta oznacza to przestój do najbliższego restartu serwera, bez żadnego komunikatu.

Po aktualizacji trzeba wykonać:

```
Start-Service WU_Connector
```

Start usługi wywołuje cykl od razu, bez czekania na harmonogram.

**Do decyzji (zgłoszone 17.10.2026, nie zmieniam sam, bo to zmiana zachowania instalatora u klienta):**
dołożyć `Start="install"` do `ServiceControl`, tak żeby **aktualizacja** wznawiała usługę, a pierwsza
instalacja zachowywała się jak dotąd. Warunek `NOT Installed` albo `WIX_UPGRADE_DETECTED` rozróżnia
te dwa przypadki.

### 3.9 Jak potwierdzić, że connector wysyła NIP nagłówkiem

Oprócz licznika `sync_run.stats.nipWAdresie` u tenanta (zasada w sekcji 6) jest szybszy test lokalny,
niewymagający dostępu do logów serwera: `GET /sync/stan` **z** nagłówkiem `X-Nip` musi zwrócić
`firma` **różne od `null`**. Bez nagłówka tenant zwraca sam stan klucza i `firma: null`, więc gdyby
nagłówek wyciął pośrednik po drodze, odpowiedź byłaby nieodróżnialna od żądania bez NIP-u.

Potwierdzone 17.10.2026 po wdrożeniu 0.1.4: bez nagłówka `firma=null`, z nagłówkiem `firma=ok`
dla obu firm. Pomysł testu: sesja tenanta (Agent C).

## 4. Okno konfiguracji — stan po przebudowie

### Zakładka „R2 Płatnik” (dawniej „Instalacje i firmy”)

- Pasek nad listą (licznik instalacji, „Szukaj instalacji…”, „Zarządzaj kontami MSSQL…”) jest **przypięty poza obszarem przewijania** — rozwijanie instalacji go nie zasłania.
- Nagłówek karty instalacji w trzech rzędach: `[x] Aktywne | nazwa | strzałka` po prawej; pod spodem ścieżka (jedna linia, wielokropek); niżej znaczniki wersji (program / baza / biblioteka API). Znacznik „synchronizowana / wyłączona” usunięty — mówi o tym „Aktywne”.
- **„Aktywne” jest trójstanowe** i steruje wszystkimi firmami naraz: pełne = wszystkie, puste = żadna, pośrednie = część. **Nie ma osobnej flagi „instalacja włączona”** — stan wynika z pola `Aktywna` przy firmach, więc wyłączenie jednej z kilku firm **nie** wyłącza instalacji.
- Firmy jako lista: znacznik przy każdej (także przy jednej), nazwa, baza pod spodem, NIP po prawej, kreski między wierszami. Firma bez bazy wyszarzona ze znacznikiem „nie da się synchronizować”. W nagłówku sekcji licznik „2 z 3” i skrót „Zaznacz / Odznacz wszystkie” (widoczny dopiero przy kilku firmach zdatnych).
- **Zapis rozbity na sekcje**, każda ze swoim przyciskiem obok „Testuj…”: „Zapisz połączenie z bazą” i „Zapisz konto R2Płatnika” (albo „Zapisz konto i bibliotekę”, gdy instalacja nie ma biblioteki API). Zapis sekcji bierze wpis z konfiguracji i nadpisuje **wyłącznie** pola tej sekcji (`EkranInstalacje.TylkoSekcja`) — niedokończone zmiany w drugiej sekcji nie jadą razem.
- Rozwinięcie instalacji **nie przewija już listy do góry**: liczone jest przesunięcie karty po zwinięciu pozostałych i suwak idzie o tyle samo. Przebudowa listy po zapisie zachowuje przewinięcie i ponownie rozwija tę samą instalację.

### Okno testów (`DialogPracy` + `Kolowrotek` + `IkonaStanu`)

- „Testuj SQL” i „Testuj logowanie” pokazują wynik **w oknie modalnym**. W trakcie kręci się kołowrotek (okna nie da się wtedy zamknąć — zadanie i tak by nie wróciło), po zakończeniu kołowrotek zamienia się w miejscu w ikonę stanu, obok stoi wynik.
- Treść wyniku: „Powodzenie” albo **sama treść błędu** (np. „Błędne hasło użytkownika &lt;login&gt;”). Bez numerów kont, wersji bibliotek i czasów — **na wyraźne życzenie użytkownika**.
- Komunikaty wyników zniknęły z kart — wynik jest w jednym miejscu.
  *Uwaga dla przyszłych sesji: wcześniejsza łatka przewijająca wynik do widoku w karcie (`NaWidok`, commit `ba76e42`) jest tym rozwiązaniem zastąpiona i słusznie jej w kodzie nie ma — modal nie może schować się pod krawędzią okna.*

### Konta MSSQL

- Okno „Zarządzaj kontami MSSQL” pokazuje **tylko konta SQL Servera** — konta Windows (lokalne i domenowe) odfiltrowane, kolumna TYP usunięta, nazwa konta bierze zwolnione miejsce.
- Okno pracuje w zakresie **serwera**, nie jednej instalacji, i **nic nie zapisuje do konfiguracji connectora** — login i hasło użytkownik wpisuje potem w karcie instalacji.
- Konta connectora: `wu_*`, tylko odczyt. **Nie ruszać kont Optimy `CDN*`.**
- Connector potrzebuje: baza `_dane_N` — pełny odczyt; baza `_bin` — tylko `FIRM`, `VERSION` (SELECT) i `fn_Kod` (EXECUTE). Dlatego zaznaczanie `_bin` i `_dane_` jest sprzężone w obie strony.

### Wygląd — decyzja: WARIANT A

Rozwinięta (edytowana) instalacja ma **całe tło miętowe**, a sekcje leżą na niej jako **białe płyty
z delikatną zieloną ramką**; granicą jest mięta widoczna między płytami — żadnych kresek ani pasków
przez całą szerokość. Zwinięte instalacje zostają białe. Lista firm nie rysuje własnej ramki (ma ją płyta),
zostały same kreski między firmami.

W kodzie: `Karta.OdstepCzesci` (12 px wcięcia i odstępu), `Blok.Plyta`.
**Nieużyte warianty zostały w kodzie:** `Uklad.Akcent` (zielona kreska) i `Uklad.PasekTytulu` —
powrót to jedna zmiana w konstruktorze `Blok`.

Dodatkowo: pola sekcji na pełną szerokość (`Blok.PolaRowno`) — Serwer / Login / Hasło w jednym rzędzie;
okno poszerzone do **1220 px**, żeby kolumna treści miała projektowe 840 px (wcześniej dostawała 818
i opisy sięgały ramki); `Blok` przelicza swój opis przy zmianie szerokości.

Okno ma **stały rozmiar**: bez maksymalizacji i bez zmiany rozmiaru (`FixedSingle`, `WM_GETMINMAXINFO`, `SC_MAXIMIZE`) — przy rozciąganiu układ wyglądał źle.

### Model podnoszenia uprawnień

Nie ma przycisku „Otwórz jako administrator”. UAC wypada **dopiero po zatwierdzeniu akcji**, która go
wymaga: okno przekazuje podniesionemu procesowi sekcję konfiguracji (`ZmianaSekcji`) przez
`--konfiguruj --zastosuj <plik> --ekran <nazwa>`, a operacje na usłudze przez `--usluga start|stop`.

### Diagnostyka okna

- Dziennik okna: **`%LOCALAPPDATA%\WU_Connector\okno-RRRR-MM-DD.log`** — puls co minutę (uchwyty GDI/USER, pamięć, liczba okien), nieobsłużone wyjątki z obu wątków, wpisy START / KONIEC. **(sprawdzone — plik `okno-2026-10-16.log` istnieje)**
- Pierwotnie pisał do `ProgramData`, ale użytkownik bez uprawnień nie może tam pisać i dziennik **milcząco się nie tworzył**.
- Zawieszanie okna użytkownik uznał za temat nieaktualny; diagnostyka zbierze dane, gdyby wróciło.
- `CA1416` usunięte: odcisk sekretu (samo SHA-256) wyprowadzony z windowsowego `PlikKonfiguracji` do osobnej klasy `Konfiguracja.Odcisk`. Oznaczenie całego `Core` jako windowsowego odrzucone — przenosiło ostrzeżenia na narzędzia.
- Ramka karty nie jest już przerywana przez linie sekcji i stopki zapisu (`Uklad.Linia` wcięta o piksel, `PasekZapisu` maluje tło sam, `Karta.Przelicz` zostawia piksel na dolną kreskę).

## 5. Co czeka

Kolejność wedle wagi, nie chronologii. **Żadne z tych zadań nie jest rozpoczęte.**

1. ~~Unikalny numer każdej paczki~~ — **zrobione 17.10.2026**, patrz 3.1: licznik w `build\wersja.txt` podbijany przez `publikuj.ps1`, jedno źródło dla trzech projektów, MSI i protokołu.
2. ~~Test ścieżki aktualizacji~~ — **zrobione 17.10.2026**, dwa razy na żywo (0.1.2 → 0.1.3 → 0.1.4). Patrz 3.8: binaria się podmieniają, żadnego „Won't Overwrite", konfiguracja i sekrety w `ProgramData` nietknięte, jeden wpis w „Programach i funkcjach". **Ale wyszło przy tym, że instalator nie uruchamia usługi — patrz 3.8, wymaga decyzji.**
3. **Wymiana deweloperskiego klucza manifestu** (22.23) — blokujące dla wdrożenia.
4. **Certyfikat code signing** dla EXE i MSI — patrz 3.3; domyka też punkt 3.
5. **Grupa minimalna uprawnień konta R2** (22.16): konto techniczne connectora siedzi wciąż w grupie „Kadry — wprowadzanie”. Po utworzeniu grupy: przepięcie konta i testy zapisu / edycji / usunięcia cudzego wpisu (`F6A`/`F8A`) / braku uprawnień. R2 pozwala grupę wyeksportować i zaimportować → robimy raz i powielamy u klientów.
6. **Zapis dzisiejszych ustaleń w `C:\Symfonia\_projekt-urlopy` i `CLAUDE.md`** (pułapka wersji MSI, dziennik okna w LOCALAPPDATA, kopiowanie workera, nowy wygląd) — niezrobione. `CLAUDE.md` zmieniamy tylko na wyraźne polecenie użytkownika.
7. **Sprawdzenie okna na czystej maszynie dla użytkownika bez praw administratora** — tutaj plik konfiguracji istniał wcześniej i mógł odziedziczyć starsze ACL.
8. **Protokół tenanta: tura A3–A7 i etap B** — czekają na sygnał nadzoru. **Przed etapem B usunąć z R2 wpisy testowe `urlop:12315` i `urlop:12316`.**

## 6. Zasady, o których łatwo zapomnieć

- Odczyt **wyłącznie SQL**, zapis urlopu **wyłącznie API**. Nigdy `INSERT`/`UPDATE` na tabelach kadrowo-płacowych.
- `AccessViolationException` z API **nie da się przechwycić** w .NET 4+ → osobny proces roboczy jest **koniecznością**, nie wygodą.
- Po nieudanym `TablePost` **obowiązkowo `TableCancel`**. Na koniec `CloseAllTables` → `LogoffUser` → `FreeAllDataModules` → `ApiFinalize`.
- To maszyna **testowa z kopią bazy produkcyjnej**, z prawdziwymi danymi osobowymi. Dane wychodzą **tylko** kanałem `tenant.wezurlop.pl`. PESEL, adresy, kwoty i kody chorób **nigdy**. Gdyby pojawiła się wątpliwość, że to jednak produkcja — **zatrzymać się i zapytać**.
- **Do repozytorium nie trafiają prawdziwe dane osobowe ani dane klienta** — zasada projektu z 17.10.2026. Dotyczy nazwisk, sald, nieobecności, a **tak samo** NIP-ów, nazw firm, nazw hostów i loginów. W kodzie, testach i dokumentacji stoją wartości **jawnie fikcyjne** (`ALFA`, `BETA`, `SERWER-DEMO`, `WNIOSKI_DEMO`, NIP-y `1234567009` i `9876543210`) albo opis bez wartości. Wartości prawdziwe żyją w konfiguracji i w zmiennych środowiskowych: `WU_R2_HASLO`, `WU_TEST_SQL_SERWER`, `WU_TEST_SQL_BAZY`.
- **NIP w dzienniku jest maskowany (`Nip.DoLogu`), ale to NIE anonimizacja** — i tak tego nie nazywamy. Zostają trzy ostatnie cyfry, więc kandydatów jest rzędu 10⁶: dużo, ale skończenie, a NIP-y stoją w publicznych rejestrach. Cel jest węższy i taki wystarcza: **dziennik nie ma być listą NIP-ów gotową do skopiowania.** Numeru, który ma naprawdę nie wyjść, do dziennika nie wpisujemy wcale. Ta sama zasada obowiązuje po stronie tenanta i tak samo jest tam nazwana.
- **NIP firmy idzie do tenanta nagłówkiem `X-Nip`, nigdy w adresie** (`?nip=` zapisywał się jawnie w logu dostępu serwera WWW). Przy POST `nip` zostaje w ciele — tam problemu nie ma. **Jak sprawdzić, że connector naprawdę przeszedł na nagłówek:** tenant liczy żądania ze starą drogą w `sync_run.stats.nipWAdresie` (mierzone przy `zadania`, czyli raz na cykl) — po wdrożeniu musi tam stać **0** w każdym przebiegu. To dowód mocniejszy niż brak `nip=` w logu dostępu, bo nie zależy od tego, czy ktoś patrzył na log we właściwej godzinie. Brak pola oznacza przebieg sprzed tej zmiany.
