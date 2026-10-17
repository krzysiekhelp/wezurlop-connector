# Buduje instalator connectora: artefakty\WU_Connector-<wersja>.msi
#
#   .\build\publikuj.ps1                  # podbija trzecie pole wersji (0.1.N -> 0.1.N+1) i buduje
#   .\build\publikuj.ps1 -BezPodbicia     # buduje jeszcze raz biezacy numer (nie wydawac tak paczki!)
#   .\build\publikuj.ps1 -Wersja 0.2.0    # wymusza numer, np. przy zmianie minor; zapisuje go do wersja.txt
#   .\build\publikuj.ps1 -BezTestow       # pomija testy (tylko do szybkiego sprawdzenia paczki)
#
# Kroki: testy -> publikacja uslugi i okna (win-x64, z wlasnym .NET) -> proces roboczy (net48 x86) do worker\ -> MSI.
#
# ------------------------------------------------------------------------------------------------
# NUMER WERSJI - dlaczego licznik w pliku repozytorium, a nie z liczby commitow
#
# Kazda paczka MUSI miec inny, rosnacy numer. MSI nie nadpisuje plikow o tej samej wersji
# ("Won't Overwrite; Existing file is of an equal version"), wiec aktualizacja paczka o numerze
# rownym zainstalowanemu CICHO nie zmienia binariow - zlapalismy to przy pierwszym wdrozeniu.
#
# Numer trzymamy w `build\wersja.txt`, ktory jest w repozytorium. Powody:
#   1. Jedzie z repozytorium, wiec po klonie licznik NIE wraca do zera i nie wydamy drugi raz numeru,
#      ktory stoi juz u klienta. Licznik w pliku lokalnym albo w rejestrze tego nie gwarantuje.
#   2. Liczba commitow (`git rev-list --count HEAD`) te wlasnosc ma, ale lamie zwiazek numer <-> zawartosc:
#      build z brudnego drzewa albo z galezi dostaje numer, ktory moze juz opisywac inna zawartosc.
#      Dla MSI to dokladnie ten blad, ktory naprawiamy. Licznik podbijamy raz na ZBUDOWANA PACZKE,
#      nie raz na commit, wiec kazda paczka ma swoj numer.
#   3. Liczba commitow nie jest monotoniczna miedzy galeziami i po scaleniach, a MajorUpgrade wymaga,
#      zeby numer rosl. Pole w pliku rosnie zawsze, niezaleznie od ksztaltu historii.
#
# Po zbudowaniu paczki ZACOMMITUJ `build\wersja.txt` - inaczej nastepny build na innej kopii repozytorium
# wyda ten sam numer. Skrypt przypomina o tym na koncu.
# ------------------------------------------------------------------------------------------------
param([string] $Wersja = '', [switch] $BezTestow, [switch] $BezPodbicia, [switch] $BezTestowMagazynu)
$ErrorActionPreference = 'Stop'
$korzen = Split-Path $PSScriptRoot -Parent
Set-Location $korzen
$app = Join-Path $korzen 'artefakty\app'
$plikWersji = Join-Path $PSScriptRoot 'wersja.txt'

# --- BRAMKA: sciezka weryfikacji podpisu manifestu bibliotek R2 musi byc przetestowana
#
# Sprawdzana PRZED podbiciem licznika: odmowa nie moze zuzyc numeru wersji, bo po kilku nieudanych
# probach numery zaczelyby przeskakiwac bez istniejacych paczek.
#
# Testy MagazynBibliotekTesty - jedyne, ktore sprawdzaja weryfikacje podpisu manifestu i podpisu
# producenta biblioteki - potrzebuja prawdziwych plikow R2PApi10.dll. Bez zmiennych srodowiskowych
# pomijaja sie CICHO, a zestaw dalej raportuje same sukcesy: liczba testow jest identyczna, rozni je
# tylko czas (ok. 2 s kontra ok. 16 s). Tak 17.10.2026 powstala paczka, w ktorej ta sciezka nie byla
# sprawdzona wcale (docs\stan.md 3.10). Dlatego skrypt ODMAWIA budowania.
$OstrzezenieTestow = $null
if ($BezTestow) {
    $OstrzezenieTestow = 'Testy POMINIETE W CALOSCI (-BezTestow).'
    Write-Host "UWAGA: $OstrzezenieTestow" -ForegroundColor Red
}
else {
    $brak = @()
    foreach ($z in 'WU_TEST_DLL_NOWA', 'WU_TEST_DLL_STARA') {
        $w = [Environment]::GetEnvironmentVariable($z)
        if ([string]::IsNullOrWhiteSpace($w)) { $brak += "$z nie jest ustawione" }
        elseif (-not (Test-Path $w))          { $brak += "$z wskazuje nieistniejacy plik: $w" }
    }
    if ($brak.Count -gt 0 -and -not $BezTestowMagazynu) {
        throw ("Nie zbuduje paczki: testy weryfikacji podpisu manifestu nie moga sie wykonac." +
               "`n  " + ($brak -join "`n  ") +
               "`n`nUstaw zmienne (sciezki do bibliotek R2 na tej maszynie):" +
               "`n  `$env:WU_TEST_DLL_NOWA  = '<instalacja R2>\BIN\R2PApi10.dll'          # wersja pliku 10.1.0.33" +
               "`n  `$env:WU_TEST_DLL_STARA = '<starsza instalacja R2>\BIN\R2PApi10.dll'  # wersja pliku 10.1.0.32" +
               "`n`nAlbo swiadomie pomin ta kontrole: -BezTestowMagazynu (zostawia slad w INFO.txt paczki).")
    }
    if ($brak.Count -gt 0) {
        $OstrzezenieTestow = 'Testy weryfikacji podpisu manifestu POMINIETE (-BezTestowMagazynu): ' + ($brak -join '; ')
        Write-Host "UWAGA: $OstrzezenieTestow" -ForegroundColor Red
    }
    # WU_TEST_SQL* nie blokuje budowania: testy odczytu bazy nie naleza do sciezki bezpieczenstwa.
    if ([Environment]::GetEnvironmentVariable('WU_TEST_SQL') -ne '1') {
        Write-Host 'Informacja: WU_TEST_SQL nie jest ustawione - testy na prawdziwym serwerze SQL pominiete.' -ForegroundColor DarkYellow
    }
}

# --- numer wersji: z parametru albo z licznika
$biezaca = (Get-Content $plikWersji -Raw).Trim()
if (-not ($biezaca -match '^\d+\.\d+\.\d+$')) { throw "build\wersja.txt ma niepoprawna tresc: '$biezaca' (oczekiwane x.y.z)" }

if ($Wersja -eq '') {
    if ($BezPodbicia) { $Wersja = $biezaca }
    else {
        $p = $biezaca.Split('.')
        $Wersja = "$($p[0]).$($p[1]).$([int]$p[2] + 1)"
    }
}
if (-not ($Wersja -match '^\d+\.\d+\.\d+$')) { throw "Wersja '$Wersja' nie ma postaci x.y.z" }

if ($Wersja -ne $biezaca) {
    Set-Content $plikWersji $Wersja -Encoding utf8 -NoNewline
    Add-Content $plikWersji "`n" -Encoding utf8 -NoNewline
}
Write-Host "Wersja paczki: $Wersja" -ForegroundColor Yellow
if ($BezPodbicia) { Write-Host "UWAGA: numer nie zostal podbity - takiej paczki nie wydawaj klientowi." -ForegroundColor Yellow }

function Krok($t) { Write-Host "`n=== $t" -ForegroundColor Cyan }
function Sprawdz { if ($LASTEXITCODE -ne 0) { throw "Krok nieudany (kod $LASTEXITCODE)" } }

if (-not $BezTestow) {
    Krok 'Testy'
    dotnet test tests\WuConnector.Tests -c Release -nologo -v q; Sprawdz
}

Krok 'Czyszczenie artefakty\app'
if ([IO.Directory]::Exists($app)) { [IO.Directory]::Delete($app, $true) }
[IO.Directory]::CreateDirectory($app) | Out-Null

$wspolne = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$Wersja", '-nologo', '-o', $app)

Krok 'Usluga (win-x64, z wlasnym .NET)'
dotnet publish src\WuConnector.Service @wspolne; Sprawdz

Krok 'Okno i zasobnik (win-x64, z wlasnym .NET)'
dotnet publish src\WuConnector.Okno @wspolne; Sprawdz

Krok 'Proces roboczy (.NET Framework 4.8, x86) -> worker\'
dotnet build src\WuConnector.Worker -c Release -nologo -v q -p:Version=$Wersja; Sprawdz
$worker = Join-Path $app 'worker'
[IO.Directory]::CreateDirectory($worker) | Out-Null
Get-ChildItem src\WuConnector.Worker\bin\Release\net48 -File | Where-Object Extension -ne '.pdb' | Copy-Item -Destination $worker

# INFO.txt jedzie W PACZCE (ComponentGroup "Aplikacja" zbiera caly artefakty\app), wiec slad o tym,
# czy sciezka podpisu byla sprawdzona, dociera az do katalogu instalacji na maszynie klienta.
Krok 'INFO.txt paczki'
$stanTestow = if ($OstrzezenieTestow) { $OstrzezenieTestow } else { 'Testy przeszly, w tym weryfikacja podpisu manifestu bibliotek R2.' }
@(
    "WU_Connector $Wersja"
    "Zbudowano: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "Maszyna:   $env:COMPUTERNAME"
    "Testy:     $stanTestow"
) | Set-Content (Join-Path $app 'INFO.txt') -Encoding utf8
Write-Host $stanTestow -ForegroundColor $(if ($OstrzezenieTestow) { 'Red' } else { 'DarkGray' })

Krok 'Instalator MSI'
# Czyszczenie obj: po nieudanym buildzie (np. ICE24 przy numerze wersji z etykieta) katalog zostaje
# w stanie, w ktorym kolejny build NIE zglasza bledu WiX, ale NIE produkuje pliku MSI - jedynym
# objawem jest MSB3030 o braku pliku do skopiowania. Patrz docs\stan.md 3.11.
Remove-Item installer\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build installer\WuConnector.Instalator.wixproj -c Release -nologo -v q -p:Wersja=$Wersja; Sprawdz

# Kontrola, ze paczka naprawde powstala i ma numer, o ktory prosilismy - `dotnet build` potrafi
# zakonczyc sie sukcesem, nie zostawiajac MSI (patrz wyzej).
$msi = Join-Path $korzen "artefakty\WU_Connector-$Wersja.msi"
if (-not (Test-Path $msi)) {
    throw ("Build instalatora zakonczyl sie bez bledu, ale pliku nie ma: $msi" +
           "`nUsun installer\obj i powtorz; jesli wraca, sprawdz numer wersji - MSI nie przyjmuje etykiet (np. 0.1.6-probna).")
}
# Wersje czytamy z tabeli Property gotowej paczki. Nieudany odczyt jest BLEDEM, nie ostrzezeniem:
# kontrola, ktora po cichu nic nie sprawdza, jest gorsza od jej braku - bo daje falszywa pewnosc.
$wMsi = $null
try {
    $inst = New-Object -ComObject WindowsInstaller.Installer
    $db = $inst.OpenDatabase($msi, 0)
    $widok = $db.OpenView("SELECT Value FROM Property WHERE Property='ProductVersion'")
    $widok.Execute()
    $rekord = $widok.Fetch()
    if ($rekord) { $wMsi = $rekord.StringData(1) }
    $widok.Close()
} catch { throw "Nie udalo sie odczytac ProductVersion z $msi : $($_.Exception.Message)" }
if ([string]::IsNullOrWhiteSpace($wMsi)) { throw "Nie udalo sie odczytac ProductVersion z $msi (tabela Property nie zwrocila wartosci)." }
if ($wMsi -ne $Wersja) { throw "MSI ma wersje $wMsi, a budowalismy $Wersja - paczka nie zgadza sie z licznikiem." }

$rozmiar = [math]::Round((Get-Item $msi).Length / 1MB, 1)
$plikow = (Get-ChildItem $app -Recurse -File).Count
Write-Host "`nGotowe: $msi ($rozmiar MB, $plikow plikow aplikacji, ProductVersion $wMsi)" -ForegroundColor Green

if ($Wersja -ne $biezaca) {
    Write-Host "Numer podbity $biezaca -> $Wersja. ZACOMMITUJ build\wersja.txt:" -ForegroundColor Yellow
    Write-Host "    git add build\wersja.txt" -ForegroundColor Yellow
    Write-Host "    git commit -m 'Wersja paczki $Wersja'" -ForegroundColor Yellow
}
