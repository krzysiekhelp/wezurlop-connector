# Buduje instalator connectora: artefakty\WU_Connector-<wersja>.msi
#
#   .\build\publikuj.ps1 [-Wersja 0.1.0]
#
# Kroki: testy -> publikacja uslugi i okna (win-x64, z wlasnym .NET) -> proces roboczy (net48 x86) do worker\ -> MSI.
param([string] $Wersja = '0.1.0', [switch] $BezTestow)
$ErrorActionPreference = 'Stop'
$korzen = Split-Path $PSScriptRoot -Parent
Set-Location $korzen
$app = Join-Path $korzen 'artefakty\app'

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
