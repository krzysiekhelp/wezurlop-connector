using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WuConnector.Contracts
{
    public static class WalidatorZadania
    {
        /// <summary>Ograniczenie KART_URL.OPIS i protokolu tenanta.</summary>
        static readonly Regex WzorRequestId = new Regex("^[A-Za-z0-9-]{1,32}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Rodzaje (R_DNIA), ktore connector zapisuje w etapie 1: 0 wypoczynkowy, 5 na zadanie.
        /// Rozszerzenie listy to swiadoma decyzja, nie przypadek.
        /// </summary>
        public static readonly int[] DozwoloneKody = { 0, 5 };

        /// <summary>Null, gdy zadanie jest poprawne; inaczej opis bledu.</summary>
        public static string Sprawdz(ZadanieWorkera z)
        {
            if (z == null) return "brak zadania";
            if (string.IsNullOrWhiteSpace(z.KatalogBin)) return "brak katalogBin";
            if (string.IsNullOrWhiteSpace(z.Login)) return "brak loginu";
            if (z.FirmIde <= 0) return "firmIde musi byc > 0";

            switch (z.Operacja)
            {
                case Operacje.Test:
                    return null;

                case Operacje.Zapis:
                    if (z.PracownikId <= 0 || z.ZatrudnienieId <= 0) return "brak pracownikId/zatrudnienieId";
                    if (z.RequestId == null || !WzorRequestId.IsMatch(z.RequestId)) return "requestId niezgodny z [A-Za-z0-9-]{1,32}";
                    if (Array.IndexOf(DozwoloneKody, z.Kod) < 0) return "kod " + z.Kod + " poza dozwolonymi";
                    if (!ParsujDate(z.DataOd, out var od) || !ParsujDate(z.DataDo, out var @do)) return "daty musza byc w formacie yyyy-MM-dd";
                    if (@do < od) return "dataDo wczesniejsza niz dataOd";
                    if ((@do - od).TotalDays > 366) return "zakres dluzszy niz rok";
                    return null;

                case Operacje.Usuniecie:
                    if (z.PracownikId <= 0 || z.ZatrudnienieId <= 0) return "brak pracownikId/zatrudnienieId";
                    if (z.Zapisy == null || z.Zapisy.Count == 0) return "brak zapisow do usuniecia";
                    if (z.Zapisy.Exists(id => id <= 0)) return "niepoprawny identyfikator zapisu";
                    return null;

                default:
                    return null; // operacja nieznana — obsluzona osobno jako OPERACJA_NIEOBSLUGIWANA
            }
        }

        public static bool ParsujDate(string s, out DateTime data)
            => DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out data);

        public static string FormatujDate(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
