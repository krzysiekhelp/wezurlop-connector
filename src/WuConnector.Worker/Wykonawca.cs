using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using WuConnector.Contracts;

namespace WuConnector.Worker
{
    /// <summary>
    /// Wykonuje JEDNO zadanie w jednej sesji R2 i zamyka biblioteke.
    /// Nie rzuca wyjatkow na zewnatrz — kazdy wynik, takze awaria, wraca jako WynikWorkera.
    /// </summary>
    internal sealed class Wykonawca
    {
        readonly ZadanieWorkera _z;
        readonly WynikWorkera _w = new WynikWorkera();
        readonly StringBuilder _buf = new StringBuilder(16384);   // GetLastError: bufor 16384 znakow
        string _zalogowany;
        bool _bibliotekaZaladowana;

        public Wykonawca(ZadanieWorkera z) { _z = z; }

        public WynikWorkera Wykonaj()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                WykonajWewnatrz();
            }
            catch (Exception ex)
            {
                // AccessViolationException tu NIE trafi — zabija proces. Usluga rozpozna to po braku wyniku.
                Ustaw(Statusy.BladPrzejsciowy, Kody.ConnectorAwaria, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Zamknij();
                _w.CzasMs = sw.ElapsedMilliseconds;
            }
            return _w;
        }

        void WykonajWewnatrz()
        {
            string blad = WalidatorZadania.Sprawdz(_z);
            if (blad != null) { Ustaw(Statusy.Blad, Kody.ZadanieNiepoprawne, blad); return; }

            if (_z.Operacja != Operacje.Zapis && _z.Operacja != Operacje.Usuniecie && _z.Operacja != Operacje.Test)
            {
                Ustaw(Statusy.Blad, Kody.OperacjaNieobslugiwana, "operacja '" + _z.Operacja + "' nie jest wykonywana przez connector");
                return;
            }

            if (IntPtr.Size != 4) { Ustaw(Statusy.Blad, Kody.ConnectorAwaria, "proces nie jest 32-bitowy"); return; }

            if (!Polacz()) return;

            switch (_z.Operacja)
            {
                case Operacje.Test:
                    Ustaw(Statusy.Zapisano, null, null);
                    Log("test: polaczenie i logowanie poprawne");
                    return;
                case Operacje.Zapis:
                    Zapisz();
                    return;
                case Operacje.Usuniecie:
                    Usun();
                    return;
            }
        }

        // ------------------------------------------------------------------ polaczenie

        bool Polacz()
        {
            string bin = ZUkosnikiem(_z.KatalogBin);
            string dll = string.IsNullOrWhiteSpace(_z.KatalogDll) ? bin : ZUkosnikiem(_z.KatalogDll);

            if (!File.Exists(Path.Combine(dll, R2.Dll)))
            {
                Ustaw(Statusy.Blad, Kody.Konfiguracja, "brak " + R2.Dll + " w " + dll);
                return false;
            }
            // Przed PIERWSZYM wywolaniem R2 — DllImport ma nazwe stala, katalog wskazuje SetDllDirectory.
            if (!Win32.SetDllDirectory(dll))
            {
                Ustaw(Statusy.Blad, Kody.ConnectorAwaria, "SetDllDirectory nie powiodlo sie");
                return false;
            }

            _bibliotekaZaladowana = true;
            if (!R2.ConnectBin(bin))
            {
                string k = Err();
                var (s, kod) = KlasyfikatorR2.BladPolaczenia(k);
                Ustaw(s, kod, k);
                return false;
            }
            _w.WersjaBiblioteki = WersjaZaladowanejBiblioteki();
            Log("ConnectBin: " + bin + " (biblioteka " + _w.WersjaBiblioteki + ")");

            int uid = R2.LoginUser(_z.Login, _z.Haslo ?? "");
            if (uid == 0)
            {
                string k = Err();
                var (s, kod) = KlasyfikatorR2.BladLogowania(k);
                Ustaw(s, kod, k);
                return false;
            }
            _zalogowany = _z.Login;
            _w.UzytkownikId = uid;
            Log("LoginUser: USERS.X_I = " + uid);

            if (!R2.ConnectDaneX(_z.FirmIde))
            {
                string k = Err();
                Ustaw(Statusy.Blad, Kody.Konfiguracja, k);
                return false;
            }
            Log("ConnectDaneX(" + _z.FirmIde + ")");
            return true;
        }

        /// <summary>
        /// Ustawia PRACOWNICY i AKTUALNE ZATRUDNIENIE na wskazanych rekordach i otwiera karte urlopowa
        /// jako detail. Zwraca uchwyt karty albo 0 (wynik juz ustawiony).
        /// </summary>
        int OtworzKarteUrlopowa()
        {
            int pracT = R2.OpenTable("PRACOWNICY", true);
            if (pracT == 0) { Ustaw(Statusy.Blad, Kody.Nieznany, "OpenTable(PRACOWNICY): " + Err()); return 0; }
            if (!R2.TableLocate(pracT, "X_I", _z.PracownikId, false))
            {
                Ustaw(Statusy.Blad, Kody.PracownikNieznaleziony, "pracownik X_I=" + _z.PracownikId + ": " + Err());
                return 0;
            }

            int zatrT = R2.OpenTable("AKTUALNE ZATRUDNIENIE", true);
            if (zatrT == 0) { Ustaw(Statusy.Blad, Kody.Nieznany, "OpenTable(AKTUALNE ZATRUDNIENIE): " + Err()); return 0; }
            // Pracownik moze miec kilka aktualnych zatrudnien — pozycjonujemy wprost po X_I.
            if (!R2.TableLocate(zatrT, "X_I", _z.ZatrudnienieId, false)
                || R2.GetTableFieldAsInteger(zatrT, "X_I") != _z.ZatrudnienieId)
            {
                Ustaw(Statusy.Blad, Kody.ZatrudnienieNieznalezione,
                      "zatrudnienie X_I=" + _z.ZatrudnienieId + " nie jest aktualnym zatrudnieniem pracownika " + _z.PracownikId);
                return 0;
            }
            Log("pozycja: pracownik " + _z.PracownikId + ", zatrudnienie " + _z.ZatrudnienieId);

            int kuT = R2.OpenTable("KARTA URLOPOWA", false);   // false = z tabelami powiazanymi
            if (kuT == 0) { Ustaw(Statusy.Blad, Kody.Nieznany, "OpenTable(KARTA URLOPOWA): " + Err()); return 0; }
            return kuT;
        }

        // ------------------------------------------------------------------ zapis

        void Zapisz()
        {
            WalidatorZadania.ParsujDate(_z.DataOd, out var od);
            WalidatorZadania.ParsujDate(_z.DataDo, out var @do);

            int kuT = OtworzKarteUrlopowa();
            if (kuT == 0) return;

            // Idempotencja, warstwa ostatnia: proces mogl zostac ubity po TablePost, a przed zgloszeniem.
            var juz = WpisyZadania(kuT);
            if (juz.Count > 0)
            {
                _w.Zapisy = juz;
                Ustaw(Statusy.Zapisano, null, null);
                Log("wpisy z OPIS=" + _z.RequestId + " juz istnieja (" + juz.Count + ") — bez ponownego zapisu");
                return;
            }

            if (!R2.TableInsert(kuT)) { Ustaw(Statusy.Blad, Kody.Nieznany, "TableInsert: " + Err()); return; }

            // NIE ustawiamy: X_I_HISTORII (z master-detail), IL_DNI, IL_GODZ, Zatwierdzil_wniosek, Data_zatw — wylicza R2.
            bool ok = R2.SetTableFieldAsInteger(kuT, "R_DNIA", _z.Kod)
                   && R2.SetDate(kuT, "Od", od)
                   && R2.SetDate(kuT, "Do", @do)
                   && R2.SetTableFieldAsInteger(kuT, "STATUS", 0)
                   && R2.SetTableFieldAsString(kuT, "OPIS", _z.RequestId)
                   // Decyzja 19.09.2026 (22.17): „uwzglednij na kolejnej liscie wynagrodzen" ZAWSZE wylaczone.
                   && R2.SetTableFieldAsBoolean(kuT, "WYPL_NA_KOL_L", false);
            if (!ok)
            {
                string k = Err();
                R2.TableCancel(kuT);
                Ustaw(Statusy.Blad, Kody.Nieznany, "ustawienie pol: " + k);
                return;
            }

            if (!R2.TablePost(kuT))
            {
                string k = Err();          // natychmiast — kolejne wywolanie kasuje komunikat
                R2.TableCancel(kuT);       // OBOWIAZKOWO po nieudanym TablePost
                Ustaw(Statusy.Blad, KlasyfikatorR2.BladZapisu(k), k);
                _w.Zapisy = WpisyZadania(kuT);   // regula: faktyczny stan — oczekiwane []
                Log("TablePost odrzucony: " + k);
                return;
            }

            string ostrz = Err();          // takze po SUKCESIE: przelom miesiaca, przekroczony limit
            _w.Ostrzezenia = KlasyfikatorR2.Ostrzezenia(ostrz);
            _w.Zapisy = WpisyZadania(kuT);

            if (_w.Ostrzezenia.Count > 0)
                Ustaw(Statusy.ZapisanoZOstrzezeniem, KlasyfikatorR2.NajwazniejszeOstrzezenie(_w.Ostrzezenia), ostrz);
            else
                Ustaw(Statusy.Zapisano, null, null);
            Log("TablePost: zapisano " + _w.Zapisy.Count + " wpis(y)");
        }

        // ------------------------------------------------------------------ usuniecie

        void Usun()
        {
            int kuT = OtworzKarteUrlopowa();
            if (kuT == 0) return;

            var bledy = new List<string>();
            foreach (int id in _z.Zapisy.Distinct())
            {
                if (!R2.TableLocate(kuT, "X_I", id, false) || R2.GetTableFieldAsInteger(kuT, "X_I") != id)
                {
                    // Juz usuniety (np. przez kadry) — cel zadania osiagniety.
                    _w.NieZnalezione.Add(id);
                    Log("X_I=" + id + ": brak pod zatrudnieniem — uznany za usuniety");
                    continue;
                }
                // Wiersz limitu (L=1) to nie okres urlopu — nigdy go nie usuwamy.
                if (R2.GetTableFieldAsBoolean(kuT, "L"))
                {
                    bledy.Add("X_I=" + id + " jest wierszem limitu");
                    continue;
                }
                if (!R2.TableDelete(kuT))
                {
                    bledy.Add("X_I=" + id + ": " + Err());
                    continue;
                }
                Log("X_I=" + id + ": usuniety (TableDelete cofa takze karte pracy)");
            }

            // Regula: zapisy = faktyczny stan. Pozostale z listy, ktore nadal istnieja.
            _w.Zapisy = _z.Zapisy.Distinct()
                          .Where(id => R2.TableLocate(kuT, "X_I", id, false) && R2.GetTableFieldAsInteger(kuT, "X_I") == id)
                          .Select(id => Odczytaj(kuT))
                          .ToList();

            if (bledy.Count == 0)
                Ustaw(Statusy.Zapisano, null, null);
            else
                Ustaw(Statusy.Blad, bledy.Any(b => b.Contains("limitu")) ? Kody.WpisNieDoUsuniecia : Kody.Nieznany,
                      string.Join("; ", bledy));
        }

        // ------------------------------------------------------------------ pomocnicze

        /// <summary>Wpisy okresu (L=0) pod biezacym zatrudnieniem z OPIS = RequestId.</summary>
        List<ZapisR2> WpisyZadania(int kuT)
        {
            var lista = new List<ZapisR2>();
            if (string.IsNullOrEmpty(_z.RequestId)) return lista;
            R2.TableFirst(kuT);
            while (!R2.TableEOF(kuT))
            {
                if (!R2.GetTableFieldAsBoolean(kuT, "L") && Str(kuT, "OPIS") == _z.RequestId)
                    lista.Add(Odczytaj(kuT));
                R2.TableNext(kuT);
            }
            return lista.OrderBy(z => z.DataOd, StringComparer.Ordinal).ToList();
        }

        ZapisR2 Odczytaj(int kuT) => new ZapisR2
        {
            Id = R2.GetTableFieldAsInteger(kuT, "X_I"),
            DataOd = Data(R2.GetDate(kuT, "Od")),
            DataDo = Data(R2.GetDate(kuT, "Do")),
            Dni = R2.GetTableFieldAsInteger(kuT, "IL_DNI"),
            Minuty = R2.GetTableFieldAsInteger(kuT, "IL_GODZ"),
        };

        static string Data(DateTime? d) => d.HasValue ? WalidatorZadania.FormatujDate(d.Value) : null;

        /// <summary>Ostatni komunikat R2. Wolac NATYCHMIAST po operacji.</summary>
        string Err()
        {
            _buf.Length = 0;
            return R2.GetLastError(_buf) ? _buf.ToString().Trim() : "";
        }

        string Str(int h, string pole)
        {
            _buf.Length = 0;
            R2.GetTableFieldAsString(h, pole, _buf);
            return _buf.ToString().Trim();
        }

        void Zamknij()
        {
            if (!_bibliotekaZaladowana) return;
            try
            {
                R2.CloseAllTables();
                if (_zalogowany != null) R2.LogoffUser(_zalogowany);
                R2.FreeAllDataModules();
                R2.ApiFinalize();
                Log("sesja i biblioteka zamkniete");
            }
            catch (Exception ex)
            {
                Log("zamkniecie: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        static string WersjaZaladowanejBiblioteki()
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                if (m.ModuleName.Equals(R2.Dll, StringComparison.OrdinalIgnoreCase))
                    return m.FileVersionInfo.FileVersion;
            return null;
        }

        static string ZUkosnikiem(string katalog) => katalog.EndsWith("\\") ? katalog : katalog + "\\";

        void Ustaw(string status, string kod, string komunikat)
        {
            _w.Status = status;
            _w.Kod = kod;
            _w.Komunikat = string.IsNullOrEmpty(komunikat) ? null : komunikat;
        }

        void Log(string s) => _w.Przebieg.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s);
    }
}
