# Uruchamia proces roboczy z jednym zadaniem — do testow recznych na kopii bazy.
# Haslo konta R2 bierze ze zmiennej srodowiskowej WU_R2_HASLO (nigdy z repozytorium).
#
# Przyklady:
#   $env:WU_R2_HASLO = '...'
#   .\tools\uruchom-worker.ps1 -Operacja test
#   .\tools\uruchom-worker.ps1 -Operacja zapis -RequestId wu-TEST-1 -Od 2026-11-30 -Do 2026-12-01
#   .\tools\uruchom-worker.ps1 -Operacja usuniecie -Zapisy 12330,12331
param(
    [Parameter(Mandatory)] [string] $Operacja,
    [string] $KatalogBin = 'C:\Symfonia\NowyR2Platnik.10_alfa\BIN\',
    [string] $KatalogDll = '',
    [string] $Login = 'WNIOSKI_DEMO',
    [int] $FirmIde = 1,
    [int] $PracownikId = 207,
    [int] $ZatrudnienieId = 207,
    [string] $RequestId = '',
    [int] $Kod = 0,
    [string] $Od = '',
    [string] $Do = '',
    [int[]] $Zapisy = @(),
    [int] $LimitSekund = 60
)
$ErrorActionPreference = 'Stop'
if (-not $env:WU_R2_HASLO) { throw 'Ustaw $env:WU_R2_HASLO' }

$exe = Join-Path $PSScriptRoot '..\src\WuConnector.Worker\bin\Release\net48\WuConnector.Worker.exe'
$zadanie = [ordered]@{
    operacja = $Operacja; katalogBin = $KatalogBin; katalogDll = $KatalogDll
    login = $Login; haslo = $env:WU_R2_HASLO; firmIde = $FirmIde
    pracownikId = $PracownikId; zatrudnienieId = $ZatrudnienieId
    requestId = $RequestId; kod = $Kod; dataOd = $Od; dataDo = $Do; zapisy = @($Zapisy)
} | ConvertTo-Json -Compress

$psi = New-Object System.Diagnostics.ProcessStartInfo $exe
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8
$p = [Diagnostics.Process]::Start($psi)
$wejscie = New-Object IO.StreamWriter($p.StandardInput.BaseStream, (New-Object Text.UTF8Encoding $false))
$wejscie.Write($zadanie); $wejscie.Close()
$stdout = $p.StandardOutput.ReadToEndAsync()

$t0 = Get-Date
if (-not $p.WaitForExit($LimitSekund * 1000)) {
    $p.Kill()
    Write-Output "WATCHDOG: proces ubity po $LimitSekund s"
    exit 1
}
$ms = [int]((Get-Date) - $t0).TotalMilliseconds
$linia = ($stdout.Result -split "`r?`n") | Where-Object { $_.StartsWith('WU-WYNIK:') } | Select-Object -First 1
Write-Output ("kod wyjscia {0}, {1} ms" -f $p.ExitCode, $ms)
if ($linia) { $linia.Substring(9) | ConvertFrom-Json | ConvertTo-Json -Depth 5 } else { Write-Output 'BRAK LINII WYNIKU'; $stdout.Result }
