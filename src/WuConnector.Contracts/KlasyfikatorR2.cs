using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WuConnector.Contracts
{
    /// <summary>
    /// R2 nie ma kodow bledow — zwraca wylacznie tekst przez GetLastError.
    /// Tu tekst zamieniamy na kod protokolu. Wzorce z komunikatow zaobserwowanych
    /// na R2 10.10.33 (USTALENIA 16.x, 22.x). Oryginal zawsze idzie dalej w polu Komunikat.
    /// </summary>
    public static class KlasyfikatorR2
    {
        /// <summary>Blad ConnectBin: brak/uszkodzone .ini, .lic, .prt albo baza. Nie mija sam.</summary>
        public static (string Status, string Kod) BladPolaczenia(string komunikat)
            => (Statusy.Blad, Kody.Konfiguracja);

        /// <summary>
        /// LoginUser zwraca 0 zarowno przy zlym hasle, jak i przy blokadzie konta czy licencji.
        /// Rozroznia je wylacznie tresc.
        /// </summary>
        public static (string Status, string Kod) BladLogowania(string komunikat)
        {
            string t = Normalizuj(komunikat);

            // "Uzytkownik X jest zalogowany na HOST" — czlowiek na koncie connectora. Przejsciowe.
            if (t.Contains("jest zalogowany"))
                return (Statusy.BladPrzejsciowy, Kody.Niedostepne);

            // Zle haslo / brak konta — nie mija samo, ponawianie nic nie da.
            // "Nie mozna zlokalizowac uzytkownika X" — konto nie istnieje w tej instalacji (R2 10.10.33, 19.09.2026).
            if (t.Contains("hasl") || t.Contains("nie istnieje") || t.Contains("nieprawidlow") || t.Contains("niepoprawn")
                || t.Contains("zlokalizowac uzytkownika"))
                return (Statusy.Blad, Kody.Konfiguracja);

            // Nierozpoznane (np. brak licencji) — traktujemy jako przejsciowe; usluga alarmuje,
            // jesli stan utrzymuje sie dluzej.
            return (Statusy.BladPrzejsciowy, Kody.Niedostepne);
        }

        /// <summary>TablePost zwrocil false — nic nie zostalo zapisane.</summary>
        public static string BladZapisu(string komunikat)
        {
            string t = Normalizuj(komunikat);
            if (t.Contains("pokrywa sie")) return Kody.Kolizja;
            if (t.Contains("na zadanie") && t.Contains("przekroczony")) return Kody.NaZadaniePrzekroczony;
            return Kody.Nieznany;
        }

        /// <summary>
        /// Ostrzezenia po UDANYM TablePost. R2 potrafi zwrocic kilka naraz;
        /// kazde rozpoznajemy osobno, nierozpoznane oznaczamy OSTRZ_INNE.
        /// </summary>
        public static List<Ostrzezenie> Ostrzezenia(string komunikat)
        {
            var wynik = new List<Ostrzezenie>();
            if (string.IsNullOrWhiteSpace(komunikat)) return wynik;

            var linie = komunikat.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            foreach (var linia in linie)
            {
                string t = Normalizuj(linia);
                string kod =
                    t.Contains("przelomie miesiaca") ? Kody.OstrzPrzelomMiesiaca :
                    t.Contains("urlopu wypoczynkowego") && t.Contains("przekroczony") ? Kody.OstrzLimitPrzekroczony :
                    t.Contains("na zadanie") && t.Contains("wykorzystany") ? Kody.OstrzNaZadanieWykorzystany :
                    Kody.OstrzInne;
                wynik.Add(new Ostrzezenie { Kod = kod, Tekst = linia });
            }
            return wynik;
        }

        /// <summary>
        /// Kod wyniku z listy ostrzezen: wazniejsze dla kadr wygrywa.
        /// Przelom miesiaca jest informacyjny — liczba czesci i tak jest w Zapisy.
        /// </summary>
        public static string NajwazniejszeOstrzezenie(IEnumerable<Ostrzezenie> ostrzezenia)
        {
            string[] kolejnosc =
            {
                Kody.OstrzLimitPrzekroczony,
                Kody.OstrzNaZadanieWykorzystany,
                Kody.OstrzInne,
                Kody.OstrzPrzelomMiesiaca,
            };
            var kody = new HashSet<string>(ostrzezenia.Select(o => o.Kod));
            return kolejnosc.FirstOrDefault(kody.Contains);
        }

        /// <summary>Male litery, bez polskich znakow, pojedyncze spacje.</summary>
        public static string Normalizuj(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
            {
                switch (c)
                {
                    case 'ą': sb.Append('a'); break;
                    case 'ć': sb.Append('c'); break;
                    case 'ę': sb.Append('e'); break;
                    case 'ł': sb.Append('l'); break;
                    case 'ń': sb.Append('n'); break;
                    case 'ó': sb.Append('o'); break;
                    case 'ś': sb.Append('s'); break;
                    case 'ź':
                    case 'ż': sb.Append('z'); break;
                    default: sb.Append(char.IsWhiteSpace(c) ? ' ' : c); break;
                }
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " +", " ");
        }
    }
}
