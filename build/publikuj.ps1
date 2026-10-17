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
param([string] $Wersja = '', [switch] $BezTestow, [switch] $BezPodbicia)
$ErrorActionPreference = 'Stop'
$korzen = Split-Path $PSScriptRoot -Parent
Set-Location $korzen
$app = Join-Path $korzen 'artefakty\app'
$plikWersji = Join-Path $PSScriptRoot 'wersja.txt'

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

Krok 'Instalator MSI'
dotnet build installer\WuConnector.Instalator.wixproj -c Release -nologo -v q -p:Wersja=$Wersja; Sprawdz

$msi = Join-Path $korzen "artefakty\WU_Connector-$Wersja.msi"
$rozmiar = [math]::Round((Get-Item $msi).Length / 1MB, 1)
$plikow = (Get-ChildItem $app -Recurse -File).Count
Write-Host "`nGotowe: $msi ($rozmiar MB, $plikow plikow aplikacji)" -ForegroundColor Green

if ($Wersja -ne $biezaca) {
    Write-Host "Numer podbity $biezaca -> $Wersja. ZACOMMITUJ build\wersja.txt:" -ForegroundColor Yellow
    Write-Host "    git add build\wersja.txt" -ForegroundColor Yellow
    Write-Host "    git commit -m 'Wersja paczki $Wersja'" -ForegroundColor Yellow
}
