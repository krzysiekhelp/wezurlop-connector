using System.Collections.Generic;

namespace WuConnector.Contracts
{
    /// <summary>
    /// Wejscie procesu roboczego: jedno zadanie na uruchomienie, JSON na stdin.
    /// Haslo idzie wylacznie przez stdin, nigdy w linii polecen.
    /// </summary>
    public sealed class ZadanieWorkera
    {
        /// <summary><see cref="Operacje"/>: zapis, usuniecie, test.</summary>
        public string Operacja { get; set; }

        /// <summary>Katalog BIN instalacji R2 — stad .ini, .lic, .prt.</summary>
        public string KatalogBin { get; set; }

        /// <summary>Katalog z R2PApi10.dll (magazyn wersji). Pusty = KatalogBin.</summary>
        public string KatalogDll { get; set; }

        public string Login { get; set; }
        public string Haslo { get; set; }

        /// <summary>FIRM.X_I → ConnectDaneX.</summary>
        public int FirmIde { get; set; }

        /// <summary>PRACOWNK.X_I — pozycjonowanie tabeli PRACOWNICY.</summary>
        public int PracownikId { get; set; }

        /// <summary>HISTORIA.X_I — pozycjonowanie AKTUALNE ZATRUDNIENIE; pod nim wisi karta urlopowa.</summary>
        public int ZatrudnienieId { get; set; }

        /// <summary>Identyfikator zadania tenanta; trafia do KART_URL.OPIS. Maks. 32 znaki [A-Za-z0-9-].</summary>
        public string RequestId { get; set; }

        /// <summary>R_DNIA — zerowy indeks listy R2 (0 wypoczynkowy, 5 na zadanie).</summary>
        public int Kod { get; set; }

        /// <summary>yyyy-MM-dd, pelny zakres — R2 sam dzieli przelom miesiaca.</summary>
        public string DataOd { get; set; }
        public string DataDo { get; set; }

        /// <summary>Przy usunieciu: KART_URL.X_I wpisow do usuniecia.</summary>
        public List<int> Zapisy { get; set; }
    }

    public static class Operacje
    {
        public const string Zapis = "zapis";
        public const string Usuniecie = "usuniecie";
        public const string Test = "test";
    }
}
