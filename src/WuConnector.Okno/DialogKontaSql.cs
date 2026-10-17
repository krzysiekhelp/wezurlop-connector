using WuConnector.Core.Konfiguracja;

namespace WuConnector.Okno;

/// <summary>
/// „Zarządzaj kontami MSSQL” — okno w zakresie serwera, nie jednej instalacji.
///
/// Kolejnosc jest wymuszona: najpierw polaczenie kontem, ktore moze zarzadzac loginami, i dopiero potem
/// reszta. Okno niczego nie zapisuje do konfiguracji connectora — konczy sie na serwerze, a login i haslo
/// uzytkownik wpisuje sam w karcie instalacji (dlatego nowe haslo pokazujemy jawnie, z przyciskiem „Kopiuj”).
/// </summary>
public sealed class DialogKontaSql : Form
{
    readonly Kontekst _k;

    // krok 1 — polaczenie
    readonly ComboBox _serwer = new() { DropDownStyle = ComboBoxStyle.DropDown, Font = Styl.Czcionka(Styl.Tresc) };
    readonly ListaWyboru _logowanie = new();
    readonly TextBox _adminLogin = E.Pole("sa"), _adminHaslo = E.Pole("", true);
    readonly Przycisk _polacz = new() { Text = "Połącz", Width = 150 };
    readonly Przycisk _zmienPolaczenie = new() { Text = "Zmień połączenie", Rodzaj = RodzajPrzycisku.Tekstowy, Width = 180, Visible = false };
    readonly Komunikat _stanPolaczenia = new() { Visible = false };
    Blok _polaPolaczenia = null!;
    Panel _tresc = null!;
    readonly Panel _rzadWyniku = new() { BackColor = Color.Transparent };
    Label? _opisPolaczenia;
    int _poczatekZOpisem, _poczatekBezOpisu;
    Action _ulozKarty = () => { };

    // krok 2 — konta
    readonly Karta _kartaPolaczenia = new();
    readonly Karta _kartaKont = new();
    readonly ListView _konta = new()
    {
        View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle,
        Font = Styl.Czcionka(Styl.Tresc), ForeColor = Styl.Tekst, Height = 300,
    };
    readonly Przycisk _nowe = new() { Text = "Nowe konto…", Rodzaj = RodzajPrzycisku.Drugi, Width = 150 };
    readonly Przycisk _noweHaslo = new() { Text = "Zmień hasło", Rodzaj = RodzajPrzycisku.Drugi, Width = 150 };
    readonly Przycisk _zmienNazwe = new() { Text = "Zmień nazwę…", Rodzaj = RodzajPrzycisku.Drugi, Width = 160 };
    readonly Przycisk _uprawnienia = new() { Text = "Uprawnienia…", Width = 160 };
    readonly Przycisk _usun = new() { Text = "Usuń", Rodzaj = RodzajPrzycisku.Drugi, Width = 110 };



    // krok 3 — dostep do baz
    readonly Panel _bazy = new() { AutoScroll = true, BackColor = Color.White, Height = 150, BorderStyle = BorderStyle.FixedSingle };

    readonly Przycisk _zamknij = new() { Text = "Zamknij", Rodzaj = RodzajPrzycisku.Drugi, Width = 130 };

    KontaSql.StanSerwera? _stan;
    KontaSql.Konto? _wybrane;
    readonly List<CheckBox> _polaBaz = new();

    const int TrybWindows = 0, TrybSql = 1;


    public DialogKontaSql(Kontekst k)
    {
        _k = k;
        // Kontrolki schowane od poczatku musza byc zgloszone ukladowi — on nie czyta tego z systemu (patrz Uklad.Pokaz).
        foreach (var schowana in new Control[] { _zmienPolaczenie, _stanPolaczenia })
            Uklad.Pokaz(schowana, false);
        Text = "Zarządzanie kontami MSSQL";
        Font = Styl.Czcionka(10f);
        BackColor = Styl.Tlo;
        // Tak jak okno glowne: staly rozmiar, a tresc przewija sie w srodku.
        var praca = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 940, 860);
        ClientSize = new Size(920, Math.Min(820, praca.Height - 80));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = Styl.IkonaAplikacji();

        // Tresc przewijana, stopka przyklejona na dole — okno z trzema krokami bywa wyzsze niz ekran.
        int szer = ClientSize.Width - 2 * Uklad.Margines - SystemInformation.VerticalScrollBarWidth;
        int y = Uklad.Margines;
        _tresc = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Styl.Tlo };
        var tresc = _tresc;
        var karty = new[] { KartaPolaczenia(szer, ref y), KartaKont(szer, ref y) };
        tresc.Controls.AddRange(karty);
        // Karta rosnie, gdy pojawi sie w niej komunikat — wtedy te ponizej musza zejsc nizej, a nie dac sie przykryc.
        void Ulozone(object? _, EventArgs __)
        {
            int gora = Uklad.Margines;
            foreach (var karta in karty.Where(Uklad.Widoczny))
            {
                karta.Top = gora;
                gora += karta.Height + Uklad.Srednia;
            }
        }
        foreach (var karta in karty) { karta.SizeChanged += Ulozone; karta.VisibleChanged += Ulozone; }
        _ulozKarty = () => Ulozone(null, EventArgs.Empty);
        _ulozKarty();

        // Szerokosc ustawiamy przed kotwicami — inaczej kotwica liczy odstep od krawedzi panelu sprzed dokowania.
        var stopka = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = Color.White, Width = ClientSize.Width };
        var kreska = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Styl.Linia };
        _zamknij.Location = new Point(ClientSize.Width - Uklad.Margines - _zamknij.Width, 14);
        _zamknij.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _zamknij.Click += (_, _) => Close();
        stopka.Controls.AddRange(new Control[] { _zamknij, kreska });

        Controls.Add(tresc);
        Controls.Add(stopka);

        PokazKroki(false);
    }

    // ---------------------------------------------------------------- krok 1

    Karta KartaPolaczenia(int szer, ref int y)
    {
        var karta = _kartaPolaczenia;
        karta.Location = new Point(Uklad.Margines, y);
        karta.Width = szer;
        int top = Uklad.NaglowekKarty(karta, "1. Połączenie z serwerem",
            "Konto musi móc zarządzać loginami i nadawać uprawnienia (rola sysadmin albo securityadmin). "
            + "Hasło administratora jest używane tylko teraz i nigdzie nie jest zapisywane.", out _opisPolaczenia);

        _serwer.Items.AddRange(_k.Plik?.Instalacje.Select(i => i.SerwerSql).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray()
                               ?? Array.Empty<object>());
        if (_serwer.Items.Count > 0) _serwer.SelectedIndex = 0; else _serwer.Text = ".";
        _logowanie.Items.AddRange(new object[] { $"{Environment.UserDomainName}\\{Environment.UserName}", "Login SQL" });
        _logowanie.SelectedIndex = TrybWindows;
        _logowanie.SelectedIndexChanged += (_, _) => _adminLogin.Enabled = _adminHaslo.Enabled = _logowanie.SelectedIndex == TrybSql;
        _adminLogin.Enabled = _adminHaslo.Enabled = false;

        // Pola i przycisk — chowane po udanym polaczeniu, zeby krok 1 zwinal sie do jednej linii.
        _polaPolaczenia = new Blok(null, null, szer);
        _polaPolaczenia.PolaWiersz(new Pole("Serwer SQL", _serwer), new Pole("Logowanie", _logowanie));
        _polaPolaczenia.PolaWiersz(new Pole("Login administratora", _adminLogin), new Pole("Hasło", _adminHaslo));
        _polaPolaczenia.Przyciski(_polacz);
        _polaPolaczenia.Location = new Point(0, top);
        karta.Controls.Add(_polaPolaczenia.Domknij());

        // Wynik zostaje widoczny takze po zwinieciu — wtedy jest jedyna trescia kroku 1, w jednej linii z przyciskiem.
        int szerokoscTresci = szer - 2 * Uklad.Margines;
        _stanPolaczenia.Width = szerokoscTresci - _zmienPolaczenie.Width - 12;
        _zmienPolaczenie.Location = new Point(_stanPolaczenia.Width + 12, 0);
        _rzadWyniku.Width = szerokoscTresci;
        _rzadWyniku.Height = Math.Max(_stanPolaczenia.Height, Styl.WysokoscKontrolki);
        _rzadWyniku.Controls.AddRange(new Control[] { _stanPolaczenia, _zmienPolaczenie });
        _stanPolaczenia.SizeChanged += (_, _) =>
        {
            _rzadWyniku.Height = Math.Max(_stanPolaczenia.Height, Styl.WysokoscKontrolki);
            _kartaPolaczenia.Przelicz();
            _ulozKarty();
        };

        var stan = new Blok(null, null, szer);
        stan.Wiersz(_rzadWyniku, Uklad.Mala);
        stan.Location = new Point(0, top + _polaPolaczenia.Height);
        karta.Controls.Add(stan.Domknij());

        _poczatekZOpisem = top;
        _poczatekBezOpisu = 20 + 24 + Uklad.Duza;   // sam tytul, bez zdania wyjasnienia
        karta.PoczatekTresci = top;
        karta.Height = top + _polaPolaczenia.Height + stan.Height + Uklad.Srednia;
        _polacz.Click += async (_, _) => await PolaczAsync();
        _zmienPolaczenie.Click += (_, _) => PokazKroki(false);

        y += karta.Height + Uklad.Srednia;
        return karta;
    }

    KontaSql.Dostep Dostep() => new(_serwer.Text.Trim(),
        _logowanie.SelectedIndex == TrybSql ? _adminLogin.Text.Trim() : null,
        _logowanie.SelectedIndex == TrybSql ? _adminHaslo.Text : null);

    /// <summary>Stan serwera po polaczeniu — na potrzeby zrzutow dokumentacyjnych.</summary>
    public KontaSql.StanSerwera? StanSerwera => _stan;

    /// <summary>Nazwa instalacji dla prefiksu baz — na potrzeby zrzutow dokumentacyjnych.</summary>
    public string NazwaInstalacjiDlaZrzutu(string prefiks) => NazwaInstalacji(prefiks);

    public async Task PolaczAsync()
    {
        _polacz.Enabled = false;
        Pokaz(_stanPolaczenia, "Łączę z serwerem…", Poziom.Info);
        try
        {
            _stan = await KontaSql.WczytajAsync(Dostep());
            Pokaz(_stanPolaczenia,
                  // Liczby kont i baz widac nizej na liscie — tu wystarczy, z czym jestesmy polaczeni.
                  $"✓ Połączono z {_serwer.Text.Trim()}", Poziom.Ok);
            Odblokuj();
        }
        catch (UnauthorizedAccessException e) { PokazKroki(false); Pokaz(_stanPolaczenia, e.Message, Poziom.Blad); }
        catch (Exception e) { PokazKroki(false); Pokaz(_stanPolaczenia, "Nie udało się połączyć: " + e.Message, Poziom.Blad); }
        finally { _polacz.Enabled = true; }
    }

    // ---------------------------------------------------------------- krok 2

    Karta KartaKont(int szer, ref int y)
    {
        _kartaKont.Location = new Point(Uklad.Margines, y);
        _kartaKont.Width = szer;
        int top = Uklad.NaglowekKarty(_kartaKont, "2. Konta na serwerze",
            "Konta SQL Servera — tylko takim loguje się connector. Wybierz to, któremu chcesz nadać odczyt, "
            + "albo załóż nowe; nazwa może być dowolna, wu_* podpowiadamy po to, żeby łatwo je rozpoznać.");

        _konta.Width = szer - 2 * Uklad.Margines;
        // Nazwa bierze cale wolne miejsce — inaczej po prawej zostaje pusta kolumna.
        _konta.Columns.Add("KONTO", _konta.Width - 130 - 160 - 24);
        _konta.Columns.Add("STAN", 130);
        _konta.Columns.Add("ODCZYT BAZ R2", 160);
        _konta.SelectedIndexChanged += (_, _) => WybierzKonto();

        // Nazwa konta zapada w osobnym oknie modalnym — dopoki jest otwarte, tego okna nie da sie uzyc.
        _nowe.Click += async (_, _) => await UtworzAsync();
        _zmienNazwe.Click += async (_, _) => await ZmienNazweAsync();
        _uprawnienia.Click += async (_, _) => await UprawnieniaAsync();
        _noweHaslo.Click += async (_, _) => await NoweHasloAsync();
        _usun.Click += async (_, _) => await UsunAsync();

        var b = new Blok(null, null, szer);
        b.Wiersz(_konta, Uklad.Srednia);
        b.Przyciski(_nowe, _noweHaslo, _zmienNazwe, _uprawnienia, _usun);
        b.Location = new Point(0, top);
        _kartaKont.Controls.Add(b.Domknij());
        _kartaKont.Height = top + b.Height + Uklad.Srednia;

        y += _kartaKont.Height + Uklad.Srednia;
        return _kartaKont;
    }

    string PodpowiedzNazwy()
    {
        var prefiks = _stan?.Bazy.FirstOrDefault()?.Prefiks;
        string proponowana = prefiks is null ? KontoOdczytuSql.DomyslnyLogin : "wu_" + prefiks.ToLowerInvariant();
        // Gdy taka juz jest, dokladamy koncowke — zeby nie nadpisac hasla dzialajacemu kontu.
        if (_stan?.Konta.Any(k => k.Nazwa.Equals(proponowana, StringComparison.OrdinalIgnoreCase)) == true)
            proponowana += "_2";
        return proponowana;
    }

    void WybierzKonto()
    {
        _wybrane = _konta.SelectedItems.Count > 0 ? _konta.SelectedItems[0].Tag as KontaSql.Konto : null;
        bool jest = _wybrane is not null;
        _noweHaslo.Enabled = jest && !_wybrane!.Windows && !_wybrane.Systemowe;
        _zmienNazwe.Enabled = jest && !_wybrane!.Windows && !_wybrane.Systemowe;
        // Konta Windows usuwa system albo domena, nie my — stad mozna im tylko odebrac uprawnienia.
        _usun.Enabled = jest && !_wybrane!.Windows && !_wybrane.Systemowe && !_wybrane.Administrator;
        _uprawnienia.Enabled = jest;

    }

    // ---------------------------------------------------------------- krok 3
    string NazwaInstalacji(string prefiks)
    {
        var i = _k.Plik?.Instalacje.FirstOrDefault(x => KontaSql.Prefiks(x.BazaBin).Equals(prefiks, StringComparison.OrdinalIgnoreCase));
        return i is null ? $"bazy {prefiks}_*" : $"{i.Nazwa}   ·   bazy {prefiks}_*";
    }


    async Task UtworzAsync()
    {
        if (_stan is null) return;
        if (DialogNoweKonto.Nowe(this, PodpowiedzNazwy(), Nazwy()) is not { } okno) return;
        try
        {
            _nowe.Enabled = false;
            string haslo = await KontaSql.UtworzAsync(Dostep(), okno.Nazwa, okno.Windows, okno.Haslo);
            if (haslo != "")
                DialogHasla.Pokaz(this, okno.Nazwa, haslo,
                    "Wpisz je w karcie instalacji, w polu „Hasło” — razem z loginem tego konta.");
            await OdswiezAsync(okno.Nazwa);
            Komunikat("Konto założone", $"Konto {okno.Nazwa} jest na serwerze.",
                      "Zostaje nadać mu dostęp do baz — przycisk „Uprawnienia…”.");
        }
        catch (Exception e) { Komunikat("Nie założono konta", e.Message); }
        finally { _nowe.Enabled = true; }
    }

    List<string> Nazwy() => _stan?.Konta.Select(k => k.Nazwa).ToList() ?? new();

    async Task NoweHasloAsync()
    {
        if (_wybrane is null) { Komunikat("Nie wybrano konta", "Zaznacz konto na liście, a potem powtórz operację."); return; }

        // Pytamy zawsze: nowe haslo uniewaznia stare wszedzie, gdzie bylo uzyte, a konta connectora
        // sa wpisane w konfiguracji instalacji — tam trzeba je potem poprawic recznie.
        string nazwa = _wybrane.Nazwa;
        string uzywane = UzywanePrzez(nazwa);
        if (!DialogPytania.Pytanie(this, "Zmiana hasła",
                $"Ustawić nowe hasło dla konta {nazwa}? Stare przestanie działać wszędzie, gdzie było użyte.",
                "Nowe hasło pokażemy tylko raz." + (uzywane == "" ? "" : uzywane.Replace("\n\nUWAGA: ", " ")),
                "Zmień hasło")) return;
        try
        {
            _noweHaslo.Enabled = false;
            string haslo = await KontaSql.UstawHasloAsync(Dostep(), _wybrane);
            DialogHasla.Pokaz(this, nazwa, haslo, uzywane == ""
                ? "Wpisz je w karcie instalacji, w polu „Hasło”, jeśli to konto ma czytać bazy connectora."
                : "To konto jest używane w konfiguracji — wpisz nowe hasło w karcie instalacji, inaczej synchronizacja stanie.");
            await OdswiezAsync(nazwa);
        }
        catch (Exception e) { Komunikat("Nie zmieniono hasła", e.Message); }
        finally { _noweHaslo.Enabled = true; }
    }

    async Task ZmienNazweAsync()
    {
        if (_wybrane is null || _stan is null) return;
        if (DialogNoweKonto.ZmianaNazwy(this, _wybrane.Nazwa, Nazwy()) is not { } okno) return;
        string nowa = okno.Nazwa;
        string uzywane = UzywanePrzez(_wybrane.Nazwa);
        if (uzywane != "" && !Potwierdz(
                $"Zmienić nazwę konta {_wybrane.Nazwa} na {nowa}?{uzywane}\n\n"
                + "Po zmianie trzeba poprawić login w karcie instalacji, inaczej connector przestanie czytać bazy.")) return;
        try
        {
            _zmienNazwe.Enabled = false;
            await KontaSql.ZmienNazweAsync(Dostep(), _wybrane, nowa, _stan.Bazy);
            await OdswiezAsync(nowa);
            Komunikat("Nazwa zmieniona", $"Konto nazywa się teraz {nowa}.",
                      uzywane == "" ? null : "Popraw login w karcie instalacji — connector nadal szuka starej nazwy.");
        }
        catch (Exception e) { Komunikat("Nie zmieniono nazwy", e.Message); }
        finally { _zmienNazwe.Enabled = true; }
    }

    async Task UsunAsync()
    {
        if (_wybrane is null || _stan is null) { Komunikat("Nie wybrano konta", "Zaznacz konto na liście, a potem powtórz operację."); return; }
        if (_wybrane.Windows)
        {
            // Konto Windows (lokalne albo domenowe) nalezy do systemu — SQL Server trzyma tylko jego uprawnienia.
            Komunikat("Konto Windows", $"{_wybrane.Nazwa} to konto Windows — usuwa się je w systemie albo w domenie.",
                      "Stąd można mu tylko nadać albo odebrać uprawnienia do baz.");
            return;
        }

        string nazwa = _wybrane.Nazwa;
        string ile = _wybrane.BazyZOdczytem.Count == 0
            ? "Konto nie czyta żadnej bazy R2Płatnika."
            : $"Konto czyta {_wybrane.BazyZOdczytem.Count} baz(y) R2Płatnika — dostęp zniknie razem z nim.";
        string uzywane = UzywanePrzez(nazwa);
        if (!DialogPytania.Pytanie(this, "Usunięcie konta",
                $"Usunąć konto {nazwa} z serwera {_serwer.Text.Trim()}? Tej operacji nie da się cofnąć.",
                ile + (uzywane == "" ? "" : uzywane.Replace("\n\nUWAGA: ", " ")),
                "Usuń konto", grozny: true)) return;
        try
        {
            _usun.Enabled = false;
            await KontaSql.UsunAsync(Dostep(), _wybrane, _stan.Bazy);
            Komunikat("Konto usunięte", $"Konto {nazwa} zostało usunięte z serwera.");
            await OdswiezAsync(null);
        }
        catch (Exception e) { Komunikat("Nie usunięto konta", e.Message); }
        finally { _usun.Enabled = true; }
    }

    /// <summary>Ostrzezenie, gdy konto jest wpisane w konfiguracji — po usunieciu instalacja przestanie czytac bazy.</summary>
    string UzywanePrzez(string login)
    {
        var uzywa = _k.Plik?.Instalacje.Where(i => login.Equals(i.LoginSql, StringComparison.OrdinalIgnoreCase)).Select(i => i.Nazwa).ToList() ?? new();
        return uzywa.Count == 0 ? "" : $"\n\nUWAGA: tego konta używa {string.Join(", ", uzywa)} — synchronizacja przestanie działać.";
    }

    /// <summary>Uprawnienia ustawia osobne okno modalne; tu tylko wykonujemy to, na co sie w nim zgodzono.</summary>
    async Task UprawnieniaAsync()
    {
        if (_wybrane is null || _stan is null) return;
        if (DialogUprawnien.Pokaz(this, _wybrane, _stan.Bazy, NazwaInstalacji) is not { } zaznaczone) return;
        try
        {
            _uprawnienia.Enabled = false;
            await KontaSql.UstawOdczytAsync(Dostep(), _wybrane, _stan.Bazy, zaznaczone);
            string opis = zaznaczone.Count == 0
                ? $"Konto {_wybrane.Nazwa} nie czyta już żadnej bazy R2Płatnika."
                : $"Konto {_wybrane.Nazwa} czyta teraz {zaznaczone.Count} {Odmiana(zaznaczone.Count)} R2Płatnika.";
            await OdswiezAsync(_wybrane.Nazwa);
            Komunikat("Uprawnienia zapisane", opis);
        }
        catch (Exception e) { Komunikat("Nie zmieniono uprawnień", e.Message); }
        finally { _uprawnienia.Enabled = true; }
    }

    static string Odmiana(int ile) => ile == 1 ? "bazę" : ile % 10 is >= 2 and <= 4 && ile % 100 is < 12 or > 14 ? "bazy" : "baz";

    /// <summary>Po kazdej zmianie czytamy stan serwera od nowa — lista ma pokazywac to, co jest, a nie to, co zlecilismy.</summary>
    /// <summary>Po kazdej zmianie czytamy stan serwera od nowa — lista ma pokazywac to, co jest, a nie to, co zlecilismy.</summary>
    async Task OdswiezAsync(string? zaznacz)
    {
        _stan = await KontaSql.WczytajAsync(Dostep());
        WypelnijKonta(zaznacz);
    }

    /// <summary>Kazda operacja konczy sie jednym oknem — nie dublujemy komunikatu w karcie pod spodem.</summary>
    void Komunikat(string tytul, string tresc, string? szczegol = null)
        => DialogPytania.Informacja(this, tytul, tresc, szczegol);

    void WypelnijKonta(string? zaznacz)
    {
        _konta.BeginUpdate();
        _konta.Items.Clear();
        // Tylko konta SQL Servera. Konta Windows (lokalne i domenowe) nalezą do systemu: hasla ani nazwy
        // im stad nie zmienimy, a connector i tak laczy sie loginem SQL — na liscie tylko myliłyby.
        foreach (var konto in (_stan?.Konta ?? new List<KontaSql.Konto>()).Where(k => !k.Windows))
        {
            var it = new ListViewItem(new[]
            {
                konto.Nazwa,
                konto.Administrator ? "administrator" : konto.Wylaczony ? "wyłączone" : "aktywne",
                $"{konto.BazyZOdczytem.Count} z {_stan!.Bazy.Count}",
            })
            { Tag = konto };
            if (konto.Systemowe || konto.Administrator) it.ForeColor = Styl.Szary;
            else if (konto.Connectorowe) it.ForeColor = Styl.Granat;
            _konta.Items.Add(it);
            if (zaznacz is not null && konto.Nazwa.Equals(zaznacz, StringComparison.OrdinalIgnoreCase))
            { it.Selected = true; it.EnsureVisible(); }
        }
        _konta.EndUpdate();
        WybierzKonto();
    }

    // ---------------------------------------------------------------- stan okna

    /// <summary>
    /// Kreator: dopoki nie ma polaczenia, widac wylacznie krok 1. Po polaczeniu krok 1 zwija sie do samego
    /// wyniku (z przyciskiem „Zmień połączenie”), a odslaniaja sie kroki 2 i 3 — jeden ekran, jedna decyzja.
    /// </summary>
    void PokazKroki(bool polaczono)
    {
        // Po połączeniu krok 1 schodzi do jednej linii: znika opis i pola, zostaje wynik z przyciskiem obok.
        Uklad.Pokaz(_polaPolaczenia, !polaczono);
        _zmienPolaczenie.Visible = polaczono;
        if (_opisPolaczenia is not null) _opisPolaczenia.Visible = !polaczono;
        _kartaPolaczenia.PoczatekTresci = polaczono ? _poczatekBezOpisu : _poczatekZOpisem;
        Uklad.Pokaz(_kartaKont, polaczono);
        if (!polaczono)
        {
            _stan = null;
            _wybrane = null;


        }
        _kartaPolaczenia.Przelicz();
        _ulozKarty();
    }

    void Odblokuj()
    {
        PokazKroki(true);
        // Po połączeniu od razu stajemy na koncie connectora — najczęściej po nie się tu przychodzi.
        WypelnijKonta(_stan?.Konta.FirstOrDefault(k => k.Connectorowe)?.Nazwa);
    }

    static void Pokaz(Komunikat k, string tekst, Poziom p)
    {
        k.Text = tekst;
        k.Poziom = p;
        Uklad.Pokaz(k, true);
    }

    bool Potwierdz(string pytanie)
        => DialogPytania.Pytanie(this, "Zarządzanie kontami MSSQL", pytanie, null, "Tak, kontynuuj");
}
