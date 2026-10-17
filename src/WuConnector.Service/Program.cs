// Usluga Windows connectora wezurlop ↔ R2Platnik.
//
//   WuConnector.Service.exe                          — praca ciagla (jako usluga albo w konsoli)
//   WuConnector.Service.exe --raz                    — jeden cykl i koniec
//   WuConnector.Service.exe --konfiguracja <plik>    — inny plik niz domyslny w ProgramData
//   WuConnector.Service.exe --zaszyfruj <tekst>      — wartosc "dpapi:…" do pliku konfiguracji
//   WuConnector.Service.exe --nowy-sekret            — losowy sekret osobaId (raz przy wdrozeniu)
//   WuConnector.Service.exe --sekret-odcisk             — odcisk sekretu osobaId (do porownania po przenosinach)
//   WuConnector.Service.exe --sekret-eksport            — sekret osobaId jawnie, na stdout (przenosiny maszyny)
//   WuConnector.Service.exe --sekret-import <base64>    — wgranie sekretu z eksportu na nowej maszynie
//   WuConnector.Service.exe --konto-sql              — konto odczytu SQL wu_connector dla instalacji z konfiguracji
using WuConnector.Core.Konfiguracja;
using WuConnector.Service;

string? Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

if (Arg("--zaszyfruj") is string jawny) { Console.WriteLine(PlikKonfiguracji.Zaszyfruj(jawny)); return 0; }
if (args.Contains("--nowy-sekret")) { Console.WriteLine(PlikKonfiguracji.Zaszyfruj(PlikKonfiguracji.NowySekretOsoby())); return 0; }

string plik = Arg("--konfiguracja") ?? Path.Combine(PlikKonfiguracji.DomyslnyKatalog, "konfiguracja.json");
string katalog = Path.GetDirectoryName(Path.GetFullPath(plik))!;

// Przenosiny na inna maszyne: sekret osobaId jest zaszyfrowany kluczem TEJ maszyny, wiec sam plik konfiguracji
// nie wystarczy. --sekret-eksport pokazuje go jawnie (do przeniesienia), --sekret-import wgrywa na nowej maszynie.
// Odcisk (8 znakow z SHA-256) pozwala porownac sekret po obu stronach bez pokazywania go.
if (args.Contains("--sekret-odcisk") || args.Contains("--sekret-eksport"))
{
    var pk = PlikKonfiguracji.Wczytaj(plik);
    Console.Error.WriteLine($"odcisk sekretu osobaId: {PlikKonfiguracji.OdciskSekretu(pk.SekretOsoby)}");
    if (args.Contains("--sekret-eksport"))
    {
        Console.Error.WriteLine("UWAGA: ponizsza linia to sekret osobaId w postaci jawnej. Przenies go bezpiecznym kanalem,");
        Console.Error.WriteLine("nie zapisuj w repozytorium ani w dzienniku, a po przenosinach usun z miejsca posredniego.");
        Console.WriteLine(PlikKonfiguracji.Jawny(pk.SekretOsoby));
    }
    return 0;
}

if (Arg("--sekret-import") is string sekretJawny)
{
    byte[] bajty;
    try { bajty = Convert.FromBase64String(sekretJawny.Trim()); }
    catch (FormatException) { Console.Error.WriteLine("Sekret musi byc tekstem base64 z eksportu."); return 2; }
    if (bajty.Length < 16) { Console.Error.WriteLine("Sekret jest za krotki — to nie jest sekret osobaId."); return 2; }

    var pk = PlikKonfiguracji.Wczytaj(plik);
    string staryOdcisk;
    try { staryOdcisk = PlikKonfiguracji.OdciskSekretu(pk.SekretOsoby); }
    catch (Exception) { staryOdcisk = "nieczytelny (sekret z innej maszyny)"; }

    pk.SekretOsoby = PlikKonfiguracji.Zaszyfruj(sekretJawny.Trim());
    pk.Zapisz(plik);
    Console.WriteLine($"sekret osobaId zapisany; odcisk przed: {staryOdcisk}, po: {PlikKonfiguracji.OdciskSekretu(pk.SekretOsoby)}");
    return 0;
}

// --konto-sql: zaklada konto odczytu wu_<prefiks baz> dla kazdej instalacji z konfiguracji (logowanie Windows
// uruchamiajacego — musi byc administratorem SQL) i zapisuje haslo zaszyfrowane w konfiguracji.
if (args.Contains("--konto-sql"))
{
    var pk = PlikKonfiguracji.Wczytaj(plik);
    foreach (var inst in pk.Instalacje)
    {
        string haslo = (await KontoOdczytuSql.UtworzAsync(inst.SerwerSql, inst.BazaBin, inst.Firmy.Select(f => f.BazaDane)))!;   // zmienHaslo = true — zawsze nowe
        inst.LoginSql = KontoOdczytuSql.LoginDla(inst.BazaBin);
        inst.HasloSql = PlikKonfiguracji.Zaszyfruj(haslo);
        Console.WriteLine($"{inst.Nazwa}: konto {inst.LoginSql} gotowe (odczyt {inst.Firmy.Count + 1} baz), haslo zapisane zaszyfrowane");
    }
    pk.Zapisz(plik);
    return 0;
}
SluzbaConnectora.JedenCykl = args.Contains("--raz");

var ostrzezenia = new List<string>();
KonfiguracjaConnectora konfiguracja;
try { konfiguracja = PlikKonfiguracji.Wczytaj(plik).Rozwin(katalog, ostrzezenia); }
catch (Exception ex)
{
    Console.Error.WriteLine($"Konfiguracja nieczytelna ({plik}): {ex.Message}");
    return 2;
}

var b = Host.CreateApplicationBuilder(args);
b.Services.AddWindowsService(o => o.ServiceName = "WU_Connector");
b.Logging.ClearProviders();
b.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
b.Logging.AddProvider(new DziennikPlikowy(Path.Combine(katalog, "dziennik")));
b.Logging.AddFilter("Microsoft", LogLevel.Warning);
b.Services.AddSingleton(konfiguracja);
b.Services.AddSingleton<WuConnector.Core.Cykl.SygnalSynchronizacji>();
// Jako usluga Windows: wlasny cykl zycia z obsluga polecenia „synchronizuj teraz" (okno connectora).
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
    b.Services.AddSingleton<IHostLifetime, LifetimeUslugi>();
b.Services.AddHostedService<SluzbaConnectora>();

var host = b.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Konfiguracja");
foreach (var o in ostrzezenia) log.LogWarning("{Ostrzezenie}", o);
await host.RunAsync();
return 0;
