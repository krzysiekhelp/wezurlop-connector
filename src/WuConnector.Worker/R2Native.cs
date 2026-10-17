using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WuConnector.Worker
{
    // Deklaracje przeniesione z diag\Diag.cs (sprawdzone na R2 10.10.32 i 10.10.33).
    //
    // DWIE RZECZY, KTORYCH NIE MA W PRZYKLADACH PRODUCENTA — obie daja CICHE bledne wyniki:
    //  1. Biblioteka jest UNICODE (Delphi XE4+, PChar = PWideChar) i nie ma wariantow ...W
    //     → CharSet.Unicode + ExactSpelling = true.
    //  2. Delphi Boolean ma 1 BAJT, takze w parametrach
    //     → [return: MarshalAs(U1)] na kazdej funkcji zwracajacej Boolean, [MarshalAs(U1)] na parametrach.
    //
    // CELOWO POMINIETE: funkcje z dopiskiem //no w naglowku producenta (GetGodzUrlPozostalo,
    // GetTableDBName, GetTableFieldName, GetTableFieldAsMemo, GetZatrIndParams) — GetGodzUrlPozostalo
    // konczy proces nieprzechwytywalnym AccessViolationException. LoginUserAsProgram — niedeterministyczne.
    // TableDisableEvents — wylacza warstwe logiczna R2.

    internal static class Win32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        // Konczy proces bez DLL_PROCESS_DETACH — biblioteka R2 zostawia zywe watki,
        // a ich odladowanie potrafi skonczyc sie 0xC0000005 i nadpisac kod wyjscia.
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    }

    internal static class R2
    {
        public const string Dll = "R2PApi10.dll";   // rozwiazywana przez SetDllDirectory
        const CharSet CS = CharSet.Unicode;

        // --- polaczenie i sesja ---
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ConnectBin(string IniPath);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool GetLastError(StringBuilder Value);
        /// <summary>Zwraca USERS.X_I; 0 = blad (NIE Boolean).</summary>
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern int LoginUser(string UserName, string UserPassword);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool LogoffUser(string UserName);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool ConnectDaneX(int FirmIde);

        // --- zamkniecie (kolejnosc: CloseAllTables → LogoffUser → FreeAllDataModules → ApiFinalize) ---
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern void CloseAllTables();
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern void FreeAllDataModules();
        /// <summary>Nieudokumentowane (brak w R2PHeader.pas). Bez niego proces wisi albo pada przy wyjsciu.</summary>
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern void ApiFinalize();

        // --- tabele ---
        /// <summary>DisableAfterOpen=false: z tabelami powiazanymi — WYMAGANE dla karty urlopowej.</summary>
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern int OpenTable(string TableName, [MarshalAs(UnmanagedType.U1)] bool DisableAfterOpen);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern void TableFirst(int Handle);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern void TableNext(int Handle);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool TableEOF(int Handle);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern int TableState(int Handle);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool TableLocate(int Handle, string KeyFields, object KeyValues,
                                              [MarshalAs(UnmanagedType.U1)] bool Ref);

        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool TableInsert(int Handle);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool TableDelete(int Handle);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool TablePost(int Handle);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool TableCancel(int Handle);

        // --- pola ---
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool GetTableFieldAsString(int Handle, string Name, StringBuilder Value);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)]
        public static extern int GetTableFieldAsInteger(int Handle, string Name);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool GetTableFieldAsBoolean(int Handle, string Name);
        [DllImport(Dll, EntryPoint = "GetTableFieldAsDate", CharSet = CS, ExactSpelling = true)]
        static extern double GetTableFieldAsDate(int Handle, string Name);

        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool SetTableFieldAsInteger(int Handle, string Name, int Value);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool SetTableFieldAsBoolean(int Handle, string Name, [MarshalAs(UnmanagedType.U1)] bool Value);
        [DllImport(Dll, CharSet = CS, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool SetTableFieldAsString(int Handle, string Name, string Value);
        [DllImport(Dll, EntryPoint = "SetTableFieldAsDate", CharSet = CS, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        static extern bool SetTableFieldAsDate(int Handle, string Name, double Value);

        // Daty w R2 to double OLE Automation. Zly typ nie zglosi bledu, tylko zapisze smieci.
        public static DateTime? GetDate(int h, string pole)
        {
            double d = GetTableFieldAsDate(h, pole);
            return d < 1.0 ? (DateTime?)null : DateTime.FromOADate(d);
        }

        public static bool SetDate(int h, string pole, DateTime v) => SetTableFieldAsDate(h, pole, v.ToOADate());
    }
}
