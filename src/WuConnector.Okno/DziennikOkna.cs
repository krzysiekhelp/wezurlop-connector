using System.Runtime.InteropServices;
using System.Text;

namespace WuConnector.Okno;

/// <summary>
/// Wlasny slad okna, osobno od dziennika uslugi. Powod: 07.10 i 13.10 Windows zamknal okno jako „Application
/// Hang”, a jedyne, co zostalo, to wpis w dzienniku zdarzen systemu — bez sladu, co okno wtedy robilo.
/// Wyciek uchwytow GDI/USER zostal naprawiony i zmierzony, ale druga przyczyna jest nadal nieznana.
///
/// Zapisujemy wiec trzy rzeczy, wszystkie tanie:
///   • bicie serca co minute z liczba uchwytow i pamiecia — rosnaca krzywa od razu wskaze wyciek,
///   • kazdy nieobslugiwany wyjatek (watek interfejsu i cala domena),
///   • start i koniec procesu, zeby bylo widac, czy okno zamknelo sie samo, czy zostalo ubite.
/// Plik jest dzienny i przycinany, wiec nie urosnie w nieskonczonosc.
/// </summary>
public static class DziennikOkna
{
    static string? _plik;
    static readonly object Zamek = new();

    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr proces, uint rodzaj);

    /// <summary>Katalog dziennika okna — w profilu uzytkownika, bo tam okno pisze zawsze.</summary>
    public static string Katalog =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WU_Connector");

    public static void Zacznij()
    {
        // Dziennik uslugi lezy w ProgramData, gdzie zwykly uzytkownik ma tylko odczyt — okno musi pisac
        // u siebie, inaczej slad po zawieszeniu nie powstaje wlasnie tam, gdzie jest najbardziej potrzebny.
        try
        {
            Directory.CreateDirectory(Katalog);
            _plik = Path.Combine(Katalog, $"okno-{DateTime.Now:yyyy-MM-dd}.log");
        }
        catch { return; }   // brak praw do katalogu nie moze przewrocic okna

        var p = System.Diagnostics.Process.GetCurrentProcess();
        Pisz($"START pid {p.Id}, wersja {typeof(DziennikOkna).Assembly.GetName().Version?.ToString(3)}, "
             + $"admin {Kontekst.Administrator}");

        Application.ThreadException += (_, e) => Pisz("WYJATEK (wątek interfejsu): " + Opis(e.Exception));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Pisz("WYJATEK (domena): " + Opis(e.ExceptionObject as Exception));
        Application.ApplicationExit += (_, _) => Pisz("KONIEC");

        // Bicie serca: dopoki wpisy przybywaja, petla komunikatow zyje. Brak wpisow w momencie zawieszenia
        // pokaze, kiedy okno przestalo odpowiadac, a liczby obok — czy cokolwiek przed tym roslo.
        var zegar = new System.Windows.Forms.Timer { Interval = 60_000 };
        zegar.Tick += (_, _) => Puls();
        zegar.Start();
        Puls();
    }

    static void Puls()
    {
        try
        {
            var p = System.Diagnostics.Process.GetCurrentProcess();
            p.Refresh();
            Pisz($"puls: GDI {GetGuiResources(p.Handle, 0)}, USER {GetGuiResources(p.Handle, 1)}, "
                 + $"uchwyty {p.HandleCount}, pamięć {p.PrivateMemorySize64 / (1024 * 1024)} MB, "
                 + $"okien {Application.OpenForms.Count}");
        }
        catch { /* diagnostyka nie moze przeszkadzac oknu */ }
    }

    static string Opis(Exception? e) => e is null ? "nieznany" : $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}";

    public static void Pisz(string tekst)
    {
        if (_plik is null) return;
        try
        {
            lock (Zamek) File.AppendAllText(_plik, $"{DateTime.Now:HH:mm:ss.fff} {tekst}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* dziennik jest pomocniczy — jego brak nie moze nic zatrzymac */ }
    }
}
