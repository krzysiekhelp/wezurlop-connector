// Atrapa serwera wezurlop dla connectora.
//   dotnet run --project tools\AtrapaTenanta -- --urls http://localhost:5080 --klucz <64hex> --nipy 1234567009,9876543210
// Bez --klucz uzywa klucza testowego (64 x 'a'). Stan tylko w pamieci.
using AtrapaTenanta;

string Arg(string nazwa, string dom) { int i = Array.IndexOf(args, nazwa); return i >= 0 && i + 1 < args.Length ? args[i + 1] : dom; }

// Uruchomiona samodzielnie — z limitami prawdziwego serwera (tura 7): 120 zadan/min, 10 zgloszen firmy/h.
var stan = new StanAtrapy { Klucz = Arg("--klucz", new string('a', 64)), LimitNaMinute = 120, LimitZgloszenNaGodzine = 10 };
foreach (var nip in Arg("--nipy", "1234567009,9876543210").Split(',', StringSplitOptions.RemoveEmptyEntries)) stan.DozwoloneNip.Add(nip.Trim());

var app = Serwer.Zbuduj(args, stan);

// --pliki <katalog>: udaje serwer bibliotek (wezurlop.pl/r2api) — manifest.json, manifest.json.sig, <wersja>/R2PApi10.dll
string pliki = Arg("--pliki", "");
if (pliki != "")
    app.MapGet("/r2api/{**p}", (string p) =>
    {
        string f = Path.GetFullPath(Path.Combine(pliki, p.Replace('/', '\\')));
        return f.StartsWith(Path.GetFullPath(pliki)) && File.Exists(f) ? Results.File(f, "application/octet-stream") : Results.NotFound();
    });
Console.WriteLine($"Atrapa wezurlop: NIP-y {string.Join(", ", stan.DozwoloneNip)}");
app.Run();
