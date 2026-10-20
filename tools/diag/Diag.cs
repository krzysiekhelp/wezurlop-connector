// Przeniesione z C:\Symfonia\_projekt-urlopy\diag 20.10.2026.
// WZORCOWA IMPLEMENTACJA P/Invoke do R2PApi10.dll: poprawny marshalling (Unicode + ExactSpelling,
// 1-bajtowy Boolean) i kolejnosc wywolan. Zaczynaj od tego pliku, nie od przykladow producenta —
// tamte daja milczaco falszywe wyniki (ustalenia, rozdzial 6). Nazwiska i sciezki sa fikcyjne.
//
// Program diagnostyczny R2PApi10 — Fazy 1-3 (odczyt) + opcjonalny zapis testowy.
// Kompilacja: csc /platform:x86 /out:Diag.exe Diag.cs
// Uruchomienie: Diag.exe [--bin <katalog>] [--user NAZWA --pass HASLO] [--zapisz]
//
// Bez --zapisz program NICZEGO nie zapisuje.
//
// DWA USTALENIA, KTORYCH NIE MA W PRZYKLADACH PRODUCENTA:
//  1. Biblioteka jest UNICODE (Delphi XE4+, PChar = PWideChar).
//     Przyklady C# uzywaja domyslnego ANSI i z ta wersja DLL nie dzialaja.
//     Brak wariantow ...W w eksportach => ExactSpelling = true.
//  2. Delphi Boolean ma 1 BAJT, a .NET bool marshalluje sie domyslnie jako 4-bajtowy BOOL.
//     Delphi zwraca wynik w AL, starsze bajty EAX bywaja smieciem => trzeba
//     [return: MarshalAs(UnmanagedType.U1)] na KAZDEJ funkcji zwracajacej Boolean.
//     Bez tego m.in. TableEOF potrafi od razu zglosic koniec tabeli.

using System;
using System.Runtime.InteropServices;
using System.Text;

static class Win
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    // TerminateProcess konczy proces NATYCHMIAST, bez DLL_PROCESS_DETACH.
    // Environment.Exit odladowuje biblioteki spod zywych watkow R2PApi10.dll
    // i konczy sie naruszeniem ochrony pamieci (0xC0000005).
    [DllImport("kernel32.dll")]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
}

static class R2
{
    const string DLL = "R2PApi10.dll";   // rozwiazywana przez SetDllDirectory na BIN instalacji
    const CharSet CS = CharSet.Unicode;

    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool ConnectBin(string IniPath);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetLastError(StringBuilder Value);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  LoginUser(string UserName, string UserPassword);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  LoginUserAsProgram();
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool LogoffUser(string UserName);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool ConnectDaneX(int FirmIde);

    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  OpenTable(string TableName, [MarshalAs(UnmanagedType.U1)] bool DisableAfterOpen);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  OpenTableEx(string TableName, [MarshalAs(UnmanagedType.U1)] bool DisableAfterOpen, string Filter);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void CloseAllTables();

    // Nieudokumentowane w naglowku producenta — znalezione w tablicy eksportow.
    // Kandydat na wlasciwe zamkniecie biblioteki (zwalnia jej watki?).
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void ApiFinalize();
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void FreeAllDataModules();
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  TableRecordCount(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  TableRecNo(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void TableFirst(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void TableNext(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TableEOF(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TableBOF(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  TableState(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TableLocate(int Handle, string KeyFields, object KeyValues,
                                          [MarshalAs(UnmanagedType.U1)] bool Ref);

    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TableInsert(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TableEdit(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TableDelete(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TablePost(int Handle);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool TableCancel(int Handle);

    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void GetBinConnectionString(StringBuilder Value);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void GetDaneXConnectionString(StringBuilder Value);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern void GetTableSQL(int Handle, StringBuilder Value);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  GetTableFieldCount(int Handle);
    // Nieudokumentowana (w naglowku producenta dopisek //no). Zwraca pozostaly urlop
    // policzony przez SAM program — test krzyzowy dla naszej formuly z SQL.
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  GetGodzUrlPozostalo(int Rodzaj, int Year, StringBuilder Godz8Days);

    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetTableFieldAsString(int Handle, string Name, StringBuilder Value);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)]
    public static extern int  GetTableFieldAsInteger(int Handle, string Name);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetTableFieldAsBoolean(int Handle, string Name);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool GetTableFieldStrings(int Handle, string Name, StringBuilder Value);

    [DllImport(DLL, EntryPoint = "GetTableFieldAsDate", CharSet = CS, ExactSpelling = true)]
    public static extern double GetDateRaw(int Handle, string Name);
    public static DateTime GetDate(int h, string n)
    {
        double d = GetDateRaw(h, n);
        return d < 1.0 ? DateTime.MinValue : DateTime.FromOADate(d);
    }

    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SetTableFieldAsInteger(int Handle, string Name, int Value);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SetTableFieldAsBoolean(int Handle, string Name, [MarshalAs(UnmanagedType.U1)] bool Value);
    [DllImport(DLL, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool SetTableFieldAsString(int Handle, string Name, string Value);
    [DllImport(DLL, EntryPoint = "SetTableFieldAsDate", CharSet = CS, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool _SetDate(int Handle, string Name, double Value);
    public static bool SetDate(int h, string n, DateTime v) { return _SetDate(h, n, v.ToOADate()); }
}

class Program
{
    static StringBuilder buf = new StringBuilder(16384);
    static bool finalize = false;
    static bool miekkie  = false;
    static bool odSet = false, doSet = false;
    static bool tylkoLoginUser = false;   // --tylko-loginuser: pomin LoginUserAsProgram
    static string dllDir = null;          // --dll <katalog>: biblioteka z innego katalogu niz ini/lic/prt
    static DateTime urlOd = new DateTime(2026, 9, 9);
    static DateTime urlDo = new DateTime(2026, 9, 10);
    static int urlRodzaj = 0;
    static string urlOpis = null;
    static int  urlWyplata = -1;      // -1 = nie ruszaj, 0 = odznacz, 1 = zaznacz
    static int  edytujId   = 0;       // X_I wpisu do edycji
    static int  usunId     = 0;       // X_I wpisu do usuniecia
    static int  saldoRok   = 0;       // --saldo <rok>

    // Ostatni komunikat R2. Wolac NATYCHMIAST po operacji — kolejne wywolanie API go kasuje.
    static string Err()
    {
        buf.Length = 0;
        bool any = R2.GetLastError(buf);
        return any ? buf.ToString().Trim() : "";
    }

    static string Str(int h, string field)
    {
        buf.Length = 0;
        R2.GetTableFieldAsString(h, field, buf);
        return buf.ToString().Trim();
    }

    static void LoadKonto(ref string user, ref string pass)
    {
        try
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location), "konto.txt");
            if (!System.IO.File.Exists(path)) return;
            string[] l = System.IO.File.ReadAllLines(path, Encoding.UTF8);
            if (l.Length > 0) user = l[0].Trim();
            if (l.Length > 1) pass = l[1].Trim();
            Info("Poswiadczenia wczytane z konto.txt (uzytkownik: " + user + ")");
        }
        catch (Exception ex) { Info("konto.txt: " + ex.Message); }
    }

    // Maskuje haslo w connection stringu przed wypisaniem.
    static string Masked(string cs)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            cs, @"(?i)(password|pwd)\s*=\s*[^;]*", "$1=***");
    }

    // Sprawdza obsadzenie stanowisk licencyjnych PO STRONIE SQL, w trakcie wlasnej sesji API.
    // Dzieki temu nie ma wyscigu z zewnetrznym monitorem.
    static void DumpLicencje(string etykieta, string binDb)
    {
        Krok("STANOWISKA LICENCYJNE — " + etykieta);
        try
        {
            string cs = "Server=SERWER-DEMO\\OPTIMA;Integrated Security=SSPI;Database="
                        + binDb + ";Connect Timeout=5";
            using (var c = new System.Data.SqlClient.SqlConnection(cs))
            {
                c.Open();
                var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM LICSRVCONN";
                int nConn = Convert.ToInt32(cmd.ExecuteScalar());
                cmd.CommandText = "SELECT COUNT(*) FROM LOCKS";
                int nLock = Convert.ToInt32(cmd.ExecuteScalar());
                cmd.CommandText = "SELECT COUNT(*) FROM MODULES";
                int nMod = Convert.ToInt32(cmd.ExecuteScalar());
                Info("LICSRVCONN = " + nConn + "   LOCKS = " + nLock + "   MODULES = " + nMod);

                cmd.CommandText = "SELECT X_I, Machine, UserName, Server, DBaseName FROM LICSRVCONN";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        Info(string.Format("   conn X_I={0} Machine={1} User={2} Server={3} DB={4}",
                            r["X_I"], r["Machine"], r["UserName"], r["Server"], r["DBaseName"]));

                cmd.CommandText = "SELECT X_I, X_TABLENAME, X_IUSER, X_USER, X_IDE FROM LOCKS";
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        Info(string.Format("   lock X_I={0} Tabela={1} User={2}/{3} Ide={4}",
                            r["X_I"], r["X_TABLENAME"], r["X_IUSER"], r["X_USER"], r["X_IDE"]));
            }
        }
        catch (Exception ex) { Info("SQL: " + ex.Message); }
    }

    static void Krok(string s) { Console.WriteLine(); Console.WriteLine("=== " + s + " ==="); }
    static void Ok(string s)   { Console.WriteLine("  [ok]   " + s); }
    static void Info(string s) { Console.WriteLine("         " + s); }
    static void Bad(string s)  { Console.WriteLine("  [BLAD] " + s); }

    const string NAZWISKO = "KOWALSKI";
    const string IMIE     = "JAN";
    const int    ZATR_ID  = 207;

    // R2PApi10 zostawia po sobie WATKI PIERWSZOPLANOWE — po wyjsciu z Main proces
    // NIE konczy sie sam (PID zyje, 12 watkow w Wait, brak okna).
    //
    // Environment.Exit tez nie wystarcza: odladowuje biblioteki natywne spod zywych
    // watkow i konczy sie naruszeniem ochrony pamieci 0xC0000005 (zmierzone na DLL 10.10.33).
    // Naruszenie nadpisuje nasz kod wyjscia, wiec nadzorca traci informacje o wyniku.
    //
    // TerminateProcess konczy proces natychmiast, BEZ DLL_PROCESS_DETACH — kod wyjscia zostaje.
    static int Main(string[] args)
    {
        int rc;
        try { rc = Run(args); }
        catch (Exception ex) { Bad("nieobsluzony: " + ex.Message); rc = 3; }

        Console.Out.Flush();
        Console.Error.Flush();
        if (miekkie)
        {
            Console.WriteLine("[--miekkie] zwykly powrot z Main — sprawdzamy, czy proces zakonczy sie sam");
            return rc;
        }
        Win.TerminateProcess(Win.GetCurrentProcess(), (uint)rc);
        return rc;   // nieosiagalne
    }

    static int Run(string[] args)
    {
        string bin  = @"C:\Symfonia\R2Platnik.10x_alfa_old\BIN\";
        string user = null, pass = null;
        bool zapisz = false;
        int czekaj = 0;
        string binDb = "r2p_platnik10_alfa_bin";

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--bindb" && i + 1 < args.Length) binDb = args[++i];
            else if (args[i] == "--bin"  && i + 1 < args.Length) bin  = args[++i];
            else if (args[i] == "--user" && i + 1 < args.Length) user = args[++i];
            else if (args[i] == "--pass" && i + 1 < args.Length) pass = args[++i];
            else if (args[i] == "--czekaj" && i + 1 < args.Length) int.TryParse(args[++i], out czekaj);
            else if (args[i] == "--zapisz") zapisz = true;
            else if (args[i] == "--tylko-loginuser") tylkoLoginUser = true;
            else if (args[i] == "--dll" && i + 1 < args.Length) dllDir = args[++i];
            else if (args[i] == "--od" && i + 1 < args.Length) { urlOd = DateTime.ParseExact(args[++i], "yyyy-MM-dd", null); odSet = true; }
            else if (args[i] == "--do" && i + 1 < args.Length) { urlDo = DateTime.ParseExact(args[++i], "yyyy-MM-dd", null); doSet = true; }
            else if (args[i] == "--rodzaj" && i + 1 < args.Length) urlRodzaj = int.Parse(args[++i]);
            else if (args[i] == "--opis" && i + 1 < args.Length) urlOpis = args[++i];
            else if (args[i] == "--wyplata" && i + 1 < args.Length) urlWyplata = int.Parse(args[++i]);
            else if (args[i] == "--edytuj"  && i + 1 < args.Length) edytujId   = int.Parse(args[++i]);
            else if (args[i] == "--usun"    && i + 1 < args.Length) usunId     = int.Parse(args[++i]);
            else if (args[i] == "--saldo"   && i + 1 < args.Length) saldoRok   = int.Parse(args[++i]);
            else if (args[i] == "--finalize") finalize = true;
            else if (args[i] == "--miekkie") miekkie = true;
        }
        if (!bin.EndsWith("\\")) bin += "\\";

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Program diagnostyczny R2PApi10   (proces " + (IntPtr.Size == 4 ? "x86" : "x64") + ")");
        Console.WriteLine("BIN: " + bin);
        Console.WriteLine("Tryb: " + (zapisz ? "ODCZYT + ZAPIS TESTOWY" : "TYLKO ODCZYT"));

        if (IntPtr.Size != 4) { Bad("Proces nie jest 32-bitowy."); return 2; }
        if (!Win.SetDllDirectory(dllDir ?? bin)) { Bad("SetDllDirectory nie powiodlo sie."); return 2; }
        if (dllDir != null) Console.WriteLine("DLL z katalogu: " + dllDir + "   (ini/lic/prt z: " + bin + ")");

        string loggedUser = null;
        try
        {
            // FAZA 0: obsadzenie licencji PRZED dotknieciem biblioteki.
            // To jest kolejnosc docelowa dla agenta — tani SELECT, zero sesji, zero ryzyka.
            DumpLicencje("PRZED polaczeniem (zadna sesja API nie istnieje)", binDb);

            Krok("FAZA 1 — polaczenie");
            if (!R2.ConnectBin(bin)) { Bad("ConnectBin: " + Err()); return 1; }
            Ok("ConnectBin");
            foreach (System.Diagnostics.ProcessModule m in System.Diagnostics.Process.GetCurrentProcess().Modules)
                if (m.ModuleName.Equals("R2PApi10.dll", StringComparison.OrdinalIgnoreCase))
                    Info("zaladowana biblioteka: " + m.FileName + "   wersja " + m.FileVersionInfo.FileVersion);

            int uid = tylkoLoginUser ? 0 : R2.LoginUserAsProgram();
            if (uid != 0) Ok("LoginUserAsProgram — USERS.X_I = " + uid);
            else
            {
                Info("LoginUserAsProgram zwrocil 0 (droga zamknieta dla samodzielnego procesu)");
                if (user == null) LoadKonto(ref user, ref pass);
                if (user == null)
                {
                    Bad("Brak poswiadczen. Utworz konto.txt (1. linia uzytkownik, 2. haslo)");
                    Info("albo uruchom: Diag.exe --user NAZWA --pass HASLO");
                    return 1;
                }
                uid = R2.LoginUser(user, pass ?? "");
                if (uid == 0) { Bad("LoginUser: " + Err()); return 1; }
                loggedUser = user;
                Ok("LoginUser(" + user + ") — USERS.X_I = " + uid);
            }

            int firmT = R2.OpenTable("FIRMY", true);
            if (firmT == 0) { Bad("OpenTable(FIRMY): " + Err()); return 1; }
            R2.TableFirst(firmT);
            int firmIde = R2.GetTableFieldAsInteger(firmT, "X_I");
            Ok("FIRMY — X_I = " + firmIde + ", nazwa = " + Str(firmT, "NAZWA"));
            if (!R2.ConnectDaneX(firmIde)) { Bad("ConnectDaneX: " + Err()); return 1; }
            Ok("ConnectDaneX(" + firmIde + ")");

            Krok("FAZA 2 — pracownik i kontekst zatrudnienia");
            int pracT = R2.OpenTable("PRACOWNICY", true);
            if (pracT == 0) { Bad("OpenTable(PRACOWNICY): " + Err()); return 1; }
            Ok("PRACOWNICY — rekordow: " + R2.TableRecordCount(pracT));

            if (!R2.TableLocate(pracT, "Nazwisko;Imie", new object[] { NAZWISKO, IMIE }, false))
            { Bad("TableLocate: " + Err()); return 1; }
            Ok("Znaleziony: " + Str(pracT, "Nazwisko") + " " + Str(pracT, "Imie")
               + "  (X_I = " + R2.GetTableFieldAsInteger(pracT, "X_I")
               + ", Identyfikator = " + Str(pracT, "Identyfikator") + ")");

            int zatrT = R2.OpenTable("AKTUALNE ZATRUDNIENIE", true);
            if (zatrT == 0) { Bad("OpenTable(AKTUALNE ZATRUDNIENIE): " + Err()); return 1; }
            int zatrId = R2.GetTableFieldAsInteger(zatrT, "X_I");
            Ok("AKT. ZATRUDNIENIE — X_IHistorii = " + zatrId
               + ", rekordow: " + R2.TableRecordCount(zatrT));
            if (zatrId != ZATR_ID) Info("UWAGA: oczekiwano " + ZATR_ID);

            int kuT = R2.OpenTable("KARTA URLOPOWA", false);
            if (kuT == 0) { Bad("OpenTable(KARTA URLOPOWA): " + Err()); return 1; }
            Ok("KARTA URLOPOWA — rekordow: " + R2.TableRecordCount(kuT)
               + ", pol: " + R2.GetTableFieldCount(kuT));

            // Ten sam SELECT, ale teraz sesja API JEST otwarta i tabele sa otwarte.
            // Roznica wzgledem pomiaru z Fazy 0 pokazuje, czy sesja rejestruje sie w bazie.
            DumpLicencje("W TRAKCIE sesji API (zalogowany, tabele otwarte)", binDb);

            Krok("DIAGNOSTYKA — connection stringi z API (haslo zamaskowane)");
            buf.Length = 0; R2.GetBinConnectionString(buf);
            Info("BIN  : " + Masked(buf.ToString().Trim()));
            buf.Length = 0; R2.GetDaneXConnectionString(buf);
            Info("DANEX: " + Masked(buf.ToString().Trim()));

            Krok("DIAGNOSTYKA — zapytanie zrodlowe (GetTableSQL)");
            buf.Length = 0;
            R2.GetTableSQL(kuT, buf);
            string sql = buf.ToString().Trim();
            Info(sql.Length > 600 ? sql.Substring(0, 600) + " […]" : (sql == "" ? "(puste)" : sql));

            Krok("FAZA 3 — slownik R_DNIA przez API");
            buf.Length = 0;
            if (R2.GetTableFieldStrings(kuT, "R_DNIA", buf))
            {
                string[] lista = buf.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lista.Length; i++) Info(i + " = " + lista[i]);
            }
            else Info("GetTableFieldStrings = false. Komunikat: '" + Err() + "'");

            Krok("FAZA 3 — przejscie po karcie urlopowej");
            R2.TableFirst(kuT);
            Info("po TableFirst: BOF=" + R2.TableBOF(kuT) + " EOF=" + R2.TableEOF(kuT)
                 + " RecNo=" + R2.TableRecNo(kuT));

            int n = 0, w2026 = 0;
            while (!R2.TableEOF(kuT) && n < 1000)
            {
                n++;
                DateTime od = R2.GetDate(kuT, "Od");
                bool lim = R2.GetTableFieldAsBoolean(kuT, "L");
                DateTime dd = lim ? DateTime.MinValue : R2.GetDate(kuT, "Do");

                if (n <= 3 || od.Year == 2026)
                {
                    Info(string.Format("#{0,-4} X_I={1,-6} Od={2}  Do={3,-10}  L={4,-5} R_DNIA={5}  IL_DNI={6}  STATUS={7}",
                        n,
                        R2.GetTableFieldAsInteger(kuT, "X_I"),
                        od == DateTime.MinValue ? "(brak)   " : od.ToString("yyyy-MM-dd"),
                        lim ? "(limit)" : (dd == DateTime.MinValue ? "(brak)" : dd.ToString("yyyy-MM-dd")),
                        lim,
                        R2.GetTableFieldAsInteger(kuT, "R_DNIA"),
                        R2.GetTableFieldAsInteger(kuT, "IL_DNI"),
                        R2.GetTableFieldAsInteger(kuT, "STATUS")));
                    if (od.Year == 2026) w2026++;
                }
                R2.TableNext(kuT);
            }
            Ok("Przeszlo rekordow: " + n + ", w tym za 2026: " + w2026);

            if (czekaj > 0)
            {
                Krok("SESJA OTWARTA — czekam " + czekaj + " s");
                Info("Teraz mozna sprawdzic po SQL: LICSRVCONN, LOCKS, MODULES w bazie BIN.");
                for (int s = czekaj; s > 0; s--)
                {
                    Console.Write("\r         pozostalo " + s + " s   ");
                    System.Threading.Thread.Sleep(1000);
                }
                Console.WriteLine();
            }

            // ---------- SALDO wg SAMEGO R2 ----------
            if (saldoRok != 0)
            {
                Krok("SALDO wg API — GetGodzUrlPozostalo(rodzaj, " + saldoRok + ")");
                for (int rodz = 0; rodz <= 1; rodz++)
                {
                    StringBuilder b8 = new StringBuilder(16384);
                    int wynik;
                    try { wynik = R2.GetGodzUrlPozostalo(rodz, saldoRok, b8); }
                    catch (Exception ex) { Bad("rodzaj=" + rodz + " wyjatek: " + ex.GetType().Name + " " + ex.Message); continue; }
                    string bufTxt = b8.ToString().Trim();
                    Info("rodzaj=" + rodz + "  zwrocono=" + wynik
                         + "   (w dniach po 480: " + (wynik / 480.0).ToString("0.####") + ")"
                         + "   bufor='" + (bufTxt.Length > 60 ? bufTxt.Substring(0, 60) + "..." : bufTxt) + "'");
                    string e = Err();
                    if (e != "") Info("   GetLastError: " + e);
                }
                return 0;
            }

            // ---------- EDYCJA istniejacego wpisu ----------
            if (edytujId != 0)
            {
                Krok("EDYCJA wpisu X_I = " + edytujId);
                if (!R2.TableLocate(kuT, "X_I", edytujId, false))
                { Bad("TableLocate(X_I=" + edytujId + "): " + Err()); return 1; }
                Ok("Znaleziony: " + R2.GetDate(kuT, "Od").ToString("yyyy-MM-dd")
                   + " .. " + R2.GetDate(kuT, "Do").ToString("yyyy-MM-dd")
                   + "   WYPL_NA_KOL_L = " + R2.GetTableFieldAsBoolean(kuT, "WYPL_NA_KOL_L"));

                if (!R2.TableEdit(kuT)) { Bad("TableEdit: " + Err()); return 1; }
                Ok("TableEdit, TableState = " + R2.TableState(kuT));

                if (odSet) Info("SetDate(Od, " + urlOd.ToString("yyyy-MM-dd") + ") = " + R2.SetDate(kuT, "Od", urlOd));
                if (doSet) Info("SetDate(Do, " + urlDo.ToString("yyyy-MM-dd") + ") = " + R2.SetDate(kuT, "Do", urlDo));
                if (urlWyplata >= 0)
                {
                    bool okSet = R2.SetTableFieldAsBoolean(kuT, "WYPL_NA_KOL_L", urlWyplata == 1);
                    Info("SetTableFieldAsBoolean(WYPL_NA_KOL_L, " + (urlWyplata == 1) + ") = " + okSet);
                    if (!okSet) { Bad(Err()); R2.TableCancel(kuT); return 1; }
                }

                if (!R2.TablePost(kuT))
                { Bad("TablePost: " + Err()); R2.TableCancel(kuT); return 1; }
                string ostrzE = Err();
                Ok("TablePost — ZAPISANO");
                Info(ostrzE != "" ? "OSTRZEZENIE Z R2: " + ostrzE : "Bez ostrzezen.");

                R2.TableLocate(kuT, "X_I", edytujId, false);
                Info("Po zapisie: " + R2.GetDate(kuT, "Od").ToString("yyyy-MM-dd") + " .. " + R2.GetDate(kuT, "Do").ToString("yyyy-MM-dd") + "   WYPL_NA_KOL_L = " + R2.GetTableFieldAsBoolean(kuT, "WYPL_NA_KOL_L")
                     + "   IL_DNI = " + R2.GetTableFieldAsInteger(kuT, "IL_DNI"));
                return 0;
            }

            // ---------- USUNIECIE wpisu ----------
            if (usunId != 0)
            {
                Krok("USUNIECIE wpisu X_I = " + usunId);
                if (!R2.TableLocate(kuT, "X_I", usunId, false))
                { Bad("TableLocate(X_I=" + usunId + "): " + Err()); return 1; }
                Info("Do usuniecia: " + R2.GetDate(kuT, "Od").ToString("yyyy-MM-dd")
                     + " .. " + R2.GetDate(kuT, "Do").ToString("yyyy-MM-dd")
                     + "   IL_DNI = " + R2.GetTableFieldAsInteger(kuT, "IL_DNI")
                     + "   OPIS = " + Str(kuT, "OPIS"));
                if (!R2.TableDelete(kuT)) { Bad("TableDelete: " + Err()); return 1; }
                string ostrzD = Err();
                Ok("TableDelete — USUNIETO");
                Info(ostrzD != "" ? "OSTRZEZENIE Z R2: " + ostrzD : "Bez ostrzezen.");
                return 0;
            }

            if (!zapisz)
            {
                Krok("ZAPIS POMINIETY — uruchom z --zapisz");
                return 0;
            }

            Krok("FAZA 4 — zapis urlopu " + urlOd.ToString("yyyy-MM-dd") + " .. " + urlDo.ToString("yyyy-MM-dd"));
            if (!R2.TableInsert(kuT)) { Bad("TableInsert: " + Err()); return 1; }
            Ok("TableInsert, TableState = " + R2.TableState(kuT));

            bool a = R2.SetTableFieldAsInteger(kuT, "R_DNIA", urlRodzaj);
            bool b = R2.SetDate(kuT, "Od", urlOd);
            bool c = R2.SetDate(kuT, "Do", urlDo);
            bool d = R2.SetTableFieldAsInteger(kuT, "STATUS", 0);
            R2.SetTableFieldAsString(kuT, "OPIS", urlOpis ?? ("TEST-API-" + DateTime.Now.ToString("yyyyMMdd-HHmm")));
            if (urlWyplata >= 0)
            {
                bool okW = R2.SetTableFieldAsBoolean(kuT, "WYPL_NA_KOL_L", urlWyplata == 1);
                Info("SetTableFieldAsBoolean(WYPL_NA_KOL_L, " + (urlWyplata == 1) + ") = " + okW);
            }
            Info("SetField: R_DNIA=" + a + " Od=" + b + " Do=" + c + " STATUS=" + d);
            if (!(a && b && c && d))
            {
                Bad("Ktores SetTableField zwrocilo false: " + Err());
                R2.TableCancel(kuT);
                return 1;
            }

            if (!R2.TablePost(kuT))
            {
                Bad("TablePost: " + Err());
                R2.TableCancel(kuT);                       // OBOWIAZKOWO
                Info("TableCancel, TableState = " + R2.TableState(kuT));
                return 1;
            }

            string ostrz = Err();                          // takze po SUKCESIE
            Ok("TablePost — ZAPISANO");
            Info(ostrz != "" ? "OSTRZEZENIE Z R2: " + ostrz : "Bez ostrzezen.");

            Krok("WERYFIKACJA — " + urlOd.ToString("MM.yyyy") + " i " + urlDo.ToString("MM.yyyy"));
            R2.TableFirst(kuT);
            while (!R2.TableEOF(kuT))
            {
                DateTime od = R2.GetDate(kuT, "Od");
                if ((od.Year == urlOd.Year && od.Month == urlOd.Month) || (od.Year == urlDo.Year && od.Month == urlDo.Month))
                    Info(string.Format("X_I={0,-6} {1} .. {2}  R_DNIA={3}  IL_DNI={4}  OPIS={5}",
                        R2.GetTableFieldAsInteger(kuT, "X_I"),
                        od.ToString("yyyy-MM-dd"),
                        R2.GetTableFieldAsBoolean(kuT, "L") ? "(limit)" : R2.GetDate(kuT, "Do").ToString("yyyy-MM-dd"),
                        R2.GetTableFieldAsInteger(kuT, "R_DNIA"),
                        R2.GetTableFieldAsInteger(kuT, "IL_DNI"),
                        Str(kuT, "OPIS")));
                R2.TableNext(kuT);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Bad(ex.GetType().Name + ": " + ex.Message);
            return 3;
        }
        finally
        {
            try
            {
                R2.CloseAllTables();
                if (loggedUser != null) R2.LogoffUser(loggedUser);
                Console.WriteLine();
                Console.WriteLine("Sesja zamknieta (CloseAllTables + LogoffUser).");

                if (finalize)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try { R2.FreeAllDataModules(); Console.WriteLine("FreeAllDataModules: ok"); }
                    catch (Exception ex) { Console.WriteLine("FreeAllDataModules: " + ex.GetType().Name + " " + ex.Message); }
                    try { R2.ApiFinalize(); Console.WriteLine("ApiFinalize: ok (" + sw.ElapsedMilliseconds + " ms)"); }
                    catch (Exception ex) { Console.WriteLine("ApiFinalize: " + ex.GetType().Name + " " + ex.Message); }
                    Console.WriteLine("Watkow w procesie: " +
                        System.Diagnostics.Process.GetCurrentProcess().Threads.Count);
                }
            }
            catch { }
        }
    }
}
