using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using WuConnector.Core.Konfiguracja;

namespace WuConnector.Okno;

/// <summary>Wspolny stan okna: plik konfiguracji, jego katalog (dziennik, stan.db) i wykryte instalacje.</summary>
public sealed class Kontekst
{
    public string SciezkaKonfiguracji { get; }
    public string Katalog => Path.GetDirectoryName(Path.GetFullPath(SciezkaKonfiguracji))!;
    public string KatalogDziennika => Path.Combine(Katalog, "dziennik");
    public PlikKonfiguracji? Plik { get; set; }
    public string? BladWczytania { get; }

    /// <summary>Wykryte instalacje z ekranu "Instalacje" — NIP-y firm potrzebne do testu polaczenia.</summary>
    public List<WykrytaInstalacja> Wykryte { get; } = new();


    public Kontekst(string sciezka)
    {
        SciezkaKonfiguracji = sciezka;
        if (File.Exists(sciezka))
            try { Plik = PlikKonfiguracji.Wczytaj(sciezka); }
            catch (Exception e) { BladWczytania = e.Message; }
    }

    /// <summary>
    /// Konfiguracje (z zaszyfrowanymi haslami) czyta i zapisuje tylko administrator — katalog ma ACL: SYSTEM + Administratorzy.
    /// Ikona w zasobniku dziala bez uprawnien (dziennik jest do odczytu dla uzytkownikow).
    /// </summary>
    public static bool Administrator { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Otwiera osobne okno konfiguracji z podniesionymi uprawnieniami (UAC). Zwraca false, gdy odmowiono.</summary>
    public bool UruchomJakoAdministrator(string dodatkoweArgumenty = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
                              $"--konfiguruj --konfiguracja \"{SciezkaKonfiguracji}\" {dodatkoweArgumenty}".TrimEnd())
            { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; /* uzytkownik anulowal UAC */ }
    }

    public enum WynikZapisu { Zapisano, Podniesiono, Odmowa }

    /// <summary>
    /// Zapis sekcji tam, gdzie uprawnien moze brakowac: z uprawnieniami zapisujemy od razu, bez nich
    /// przekazujemy gotowa sekcje oknu podniesionemu przez UAC. Okno pytamy o zgode dopiero tutaj —
    /// po zatwierdzeniu akcji, a nie przed wypelnieniem formularza.
    /// </summary>
    public (WynikZapisu Wynik, string Opis, bool Kompletna) ZapiszLubPodnies(ZmianaSekcji zmiana, string ekran)
    {
        if (Administrator)
        {
            var (opis, kompletna) = ZapiszSekcje(zmiana);
            return (WynikZapisu.Zapisano, opis, kompletna);
        }

        string przekaz = SciezkaPrzekazania();
        zmiana.Zapisz(przekaz);
        TylkoDlaMnie(przekaz);
        if (UruchomJakoAdministrator($"--zastosuj \"{przekaz}\" --ekran \"{ekran}\""))
            return (WynikZapisu.Podniesiono, "Zapisuje okno z uprawnieniami administratora.", false);

        try { File.Delete(przekaz); } catch (IOException) { }
        return (WynikZapisu.Odmowa, "Nie zapisano — odmówiono uprawnień administratora. Wpisane dane zostają w oknie.", false);
    }

    static string SciezkaPrzekazania()
        => Path.Combine(Path.GetTempPath(), $"wu-sekcja-{Guid.NewGuid():N}.json");

    /// <summary>
    /// Plik przekazania czyta wylacznie ten uzytkownik (i jego proces po UAC) — dziedziczone prawa katalogu
    /// tymczasowego zostaja odciete. Sekrety w srodku sa i tak zaszyfrowane DPAPI.
    /// </summary>
    static void TylkoDlaMnie(string sciezka)
    {
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(sciezka).SetAccessControl(acl);
    }

    /// <summary>Druga strona przekazania: podniesione okno nanosi sekcje na aktualna konfiguracje i kasuje plik.</summary>
    public string ZastosujPrzekazanie(string sciezka)
    {
        try
        {
            var zmiana = ZmianaSekcji.Wczytaj(sciezka);
            var (opis, _) = ZapiszSekcje(zmiana);
            return opis;
        }
        catch (Exception e) { return "Nie zapisano: " + e.Message; }
        finally { try { File.Delete(sciezka); } catch (IOException) { } }
    }


    // ---------------------------------------------------------- sekret osobaId (przenosiny maszyny)

    /// <summary>Odcisk sekretu osobaId (8 znakow) albo null, gdy konfiguracji nie ma.</summary>
    public string? OdciskSekretu()
    {
        if (Plik is null) return null;
        try { return PlikKonfiguracji.OdciskSekretu(Plik.SekretOsoby); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Sekret nieczytelny na tej maszynie — konfiguracja przyszla z innego komputera.
    /// Nowy sekret zmienilby identyfikatory osob, wiec okno ma to pokazac i podpowiedziec import.
    /// </summary>
    public bool SekretZInnejMaszyny()
    {
        if (Plik is null) return false;
        try { PlikKonfiguracji.Jawny(Plik.SekretOsoby); return false; }
        catch (Exception) { return true; }
    }

    /// <summary>Sekret w postaci jawnej — do przeniesienia na nowa maszyne.</summary>
    public string SekretJawny() => PlikKonfiguracji.Jawny(ZapewnijPlik().SekretOsoby);

    /// <summary>
    /// Wgranie sekretu z innej maszyny. Zwraca odcisk sprzed wgrania i wynik zapisu — bez uprawnien
    /// zapis przejmuje okno podniesione przez UAC, wiec nowego odcisku nie znamy jeszcze w tym procesie.
    /// </summary>
    public (string Przed, WynikZapisu Wynik, string Opis) ImportujSekret(string jawny)
    {
        byte[] bajty;
        try { bajty = Convert.FromBase64String(jawny.Trim()); }
        catch (FormatException) { throw new ArgumentException("To nie jest sekret z eksportu — oczekiwany tekst base64."); }
        if (bajty.Length < 16) throw new ArgumentException("Sekret jest za krótki — to nie jest sekret osobaId.");

        string przed;
        try { przed = OdciskSekretu() ?? "brak"; }
        catch (Exception) { przed = "nieczytelny"; }
        przed ??= "nieczytelny";

        var (wynik, opis, _) = ZapiszLubPodnies(ZmianaSekcji.DlaSekretu(PlikKonfiguracji.Zaszyfruj(jawny.Trim())), "Połączenie");
        return (przed, wynik, wynik == WynikZapisu.Zapisano
            ? $"Wgrano. Odcisk przed: {przed}, po: {PlikKonfiguracji.OdciskSekretu(Plik!.SekretOsoby)}."
            : opis);
    }

    public string SciezkaWorkera =>
        Plik?.SciezkaWorkera ?? Path.Combine(AppContext.BaseDirectory, "worker", "WuConnector.Worker.exe");

    public string SciezkaStanu => Plik?.SciezkaStanu ?? Path.Combine(Katalog, "stan.db");

    /// <summary>Nowa konfiguracja przy pierwszym zapisie — z losowym sekretem osobaId (raz na zawsze dla tej instalacji).</summary>
    static PlikKonfiguracji NowyPlik() => new()
    {
        AdresSerwera = "",
        KluczApi = "",
        SekretOsoby = PlikKonfiguracji.Zaszyfruj(PlikKonfiguracji.NowySekretOsoby()),
        Instalacje = new(),
    };

    public PlikKonfiguracji ZapewnijPlik() => Plik ??= NowyPlik();

    /// <summary>
    /// Zapis jednej sekcji: plik czytany na nowo z dysku, zmieniana tylko ta sekcja — niezapisane zmiany w innych
    /// sekcjach okna nie trafiaja do pliku. Usluge uruchamiamy ponownie tylko przy kompletnej konfiguracji.
    /// Zwraca opis wyniku do pokazania przy przycisku.
    /// </summary>
    public (string Opis, bool Kompletna) ZapiszSekcje(ZmianaSekcji zmiana)
    {
        var plik = File.Exists(SciezkaKonfiguracji) ? PlikKonfiguracji.Wczytaj(SciezkaKonfiguracji) : NowyPlik();
        zmiana.Zastosuj(plik);
        plik.Zapisz(SciezkaKonfiguracji);
        Plik = plik;
        Zapisano?.Invoke();

        var (blokujace, pominiete) = Braki();
        string uwaga = pominiete.Count > 0 ? $" Pominięte do czasu uzupełnienia — {string.Join("; ", pominiete)}." : "";
        return blokujace.Count > 0
            ? ($"Zapisano {DateTime.Now:HH:mm}. Do uruchomienia usługi brakuje: {string.Join(", ", blokujace)}.{uwaga}", false)
            : ($"Zapisano {DateTime.Now:HH:mm}. {UruchomPonownieUsluge()}{uwaga}", pominiete.Count == 0);
    }

    /// <summary>Po zapisie dowolnej sekcji — inne sekcje odswiezaja stan „w konfiguracji".</summary>
    public event Action? Zapisano;

    /// <summary>
    /// Blokujace: bez nich usluga nie ruszy (adres, klucz, choc jedna kompletna instalacja).
    /// Pominiete: niekompletne instalacje — usluga pracuje bez nich.
    /// </summary>
    public (List<string> Blokujace, List<string> Pominiete) Braki()
    {
        var b = new List<string>();
        var p = new List<string>();
        if (Plik is null) { b.Add("cała konfiguracja"); return (b, p); }
        if (string.IsNullOrWhiteSpace(Plik.AdresSerwera)) b.Add("adres wezurlop");
        if (string.IsNullOrEmpty(Plik.KluczApi)) b.Add("klucz instalacji");
        if (!Plik.Instalacje.Any(i => i.Firmy.Any(f => f.Aktywna) && PlikKonfiguracji.Braki(i) is null))
            b.Add("kompletna instalacja R2 z zaznaczoną firmą");
        foreach (var i in Plik.Instalacje)
            if (PlikKonfiguracji.Braki(i) is string bi) p.Add($"{i.Nazwa}: brak {bi}");
        return (b, p);
    }

    /// <summary>
    /// „Synchronizuj teraz": polecenie uzytkownika dla uslugi — przerywa oczekiwanie i uruchamia cykl.
    /// Nie wymaga uprawnien administratora. Zwraca opis wyniku.
    /// </summary>
    public static (bool Ok, string Opis) ZlecSynchronizacje()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(EkranStatus.NazwaUslugi);
            if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                return (false, "Usługa nie działa — najpierw ją uruchom.");
            sc.ExecuteCommand(WuConnector.Core.Cykl.SygnalSynchronizacji.Polecenie);
            return (true, $"Synchronizacja zlecona {DateTime.Now:HH:mm:ss} — wynik pojawi się w dzienniku za kilka sekund.");
        }
        catch (InvalidOperationException e) when (e.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 5 })
        {
            return (false, "Brak uprawnień do sterowania usługą.");
        }
        catch (InvalidOperationException) { return (false, "Usługa nie jest zainstalowana."); }
    }

    /// <summary>
    /// Start i zatrzymanie uslugi: z uprawnieniami od razu, bez nich przez krotkie podniesienie (UAC) —
    /// osobny proces robi jedna rzecz i konczy sie, okno zostaje tam, gdzie bylo.
    /// </summary>
    public (bool Ok, string Opis) SterujUsluga(bool uruchom)
    {
        if (!Administrator)
        {
            return UruchomJakoAdministrator($"--usluga {(uruchom ? "start" : "stop")}")
                ? (true, uruchom ? "Uruchamiam usługę…" : "Zatrzymuję usługę…")
                : (false, "Nie wykonano — odmówiono uprawnień administratora.");
        }
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(EkranStatus.NazwaUslugi);
            if (uruchom) sc.Start(); else sc.Stop();
            sc.WaitForStatus(uruchom ? System.ServiceProcess.ServiceControllerStatus.Running
                                     : System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            return (true, uruchom ? "Usługa uruchomiona." : "Usługa zatrzymana.");
        }
        catch (InvalidOperationException) { return (false, "Usługa nie jest zainstalowana."); }
        catch (System.ServiceProcess.TimeoutException) { return (false, "Usługa nie odpowiedziała — sprawdź Dziennik."); }
    }

    /// <summary>Usluga czyta konfiguracje przy starcie — po zapisie uruchamiamy ja ponownie (albo pierwszy raz).</summary>
    static string UruchomPonownieUsluge()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(EkranStatus.NazwaUslugi);
            if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            sc.Start();
            sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            return "Usługa uruchomiona z nową konfiguracją.";
        }
        catch (InvalidOperationException) { return "Usługa nie jest zainstalowana — konfiguracja czeka na instalację."; }
        catch (System.ServiceProcess.TimeoutException) { return "Usługa nie odpowiedziała — sprawdź Dziennik."; }
    }

    /// <summary>Konfiguracja dla Core (odszyfrowana) albo null, gdy niekompletna.</summary>
    public KonfiguracjaConnectora? Rozwinieta()
    {
        if (Plik is null) return null;
        try { return Plik.Rozwin(Katalog, new List<string>()); } catch { return null; }
    }
}
