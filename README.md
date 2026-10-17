# WU Connector — wezurlop ↔ Symfonia R2Płatnik

Connector działa w sieci klienta, na serwerze z R2Płatnikiem: czyta dane kadrowe z MSSQL, wysyła zmiany do aplikacji wezurlop klienta, pobiera zaakceptowane wnioski i zapisuje je do karty urlopowej R2 przez `R2PApi10.dll`.

Dokumentacja projektu: `C:\Symfonia\_projekt-urlopy\` — przede wszystkim `ARCHITEKTURA-agenta.md` (v0.2), `API-TENANTA-v1.md`, `PRZYKLADY-API-TENANTA.md`, `KSZTALT-DANYCH-R2.md`.

## Projekty

| Projekt | Platforma | Rola |
|---|---|---|
| `src/WuConnector.Contracts` | netstandard2.0 | wspólne typy: zadanie i wynik procesu roboczego, klasyfikacja komunikatów R2, walidacja |
| `src/WuConnector.Worker` | **net48, x86** | proces roboczy — **jedyny, który ładuje R2PApi10.dll**; jedno zadanie na uruchomienie |
| `tests/WuConnector.Tests` | net10.0 | testy jednostkowe |

Usługa (net10) i okno konfiguracji (WinForms net10) — kolejne kroki (`ARCHITEKTURA-agenta.md` §10).

## Proces roboczy

```
stdin : {"operacja":"zapis","katalogBin":"…\\BIN\\","login":"…","haslo":"…","firmIde":1,
         "pracownikId":207,"zatrudnienieId":207,"requestId":"wu-…","kod":0,
         "dataOd":"2026-11-30","dataDo":"2026-12-01"}
stdout: WU-WYNIK:{"status":"zapisanoZOstrzezeniem","kod":"OSTRZ_PRZELOM_MIESIACA","zapisy":[…],…}
```

Operacje: `zapis`, `usuniecie`, `test`. Wynik zawsze w linii `WU-WYNIK:`; kod wyjścia jest kanałem pomocniczym.

Zasady wbudowane w kod (uzasadnienia w dokumentacji):
- Unicode + 1-bajtowy `Boolean` w deklaracjach biblioteki,
- zamknięcie: `CloseAllTables → LogoffUser → FreeAllDataModules → ApiFinalize`, potem `TerminateProcess`,
- `TableCancel` po każdym nieudanym `TablePost`,
- „uwzględnij na kolejnej liście wynagrodzeń" zawsze wyłączone,
- przed zapisem sprawdzenie, czy wpisy z tym `requestId` już istnieją (idempotencja),
- wiersze limitu (`L = 1`) nigdy nie są usuwane.

## Budowanie i testy

```
dotnet build WuConnector.slnx -c Release
dotnet test tests\WuConnector.Tests -c Release
```

Test na żywo (kopia bazy): `tools\uruchom-worker.ps1` — hasło konta R2 w `$env:WU_R2_HASLO`.
