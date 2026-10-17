using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace WuConnector.Core.Biblioteki;

public sealed record WynikPodpisu(bool Poprawny, string? Podmiot, string? Blad);

/// <summary>
/// Sprawdza, czy plik jest oryginalem producenta R2: podpis Authenticode wazny wg Windows (WinVerifyTrust)
/// ORAZ certyfikat wystawiony dla Symfonia Sp. z o.o. o numerze KRS 0000228956.
/// Przypiecie do KRS, a nie tylko do nazwy — nazwe w certyfikacie moze miec kazdy, kto przejdzie weryfikacje EV.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PodpisProducenta
{
    public const string Nazwa = "Symfonia Sp. z o.o.";
    public const string NumerRejestrowy = "0000228956";   // KRS — pole SERIALNUMBER certyfikatu EV

    public static WynikPodpisu Sprawdz(string plik)
    {
        if (!File.Exists(plik)) return new(false, null, "brak pliku");

        int wynik = WinVerifyTrust(plik);
        if (wynik != 0) return new(false, null, $"podpis cyfrowy nieprawidlowy (WinVerifyTrust 0x{wynik:X8})");

        string podmiot;
        try
        {
#pragma warning disable SYSLIB0057 // jedyne API zwracajace certyfikat podpisu Authenticode
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(plik));
#pragma warning restore SYSLIB0057
            podmiot = cert.Subject;
        }
        catch (Exception e) { return new(false, null, "nie mozna odczytac certyfikatu: " + e.Message); }

        var pola = Pola(podmiot);
        bool nazwa = pola.TryGetValue("CN", out var cn) && cn == Nazwa;
        bool krs = pola.TryGetValue("SERIALNUMBER", out var sn) && sn == NumerRejestrowy;
        return nazwa && krs
            ? new(true, podmiot, null)
            : new(false, podmiot, $"podpisane przez kogos innego niz {Nazwa} (KRS {NumerRejestrowy})");
    }

    /// <summary>"CN=A, O=B, SERIALNUMBER=C" → slownik. Wartosci w cudzyslowach moga zawierac przecinki.</summary>
    static Dictionary<string, string> Pola(string dn)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < dn.Length)
        {
            int rowna = dn.IndexOf('=', i);
            if (rowna < 0) break;
            string klucz = dn[i..rowna].Trim();
            int j = rowna + 1;
            string wartosc;
            if (j < dn.Length && dn[j] == '"')
            {
                int koniec = dn.IndexOf('"', j + 1);
                wartosc = dn[(j + 1)..koniec];
                j = koniec + 1;
            }
            else
            {
                int przecinek = dn.IndexOf(',', j);
                if (przecinek < 0) przecinek = dn.Length;
                wartosc = dn[j..przecinek].Trim();
                j = przecinek;
            }
            d.TryAdd(klucz, wartosc);
            i = j + 1;
        }
        return d;
    }

    // ------------------------------------------------------------------ WinVerifyTrust

    static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    static int WinVerifyTrust(string plik)
    {
        var info = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = plik };
        IntPtr pInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(info, pInfo, false);
            var dane = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,                  // WTD_UI_NONE — usluga nie ma pulpitu
                fdwRevocationChecks = 0,         // WTD_REVOKE_NONE — serwer klienta bywa bez internetu do CRL
                dwUnionChoice = 1,               // WTD_CHOICE_FILE
                pFile = pInfo,
                dwProvFlags = 0x00000010,        // WTD_REVOCATION_CHECK_NONE
            };
            var akcja = WintrustActionGenericVerifyV2;
            return WinVerifyTrustNative(IntPtr.Zero, ref akcja, ref dane);
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pInfo);
            Marshal.FreeHGlobal(pInfo);
        }
    }

    [DllImport("wintrust.dll", EntryPoint = "WinVerifyTrust", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrustNative(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
