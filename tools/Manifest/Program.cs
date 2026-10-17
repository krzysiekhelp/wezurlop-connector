// Narzedzie wydawcy: klucz podpisu i manifest bibliotek R2 dla serwera bibliotek (wezurlop.pl).
//
//   Manifest generuj-klucz <katalog>
//       → klucz-prywatny.pem (TRZYMAC POZA REPOZYTORIUM I POZA SERWEREM WWW) + klucz-publiczny.pem
//         (tresc publicznego wklejamy do src/WuConnector.Core/Biblioteki/KluczManifestu.cs)
//
//   Manifest podpisz <katalog-bibliotek> <klucz-prywatny.pem>
//       katalog-bibliotek: podkatalogi <wersja-pliku>\R2PApi10.dll, np. 10.1.0.33\R2PApi10.dll
//       → manifest.json + manifest.json.sig w tym samym katalogu (gotowe do wystawienia 1:1 na serwerze)
//
// Kazda biblioteka przed wpisaniem do manifestu musi miec wazny podpis producenta (Symfonia, KRS 0000228956).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WuConnector.Core.Biblioteki;

if (args.Length >= 2 && args[0] == "generuj-klucz")
{
    Directory.CreateDirectory(args[1]);
    using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    string prywatny = Path.Combine(args[1], "klucz-prywatny.pem");
    if (File.Exists(prywatny)) { Console.Error.WriteLine("Klucz juz istnieje — nie nadpisuje: " + prywatny); return 1; }
    File.WriteAllText(prywatny, ec.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(Path.Combine(args[1], "klucz-publiczny.pem"), ec.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine(ec.ExportSubjectPublicKeyInfoPem());
    return 0;
}

if (args.Length >= 3 && args[0] == "podpisz")
{
    string katalog = args[1];
    var pozycje = new List<PozycjaBiblioteki>();
    foreach (var k in Directory.GetDirectories(katalog).OrderBy(x => x))
    {
        string plik = Path.Combine(k, MagazynBibliotek.NazwaPliku);
        if (!File.Exists(plik)) continue;
        var p = PodpisProducenta.Sprawdz(plik);
        if (!p.Poprawny) { Console.Error.WriteLine($"ODRZUCONA {plik}: {p.Blad}"); return 2; }

        string wPliku = FileVersionInfo.GetVersionInfo(plik).FileVersion!;
        var v = Version.Parse(wPliku);
        // Baza 10.10.33 ↔ plik 10.1.0.33 (numer glowny, dziesiatki podwersji, kompilacja).
        string wBazy = $"{v.Major}.{v.Minor * 10 + v.Build}.{v.Revision}";
        string sha;
        using (var s = File.OpenRead(plik)) sha = Convert.ToHexString(SHA256.HashData(s));
        pozycje.Add(new PozycjaBiblioteki(wBazy, wPliku, sha, new FileInfo(plik).Length, $"{Path.GetFileName(k)}/{MagazynBibliotek.NazwaPliku}"));
        Console.WriteLine($"  {wPliku}  baza {wBazy}  {sha[..16]}…  podpis: {p.Podmiot![..30]}…");
    }

    var m = new ManifestBibliotek(1, DateTimeOffset.Now, pozycje);
    byte[] tresc = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m, ManifestBibliotek.Json));
    File.WriteAllBytes(Path.Combine(katalog, "manifest.json"), tresc);
    File.WriteAllText(Path.Combine(katalog, "manifest.json.sig"), ManifestBibliotek.Podpisz(tresc, File.ReadAllText(args[2])));
    Console.WriteLine($"manifest.json: {pozycje.Count} bibliotek, podpisany");
    return 0;
}

Console.Error.WriteLine("Uzycie: Manifest generuj-klucz <katalog> | Manifest podpisz <katalog-bibliotek> <klucz-prywatny.pem>");
return 2;
