# Test sciezki aktualizacji MSI: instalacja N, potem N+1.
#
# URUCHAMIA ADMINISTRATOR, Z PODNIESIONYMI UPRAWNIENIAMI. Skrypt zatrzymuje usluge, odinstalowuje
# i instaluje produkt, wiec nie jest uruchamiany automatycznie przez nikogo i nie wchodzi do publikuj.ps1.
#
#   .\build\test-aktualizacji.ps1 -Stara 0.1.2 -Nowa 0.1.3
#   .\build\test-aktualizacji.ps1 -Stara 0.1.2 -Nowa 0.1.3 -BezOdinstalowania
#
# CO SPRAWDZA (to jest punkt 2 listy "co czeka" z docs\stan.md):
#   1. czy MSI N+1 faktycznie PODMIENIA binaria - porownanie SHA-256 przed i po,
#   2. czy konfiguracja i sekrety w C:\ProgramData\WU_Connector PRZEZYWAJA aktualizacje,
#   3. czy w "Programach i funkcjach" zostaje JEDEN wpis, nie dwa,
#   4. czy usluga wraca i chodzi po aktualizacji.
#
# DLACZEGO TO WAZNE: MSI nie nadpisuje plikow o tej samej wersji ("Won't Overwrite; Existing file is
# of an equal version"). Dopoki wszystkie paczki mialy numer 0.1.0, aktualizacja CICHO nie zmieniala
# binariow. Licznik w build\wersja.txt to naprawia - ten skrypt ma to potwierdzic na maszynie.
#
# UWAGA: test podmienia dzialajaca instalacje connectora. Na maszynie klienta go nie uruchamiamy.
param(
    [Parameter(Mandatory)] [string] $Stara,
    [Parameter(Mandatory)] [string] $Nowa,
    [switch] $BezOdinstalowania
)
$ErrorActionPreference = 'Stop'
$korzen = Split-Path $PSScriptRoot -Parent
$artefakty = Join-Path $korzen 'artefakty'
$cel = 'C:\Program Files\WU_Connector'
$dane = 'C:\ProgramData\WU_Connector'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Uruchom jako administrator - skrypt zatrzymuje usluge i instaluje produkt.'
}

$msiStara = Join-Path $artefakty "WU_Connector-$Stara.msi"
$msiNowa  = Join-Path $artefakty "WU_Connector-$Nowa.msi"
foreach ($m in $msiStara, $msiNowa) { if (-not (Test-Path $m)) { throw "Brak paczki: $m" } }
if ([version]$Nowa -le [version]$Stara) { throw "Nowa ($Nowa) musi byc wieksza od starej ($Stara)" }

function Krok($t) { Write-Host "`n=== $t" -ForegroundColor Cyan }
function Hash($p) { if (Test-Path $p) { (Get-FileHash $p -Algorithm SHA256).Hash.Substring(0,16) } else { '(brak pliku)' } }
function Wpisy {
    @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
      'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*') |
        ForEach-Object { Get-ItemProperty $_ -ErrorAction SilentlyContinue } |
        Where-Object DisplayName -eq 'WU_Connector' |
        Select-Object DisplayName, DisplayVersion, PSChildName
}
function Instaluj($msi, $co) {
    Krok "$co : $(Split-Path $msi -Leaf)"
    $log = Join-Path $env:TEMP "wu-msi-$co.log"
    $p = Start-Process msiexec -ArgumentList '/i', "`"$msi`"", '/qn', '/l*v', "`"$log`"" -Wait -PassThru
    Write-Host "msiexec kod wyjscia: $($p.ExitCode)   dziennik: $log"
    if ($p.ExitCode -ne 0) { throw "Instalacja nieudana (kod $($p.ExitCode)); szczegoly w $log" }
    # Slad bledu, ktory naprawiamy licznikiem wersji - przy rownych numerach pojawial sie w dzienniku MSI.
    $pominiete = Select-String -Path $log -Pattern "Won't Overwrite" -SimpleMatch -ErrorAction SilentlyContinue
    if ($pominiete) { Write-Host "UWAGA: MSI pominal $($pominiete.Count) plik(ow) jako 'Won't Overwrite'" -ForegroundColor Red }
    else { Write-Host "Zadnego pliku nie pominieto jako 'Won't Overwrite'" -ForegroundColor Green }
}

Krok 'Stan wyjsciowy'
Wpisy | Format-Table -AutoSize
Write-Host "konfiguracja istnieje: $(Test-Path (Join-Path $dane 'konfiguracja.json'))"

if (-not $BezOdinstalowania) {
    Krok 'Odinstalowanie tego, co jest (czysty punkt startu)'
    foreach ($w in Wpisy) {
        $p = Start-Process msiexec -ArgumentList '/x', $w.PSChildName, '/qn' -Wait -PassThru
        Write-Host "odinstalowano $($w.DisplayVersion), kod $($p.ExitCode)"
    }
}

# --- krok 1: wersja N
Instaluj $msiStara "N-$Stara"
$hashN = Hash (Join-Path $cel 'WuConnector.Core.dll')
$wersjaN = (Get-Item (Join-Path $cel 'WuConnector.Service.exe')).VersionInfo.FileVersion
Write-Host "Core.dll: $hashN    wersja uslugi: $wersjaN"

# Znacznik w ProgramData: sprawdzimy po aktualizacji, czy przezyl.
$znacznik = Join-Path $dane 'test-aktualizacji.txt'
Set-Content $znacznik "znacznik z testu aktualizacji $(Get-Date -Format s)" -Encoding utf8
$konfiguracjaPrzed = if (Test-Path (Join-Path $dane 'konfiguracja.json')) { Hash (Join-Path $dane 'konfiguracja.json') } else { '(brak)' }

# --- krok 2: wersja N+1 na wierzch (MajorUpgrade)
Instaluj $msiNowa "N1-$Nowa"
$hashN1 = Hash (Join-Path $cel 'WuConnector.Core.dll')
$wersjaN1 = (Get-Item (Join-Path $cel 'WuConnector.Service.exe')).VersionInfo.FileVersion

# --- wyniki
Krok 'WYNIKI'
$wpisy = @(Wpisy)
$u = Get-Service WU_Connector -ErrorAction SilentlyContinue

[pscustomobject][ordered]@{
    'binaria podmienione'        = $hashN -ne $hashN1
    'Core.dll przed'             = $hashN
    'Core.dll po'                = $hashN1
    'wersja uslugi przed'        = $wersjaN
    'wersja uslugi po'           = $wersjaN1
    'znacznik w ProgramData zyje' = Test-Path $znacznik
    'konfiguracja nietknieta'    = (Test-Path (Join-Path $dane 'konfiguracja.json')) -and
                                   ($konfiguracjaPrzed -eq (Hash (Join-Path $dane 'konfiguracja.json')))
    'wpisow w Programach'        = $wpisy.Count
    'wersja we wpisie'           = ($wpisy | ForEach-Object DisplayVersion) -join ', '
    'usluga'                     = if ($u) { "$($u.Status), start: $($u.StartType)" } else { 'BRAK USLUGI' }
    'worker\ plikow'             = (Get-ChildItem (Join-Path $cel 'worker') -File -ErrorAction SilentlyContinue | Measure-Object).Count
} | Format-List

Remove-Item $znacznik -ErrorAction SilentlyContinue

Krok 'Jak czytac wynik'
@'
Oczekiwane:
  binaria podmienione        = True        <- gdyby False, licznik wersji nie dziala
  wersja uslugi po           = numer N+1
  znacznik w ProgramData zyje = True       <- aktualizacja nie czysci danych
  konfiguracja nietknieta    = True        <- sekrety i ustawienia klienta zostaja
  wpisow w Programach        = 1           <- MajorUpgrade zastapil, nie dolozyl
  usluga                     = Running
Po tescie usluga chodzi w wersji N+1; zadnej dodatkowej akcji nie trzeba.
'@
