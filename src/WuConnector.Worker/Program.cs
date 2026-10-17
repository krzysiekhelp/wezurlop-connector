using System;
using System.IO;
using System.Text;
using WuConnector.Contracts;

namespace WuConnector.Worker
{
    /// <summary>
    /// Proces roboczy connectora.
    ///   wejscie : JSON zadania na stdin (albo --wejscie &lt;plik&gt; do testow)
    ///   wyjscie : jedna linia "WU-WYNIK:{json}" na stdout
    ///   kod     : 0 = wynik wypisany (dowolny status), 2 = nieczytelne wejscie, 3 = awaria
    /// Uruchamiany przez usluge z twardym limitem czasu; moze zostac ubity w dowolnej chwili.
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            var utf8 = new UTF8Encoding(false);
            Console.OutputEncoding = utf8;
            Console.InputEncoding = utf8;

            int rc;
            try
            {
                string json = CzytajWejscie(args, utf8);
                ZadanieWorkera zadanie;
                try { zadanie = WorkerJson.Deserializuj<ZadanieWorkera>(json); }
                catch (Exception ex)
                {
                    Wypisz(new WynikWorkera { Status = Statusy.Blad, Kod = Kody.ZadanieNiepoprawne, Komunikat = "JSON: " + ex.Message });
                    return Zakoncz(2);
                }

                Wypisz(new Wykonawca(zadanie).Wykonaj());
                rc = 0;
            }
            catch (Exception ex)
            {
                Wypisz(new WynikWorkera { Status = Statusy.BladPrzejsciowy, Kod = Kody.ConnectorAwaria, Komunikat = ex.GetType().Name + ": " + ex.Message });
                rc = 3;
            }
            return Zakoncz(rc);
        }

        static string CzytajWejscie(string[] args, Encoding enc)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--wejscie") return File.ReadAllText(args[i + 1], enc);
            using (var r = new StreamReader(Console.OpenStandardInput(), enc))
                return r.ReadToEnd();
        }

        static void Wypisz(WynikWorkera w)
        {
            Console.Out.WriteLine(WorkerJson.Znacznik + WorkerJson.Serializuj(w));
            Console.Out.Flush();
        }

        /// <summary>
        /// Po ApiFinalize zwykly powrot konczy sie EXIT=0, ale gdy zamkniecie biblioteki sie nie udalo,
        /// odladowanie DLL spod zywych watkow daje 0xC0000005 i nadpisuje kod. TerminateProcess
        /// konczy proces bez DLL_PROCESS_DETACH — kod wyjscia zostaje nasz.
        /// </summary>
        static int Zakoncz(int rc)
        {
            Console.Out.Flush();
            Console.Error.Flush();
            Win32.TerminateProcess(Win32.GetCurrentProcess(), (uint)rc);
            return rc;
        }
    }
}
