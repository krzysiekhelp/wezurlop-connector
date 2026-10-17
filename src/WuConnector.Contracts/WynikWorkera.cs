using System.Collections.Generic;

namespace WuConnector.Contracts
{
    /// <summary>
    /// Wyjscie procesu roboczego: jedna linia na stdout z prefiksem <see cref="WorkerJson.Znacznik"/>.
    /// Kod wyjscia procesu jest kanalem pomocniczym — o wyniku decyduje ten JSON.
    /// </summary>
    public sealed class WynikWorkera
    {
        /// <summary><see cref="Statusy"/>.</summary>
        public string Status { get; set; }

        /// <summary>Najwazniejszy kod (<see cref="Kody"/>); null przy czystym sukcesie.</summary>
        public string Kod { get; set; }

        /// <summary>Oryginalny tekst z R2. Moze zawierac imie i nazwisko.</summary>
        public string Komunikat { get; set; }

        public List<Ostrzezenie> Ostrzezenia { get; set; } = new List<Ostrzezenie>();

        /// <summary>
        /// Wpisy karty urlopowej z OPIS = RequestId po wykonaniu zadania, widziane przez API.
        /// Usluga potwierdza je po SQL (regula: zapisy = faktyczny stan w R2).
        /// </summary>
        public List<ZapisR2> Zapisy { get; set; } = new List<ZapisR2>();

        /// <summary>Przy usunieciu: identyfikatory, ktorych nie bylo pod zatrudnieniem.</summary>
        public List<int> NieZnalezione { get; set; } = new List<int>();

        /// <summary>USERS.X_I zalogowanego konta — do rozpoznawania wlasnych wpisow (X_F7).</summary>
        public int UzytkownikId { get; set; }

        public string WersjaBiblioteki { get; set; }
        public long CzasMs { get; set; }

        /// <summary>Kroki wykonania — do dziennika connectora, nie do tenanta.</summary>
        public List<string> Przebieg { get; set; } = new List<string>();
    }

    public sealed class ZapisR2
    {
        /// <summary>KART_URL.X_I.</summary>
        public int Id { get; set; }
        public string DataOd { get; set; }
        public string DataDo { get; set; }
        public int Dni { get; set; }
        public int Minuty { get; set; }
    }

    public sealed class Ostrzezenie
    {
        public string Kod { get; set; }
        public string Tekst { get; set; }
    }

    public static class Statusy
    {
        public const string Zapisano = "zapisano";
        public const string ZapisanoZOstrzezeniem = "zapisanoZOstrzezeniem";
        public const string Blad = "blad";
        public const string BladPrzejsciowy = "bladPrzejsciowy";
    }

    public static class Kody
    {
        public const string OstrzPrzelomMiesiaca = "OSTRZ_PRZELOM_MIESIACA";
        public const string OstrzLimitPrzekroczony = "OSTRZ_LIMIT_PRZEKROCZONY";
        public const string OstrzNaZadanieWykorzystany = "OSTRZ_NA_ZADANIE_WYKORZYSTANY";
        public const string OstrzInne = "OSTRZ_INNE";

        public const string Kolizja = "R2_KOLIZJA";
        public const string NaZadaniePrzekroczony = "R2_NA_ZADANIE_PRZEKROCZONY";
        public const string Niedostepne = "R2_NIEDOSTEPNE";
        public const string Konfiguracja = "R2_KONFIGURACJA";
        public const string PracownikNieznaleziony = "R2_PRACOWNIK_NIEZNALEZIONY";
        public const string ZatrudnienieNieznalezione = "R2_ZATRUDNIENIE_NIEZNALEZIONE";
        public const string WpisNieDoUsuniecia = "R2_WPIS_NIE_DO_USUNIECIA";
        public const string Nieznany = "R2_NIEZNANY";

        public const string OperacjaNieobslugiwana = "OPERACJA_NIEOBSLUGIWANA";
        public const string ZadanieNiepoprawne = "ZADANIE_NIEPOPRAWNE";
        public const string ConnectorTimeout = "CONNECTOR_TIMEOUT";
        public const string ConnectorAwaria = "CONNECTOR_AWARIA";
    }
}
