namespace WuConnector.Okno;

/// <summary>
/// Instrukcja connectora w oknie — zeby nie trzeba bylo szukac dokumentacji na dysku.
/// Opisuje to, co uzytkownik ma przed soba: zakladki, kolejnosc konfiguracji, pliki na dysku
/// i najczestsze problemy. Sciezki czytamy z kontekstu, wiec pokazuja prawde tej instalacji.
/// </summary>
public sealed class EkranPomoc : Ekran
{
    public override string Tytul => "Pomoc";
    public override string Podtytul => "Jak skonfigurować connector i co robić, gdy coś nie działa";

    public EkranPomoc(Kontekst k)
    {
        int szer = Uklad.SzerokoscTresci;

        // ---------- czym to jest
        Karta("Czym jest connector",
              "Łącznik między programem kadrowym R2Płatnik a aplikacją wezurlop.", b =>
        {
            Akapit(b, "Connector pracuje jako usługa Windows na serwerze, na którym stoi R2Płatnik. "
                    + "Co jakiś czas (według harmonogramu) wykonuje jeden cykl: czyta dane kadrowe z bazy SQL, "
                    + "wysyła zmiany do aplikacji wezurlop i pobiera z niej zaakceptowane wnioski urlopowe, "
                    + "które zapisuje do karty urlopowej R2Płatnika.");
            Akapit(b, "To connector łączy się z wezurlop, nigdy odwrotnie. Serwer nie musi mieć publicznego adresu "
                    + "ani otwartych portów — wystarczy, że ma dostęp do internetu.");
            Punkt(b, "Dane kadrowe czyta bezpośrednio z bazy SQL — tylko do odczytu.");
            Punkt(b, "Urlopy zapisuje wyłącznie przez bibliotekę R2Płatnika, nigdy wprost do bazy. "
                   + "Dzięki temu program sam przelicza dni i godziny oraz aktualizuje kartę pracy.");
        });

        // ---------- pierwsze uruchomienie
        Karta("Pierwsze uruchomienie — po kolei",
              "Każdy krok zapisuje się osobno. Nie trzeba wypełnić wszystkiego naraz.", b =>
        {
            Krok(b, 1, "Połączenie",
                 "Wpisz adres aplikacji wezurlop tej firmy i klucz instalacji, który wydaje jej administrator. "
                 + "Sprawdź przyciskiem „Testuj połączenie” i zapisz.");
            Krok(b, 2, "R2 Płatnik — znajdź instalację",
                 "Przyciskiem „Szukaj instalacji…” wskaż katalog, w którym stoi R2Płatnik. Connector sam odczyta "
                 + "wersję programu, wersję bazy i to, czy jest biblioteka API.");
            Krok(b, 3, "Konto SQL do odczytu",
                 "Usługa pracuje jako konto systemowe, które nie ma dostępu do baz R2. Przyciskiem "
                 + "„Zarządzaj kontami MSSQL…” załóż osobne konto tylko do odczytu, a potem wpisz je "
                 + "w sekcji „Połączenie z bazą programu kadrowego”.");
            Krok(b, 4, "Konto w R2Płatniku",
                 "Podaj login i hasło konta, którym connector zapisuje urlopy. Konto musi być dedykowane — "
                 + "R2 nie pozwala zalogować tego samego użytkownika dwa razy, więc człowiek zalogowany "
                 + "na tym koncie zablokuje connector.");
            Krok(b, 5, "Firmy do synchronizacji",
                 "Zaznacz firmy, które mają trafiać do wezurlop. Zaznaczenie zapisuje się od razu, bez przycisku.");
            Krok(b, 6, "Harmonogram i start",
                 "Ustaw, jak często connector ma się synchronizować, zapisz, a potem uruchom usługę "
                 + "przyciskiem na zakładce Status.");
        });

        // ---------- codzienna praca
        Karta("Codzienna praca",
              "Na co patrzeć i co można zrobić ręcznie.", b =>
        {
            Punkt(b, "Zakładka Status pokazuje, czy usługa pracuje i kiedy każda firma miała ostatni "
                   + "zakończony cykl. „Aktualne” znaczy, że cykl zmieścił się w harmonogramie.");
            Punkt(b, "„Synchronizuj teraz” zleca cykl natychmiast, nie czekając na harmonogram. "
                   + "Wynik pojawia się w tabeli firm i w dzienniku po kilku sekundach.");
            Punkt(b, "Karta „Ostatnie ostrzeżenia i błędy” pokazuje to, co wymaga uwagi. Przycisk "
                   + "„Wyczyść” chowa dotychczasowe wpisy — od tej chwili widać tylko nowe. "
                   + "Nic nie jest kasowane: wszystko zostaje w zakładce Dziennik.");
            Punkt(b, "Zakładka Podgląd wysyłki pokazuje, co dokładnie poszłoby do wezurlop, bez wysyłania.");
        });

        // ---------- pliki na dysku
        Karta("Gdzie co leży",
              "Ścieżki tej instalacji — przydają się przy kopii zapasowej i przy zgłoszeniu problemu.", b =>
        {
            Sciezka(b, "Plik konfiguracji", k.SciezkaKonfiguracji);
            Sciezka(b, "Dziennik usługi", k.KatalogDziennika);
            Sciezka(b, "Dziennik okna", DziennikOkna.Katalog);
            Sciezka(b, "Stan synchronizacji", k.SciezkaStanu);
            Akapit(b, "Hasła i klucz w pliku konfiguracji są zaszyfrowane kluczem tej maszyny — po przeniesieniu "
                    + "pliku na inny komputer nie dadzą się odczytać. Przy przenosinach trzeba osobno przenieść "
                    + "sekret tożsamości (zakładka Połączenie), inaczej te same osoby założą się w wezurlop po raz drugi.");
        });

        // ---------- prywatnosc
        Karta("Co connector wysyła, a czego nie",
              "Zakres danych jest zawężony celowo.", b =>
        {
            Punkt(b, "Wysyła: nazwiska i imiona pracowników zatrudnionych na umowę o pracę, ich zatrudnienia, "
                   + "limity urlopowe, nieobecności, słowniki rodzajów i kalendarze.");
            Punkt(b, "Nie wysyła: numerów PESEL, adresów, kwot wynagrodzeń ani kodów chorób. "
                   + "Identyfikator osoby powstaje z sekretu tej instalacji, więc z samego identyfikatora "
                   + "nie da się odtworzyć numeru.");
            Punkt(b, "Do R2Płatnika zapisuje wyłącznie urlop do karty urlopowej. Niczego innego nie zmienia.");
            Punkt(b, "W dzienniku connectora nie ma treści danych kadrowych ani haseł. Dziennik leży na serwerze klienta "
                   + "i widzi go ten, kto ma dostęp do connectora — czyli i tak do danych kadrowych.");
        });

        // ---------- aktualizacja
        Karta("Aktualizacja",
              "Nowa wersja instalowana paczką MSI.", b =>
        {
            Punkt(b, "Aktualizacja zatrzymuje usługę, podmienia pliki i uruchamia ją z powrotem. "
                   + "Konfiguracja, sekret i stan synchronizacji zostają nietknięte.");
            Punkt(b, "Pierwsza instalacja nie uruchamia usługi — robi to okno po zapisaniu konfiguracji.");
            Punkt(b, "Numer wersji widać na dole menu po lewej stronie.");
        });

        // ---------- problemy
        Karta("Gdy coś nie działa",
              "Objaw i co z nim zrobić.", b =>
        {
            Problem(b, "„Użytkownik X jest zalogowany”",
                    "Ktoś pracuje w R2Płatniku na koncie connectora. Konto musi być dedykowane — "
                    + "załóż osobne dla connectora i nie używaj go do pracy.");
            Problem(b, "„Błędne hasło użytkownika X”",
                    "Hasło konta R2 zmieniło się albo zostało źle wpisane. Popraw je w sekcji "
                    + "„Konto connectora w R2Płatniku” i sprawdź przyciskiem „Testuj logowanie”.");
            Problem(b, "„odczyt SQL nieudany”",
                    "Konto SQL nie ma dostępu do bazy albo zmieniło hasło. Sprawdź przyciskiem „Testuj SQL”; "
                    + "hasło konta wu_* ustawisz w oknie „Zarządzaj kontami MSSQL…”.");
            Problem(b, "Firma czeka na zatwierdzenie",
                    "Connector zgłosił firmę do wezurlop, ale administrator aplikacji jeszcze jej nie zatwierdził. "
                    + "Synchronizacja ruszy sama po zatwierdzeniu.");
            Problem(b, "„Klucz nieprawidłowy albo wygasł”",
                    "Klucz instalacji został unieważniony albo zastąpiony nowym. Poproś administratora wezurlop "
                    + "o nowy i wpisz go w zakładce Połączenie.");
            Problem(b, "Status pokazuje „opóźnione”",
                    "Ostatni cykl jest starszy, niż przewiduje harmonogram. Sprawdź, czy usługa pracuje, "
                    + "i zajrzyj do ostatnich ostrzeżeń.");
            Akapit(b, "Gdy problem nie mija: zakładka Dziennik pokazuje przebieg po kolei. Przy zgłoszeniu "
                    + "do wsparcia podaj wersję connectora, godzinę zdarzenia i treść komunikatu — "
                    + "nie wysyłaj pliku konfiguracji, bo zawiera zaszyfrowane hasła.");
        });

        // --- pomocnicy skladu ---
        void Karta(string tytul, string opis, Action<Blok> tresc)
        {
            var karta = new Karta { Width = szer };
            int y = Uklad.NaglowekKarty(karta, tytul, opis);
            var b = new Blok(null, null, szer, zLinia: false);
            tresc(b);
            b.Location = new Point(0, y);
            karta.Controls.Add(b.Domknij());
            karta.Height = y + b.Height + Uklad.Mala;
            Dodaj(karta);
        }

        static void Akapit(Blok b, string t) => b.Wiersz(E.Tekst(t), Uklad.Srednia);

        static void Punkt(Blok b, string t) => b.Wiersz(E.Tekst("•   " + t), Uklad.Mala);

        static void Krok(Blok b, int nr, string tytul, string t)
        {
            b.Wiersz(E.Tekst($"{nr}.   {tytul}", true), 2);
            b.Wiersz(E.Opis(t), Uklad.Srednia);
        }

        static void Problem(Blok b, string objaw, string rada)
        {
            b.Wiersz(E.Tekst(objaw, true), 2);
            b.Wiersz(E.Opis(rada), Uklad.Srednia);
        }

        static void Sciezka(Blok b, string co, string gdzie)
        {
            b.Wiersz(E.Opis(co), 0);
            var p = E.Tekst(gdzie);
            p.Font = new Font("Consolas", 9.5f);
            b.Wiersz(p, Uklad.Mala);
        }
    }
}
