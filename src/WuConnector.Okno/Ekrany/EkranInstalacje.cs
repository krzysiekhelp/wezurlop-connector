using WuConnector.Contracts;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Zadania;

namespace WuConnector.Okno;

/// <summary>
/// Dwie sekcje: u gory firmy, ktore connector synchronizuje (z konfiguracji), nizej szukanie instalacji
/// na tym serwerze. Szukanie nie startuje samo — dopiero na przycisk; znalezione instalacje juz
/// skonfigurowane sa pomijane, zeby ta sama nie pojawila sie dwa razy.
/// </summary>
public sealed class EkranInstalacje : Ekran
{
    readonly Kontekst _k;
    readonly TextBox _katalog;
    readonly Przycisk _wykryj = new() { Text = "Szukaj instalacji", Width = 180 };
    readonly Label _wynikWykrycia = E.Opis("");
    readonly Karta _kartaSzukania = new() { Height = 170, Visible = false };
    readonly Karta _pasekGorny = new() { Height = 60, Ramka = false, KolorTla = Styl.Tlo };
    /// <summary>Pasek z licznikiem i przyciskami stoi poza obszarem przewijania — rozwinieta instalacja go nie zaslania.</summary>
    readonly Panel _gora = new() { Dock = DockStyle.Top, Height = 64, BackColor = Styl.Tlo };
    readonly Label _licznik = E.Tekst("");
    readonly Przycisk _pokazSzukanie = new() { Text = "Szukaj instalacji…", Rodzaj = RodzajPrzycisku.Drugi, Width = 190 };
    readonly Przycisk _kontaSql = new() { Text = "Zarządzaj kontami MSSQL…", Width = 250 };

    readonly List<WidokInstalacji> _skonfigurowane = new();
    readonly List<WidokInstalacji> _wylaczone = new();
    readonly List<WidokInstalacji> _znalezione = new();
    bool _wczytano;
    bool _odswiezanie;

    public override string Tytul => "R2 Płatnik";
    public override string Podtytul => "Firmy, które connector synchronizuje, i instalacje R2Płatnika na tym serwerze";

    public EkranInstalacje(Kontekst k)
    {
        _k = k;
        Uklad.Pokaz(_kartaSzukania, false);
        string start = k.Plik?.Instalacje.FirstOrDefault()?.KatalogBin is string b
            ? Path.GetDirectoryName(Path.GetDirectoryName(b.TrimEnd('\\')))! : @"C:\Symfonia";
        _katalog = E.Pole(start);

        var n = E.Naglowek("Dodaj instalację R2Płatnika"); n.Location = new Point(24, 20);
        var o = E.Opis("Connector przeszuka wskazany katalog (szuka pliku BIN\\RPlatnik.ini) i niczego w instalacjach nie zmienia.\n"
                       + "Instalacje już synchronizowane pomija — są wyżej.");
        o.Location = new Point(25, 50);
        _katalog.Location = new Point(24, 96); _katalog.Width = 460;
        _wykryj.Location = new Point(500, 90);
        _wynikWykrycia.Location = new Point(25, 134);
        _wykryj.Click += async (_, _) => await SzukajAsync();
        _kartaSzukania.Controls.AddRange(new Control[] { n, o, _katalog, _wykryj, _wynikWykrycia });

        // Pasek nad lista: ile instalacji jest w konfiguracji i ile z nich nic nie synchronizuje.
        _pasekGorny.Width = Uklad.SzerokoscTresci;   // zanim ustawimy kotwice — inaczej przycisk ucieka poza karte
        _licznik.ForeColor = Styl.Szary;
        _licznik.AutoSize = false;
        _licznik.AutoEllipsis = true;   // przyciski maja pierwszenstwo; licznik skraca sie, nie wchodzi pod nie
        _licznik.Size = new Size(Uklad.SzerokoscTresci - _pokazSzukanie.Width - _kontaSql.Width - 30, 24);
        _licznik.TextAlign = ContentAlignment.MiddleLeft;
        _licznik.Location = new Point(2, 21);
        _kontaSql.Location = new Point(Uklad.SzerokoscTresci - _kontaSql.Width, 14);
        _pokazSzukanie.Location = new Point(_kontaSql.Left - _pokazSzukanie.Width - 10, 14);
        _kontaSql.Anchor = _pokazSzukanie.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _pokazSzukanie.Click += (_, _) =>
        {
            Uklad.Pokaz(_kartaSzukania, !Uklad.Widoczny(_kartaSzukania));
            // Napis mowi, co przycisk zrobi po nacisnieciu — inaczej przy otwartym panelu wyglada na martwy.
            _pokazSzukanie.Text = Uklad.Widoczny(_kartaSzukania) ? "Ukryj szukanie" : "Szukaj instalacji…";
            if (Uklad.Widoczny(_kartaSzukania)) _katalog.Focus();
        };
        // Konta SQL nalezą do serwera, nie do pojedynczej instalacji — stad przycisk w pasku, a nie w karcie.
        _kontaSql.Click += (_, _) =>
        {
            using var dialog = new DialogKontaSql(_k);
            dialog.ShowDialog(FindForm());
        };
        _pasekGorny.Controls.AddRange(new Control[] { _licznik, _pokazSzukanie, _kontaSql });
        _pasekGorny.Location = new Point(0, 0);
        _gora.Controls.Add(_pasekGorny);
        Controls.Add(_gora);   // po Tresc — doki licza sie od konca, wiec pasek bierze gore, a lista reszte
        Tresc.Resize += (_, _) => DopasujGore();
        DopasujGore();

        // Po zapisie sekcji instalacja moze wejsc do synchronizowanych — wtedy ekran przebudowujemy.
        k.Zapisano += () => { if (!_odswiezanie) BeginInvoke(new Action(async () => await WczytajSkonfigurowaneAsync())); };
    }

    public override async Task PokazAsync()
    {
        if (!_wczytano) await WczytajSkonfigurowaneAsync();
    }

    /// <summary>Przypiety pasek trzyma te sama szerokosc co karty pod nim — inaczej przyciski uciekaja na bok.</summary>
    void DopasujGore()
    {
        int w = Tresc.Width - Tresc.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6;
        _pasekGorny.Width = Math.Max(400, Math.Min(w, Uklad.SzerokoscTresci));
    }

    /// <summary>Do zrzutow dokumentacyjnych: rozwija pierwsza instalacje, zeby na obrazku byly sekcje, nie tylko naglowki.</summary>
    public void RozwinPierwszaDlaZrzutu()
    {
        if (_skonfigurowane.Concat(_wylaczone).Concat(_znalezione).FirstOrDefault() is { } pierwsza)
            Przelacz(pierwsza);
    }

    /// <summary>
    /// Do zrzutow dokumentacyjnych: karta instalacji z kilkoma firmami. Na tej maszynie kazda instalacja
    /// ma jedna firme, a sekcja firm wyglada inaczej, gdy jest ich kilka — stad wymyslona instalacja.
    /// </summary>
    public static Karta KartaKilkuFirmDlaZrzutu(Kontekst k)
    {
        var firmy = new[]
        {
            new WykrytaFirma(1, "ALFA", "1234567009", "r2p_platnik10_alfa_dane_1", true),
            new WykrytaFirma(2, "ALFA TRANSPORT", "5272713911", "r2p_platnik10_alfa_dane_2", true),
            new WykrytaFirma(3, "ALFA SERWIS", "5213002211", "r2p_platnik10_alfa_dane_3", false),
        };
        var w = new WykrytaInstalacja("NowyR2Platnik.10_alfa", @"C:\Symfonia\NowyR2Platnik.10_alfa\BIN\",
            @"SERWER-DEMO\OPTIMA", "r2p_platnik10_alfa", "r2p_platnik10_alfa_bin",
            "10.1.0.33", "10.1.0.33", "10.10.33", firmy, null);
        var wpis = new PlikKonfiguracji.InstalacjaWPliku
        {
            Nazwa = w.Nazwa, KatalogBin = w.KatalogBin, SerwerSql = w.SerwerSql, BazaBin = w.BazaBin,
            LoginSql = "wu_alfa", HasloSql = "", LoginR2 = "WNIOSKI_DEMO", HasloR2 = "",
            Firmy = new List<FirmaR2>
            {
                new() { FirmIde = 1, BazaDane = firmy[0].BazaDane, Aktywna = true },
                new() { FirmIde = 2, BazaDane = firmy[1].BazaDane, Aktywna = true },
            },
        };
        var widok = new WidokInstalacji(w, wpis, k) { Rozwinieta = true };
        return widok.Karta;
    }

    /// <summary>Sekcja gorna: instalacje z konfiguracji — niezaleznie od tego, czy i gdzie ktos szukal.</summary>
    async Task WczytajSkonfigurowaneAsync()
    {
        _odswiezanie = true;
        // Zapis sekcji przebudowuje liste — instalacja, nad ktora ktos pracuje, ma zostac rozwinieta.
        string? otwarta = _skonfigurowane.Concat(_wylaczone).Concat(_znalezione).FirstOrDefault(w => w.Rozwinieta)?.KatalogBin;
        int przewiniecie = -Tresc.AutoScrollPosition.Y;   // autozapis przebudowuje liste — widok ma zostac na miejscu
        try
        {
            _skonfigurowane.Clear();
            _wylaczone.Clear();
            _k.Wykryte.Clear();

            var wpisy = (_k.Plik?.Instalacje ?? new()).ToList();
            foreach (var wpis in wpisy)
            {
                var w = await WykrywanieInstalacji.WykryjAsync(wpis.KatalogBin);
                _k.Wykryte.Add(w);
                // Instalacja bez ani jednej zaznaczonej firmy niczego nie synchronizuje — osobna sekcja, zeby nie mylila.
                var widok = new WidokInstalacji(w, wpis, _k);
                (wpis.Firmy.Any(f => f.Aktywna) ? _skonfigurowane : _wylaczone).Add(widok);
            }

            int firmy = wpisy.Sum(i => i.Firmy.Count(f => f.Aktywna));
            _licznik.Text = wpisy.Count == 0
                ? "Nic nie jest jeszcze skonfigurowane — znajdź instalację i zaznacz firmę."
                : $"{wpisy.Count} {Odmiana(wpisy.Count, "instalacja", "instalacje", "instalacji")}   ·   "
                  + $"{_skonfigurowane.Count} {Odmiana(_skonfigurowane.Count, "synchronizowana", "synchronizowane", "synchronizowanych")}   ·   "
                  + $"{_wylaczone.Count} {Odmiana(_wylaczone.Count, "wyłączona", "wyłączone", "wyłączonych")}";

            // Znalezione, ktore w miedzyczasie weszly do konfiguracji, znikaja z dolnej sekcji.
            var sciezki = wpisy.Select(i => i.KatalogBin).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _znalezione.RemoveAll(z => sciezki.Contains(z.KatalogBin));
            foreach (var z in _znalezione) _k.Wykryte.Add(z.Wykryta);

            Przebuduj();
            if (otwarta is not null
                && _skonfigurowane.Concat(_wylaczone).Concat(_znalezione)
                    .FirstOrDefault(w => w.KatalogBin.Equals(otwarta, StringComparison.OrdinalIgnoreCase)) is { } znow)
                znow.Rozwinieta = true;
            if (przewiniecie > 0) Tresc.AutoScrollPosition = new Point(0, przewiniecie);
            _wczytano = true;
        }
        finally { _odswiezanie = false; }
    }

    /// <summary>Sekcja dolna: szukanie na zadanie. Instalacje juz synchronizowane pomijamy.</summary>
    async Task SzukajAsync()
    {
        _wykryj.Enabled = false;
        _wynikWykrycia.Text = "Szukam…";
        _znalezione.Clear();
        try
        {
            var skonfigurowane = (_k.Plik?.Instalacje ?? new()).Select(i => i.KatalogBin).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var katalogi = await Task.Run(() => WykrywanieInstalacji.ZnajdzKatalogiBin(_katalog.Text.Trim()));
            var nowe = katalogi.Where(kb => !skonfigurowane.Contains(kb)).ToList();

            foreach (var bin in nowe)
            {
                var w = await WykrywanieInstalacji.WykryjAsync(bin);
                _k.Wykryte.Add(w);
                _znalezione.Add(new WidokInstalacji(w, null, _k));
            }

            int juz = katalogi.Count - nowe.Count;
            _wynikWykrycia.Text = (nowe.Count, juz) switch
            {
                (0, 0) => "Nie znaleziono instalacji w tym katalogu",
                (0, var j) => $"Nie znaleziono nowych instalacji ({j} już synchronizowane, patrz wyżej)",
                (var x, 0) => $"Nowe instalacje: {x}",
                var (x, j) => $"Nowe instalacje: {x} · pominięte, bo już synchronizowane: {j}",
            };
            Przebuduj();
        }
        finally { _wykryj.Enabled = true; }
    }

    /// <summary>Uklada obie sekcje w kolejnosci: synchronizowane → szukanie → znalezione.</summary>
    /// <summary>
    /// Jedna lista: najpierw instalacje z konfiguracji (synchronizowane, potem wyłączone), na koncu znalezione.
    /// Kazda jest osobna karta z wlasna ramka, zwinieta do naglowka — rozwinieta jest tylko ta, nad ktora sie pracuje.
    /// </summary>
    void Przebuduj()
    {
        Tresc.SuspendLayout();
        Tresc.Controls.Clear();

        foreach (var w in _skonfigurowane.Concat(_wylaczone).Concat(_znalezione))
        {
            w.Przelaczono -= Przelacz;
            w.Przelaczono += Przelacz;
            Dodaj(w.Karta).Margin = new Padding(0, 0, 0, 12);
        }

        Dodaj(_kartaSzukania).Margin = new Padding(0, 10, 0, 16);

        Tresc.ResumeLayout();
        DopasujSzerokosc();
    }

    /// <summary>Rozwinieta jest jedna instalacja — reszta sie zwija, zeby ekran nie rosl bez konca.</summary>
    void Przelacz(WidokInstalacji ta)
    {
        bool rozwin = !ta.Rozwinieta;
        // Karta ma zostac tam, gdzie jest pod kursorem. Zwijanie instalacji nad nia zmienia jej polozenie
        // w liscie, wiec po przebudowie przesuwamy suwak o tyle samo — zamiast przewijac widok do karty.
        int przed = ta.Karta.Top;
        Tresc.SuspendLayout();
        foreach (var w in _skonfigurowane.Concat(_wylaczone).Concat(_znalezione)) w.Rozwinieta = w == ta && rozwin;
        Tresc.ResumeLayout();
        int po = ta.Karta.Top;
        if (po != przed)
            Tresc.AutoScrollPosition = new Point(-Tresc.AutoScrollPosition.X, -Tresc.AutoScrollPosition.Y + (po - przed));
    }

    static string Odmiana(int ile, string jeden, string kilka, string wiele)
    {
        if (ile == 1) return jeden;
        int dziesiatki = ile % 100, jednosci = ile % 10;
        return jednosci is >= 2 and <= 4 && dziesiatki is < 12 or > 14 ? kilka : wiele;
    }

    /// <summary>Ramka wokol listy firm z kreskami miedzy wierszami — zeby przy kilku firmach bylo widac, gdzie konczy sie jedna.</summary>
    sealed class RamkaListy : Panel
    {
        public List<int> Kreski { get; } = new();

        public RamkaListy()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Lista lezy na bialej plycie sekcji, wiec nie rysuje wlasnej ramki ani tla — tylko kreski
            // miedzy firmami. Trzecia ramka pod rzad (karta → plyta → lista) robila sie natretna.
            using var kreska = new Pen(Styl.Linia);
            foreach (int y in Kreski) e.Graphics.DrawLine(kreska, 0, y, Width, y);
            base.OnPaint(e);
        }
    }

    /// <summary>Karta jednej instalacji: wersje, firmy, konto R2 z testem logowania.</summary>
    sealed class WidokInstalacji
    {
        public Karta Karta { get; } = new();
        public WykrytaInstalacja Wykryta => _w;
        public string KatalogBin => _w.KatalogBin;
        readonly WykrytaInstalacja _w;
        PlikKonfiguracji.InstalacjaWPliku? _stara;
        /// <summary>
        /// Kazda sekcja zapisuje sie osobno — zmiana hasla SQL nie zapala przycisku przy koncie R2 i odwrotnie.
        /// Firmy zapisuja sie same (bez przycisku), stan instalacji nieodczytanej ma wlasna stopke.
        /// </summary>
        PasekZapisu? _pasekFirmy, _pasekSql, _pasekR2, _pasekStan;
        readonly Kontekst _k;
        readonly List<(CheckBox Pole, WykrytaFirma Firma)> _firmy = new();
        readonly TextBox _login, _haslo, _dll;
        readonly Przycisk _testuj = new() { Text = "Testuj logowanie", Rodzaj = RodzajPrzycisku.Drugi, Width = 170 };
        int? _uzytkownik;
        /// <summary>Instalacja z konfiguracji, ktorej nie da sie teraz odczytac — zapis zostawia ja bez zmian, chyba ze _usun.</summary>
        bool _zachowaj;
        readonly CheckBox _usun = new() { Text = "Usuń tę instalację z konfiguracji (connector przestanie ją synchronizować)", AutoSize = true, BackColor = Color.White, ForeColor = Styl.Koral };
        readonly TextBox _sqlSerwer, _sqlLogin, _sqlHaslo;
        readonly Przycisk _sqlTestuj = new() { Text = "Testuj SQL", Rodzaj = RodzajPrzycisku.Drugi, Width = 130 };

        /// <summary>Bloki i stopka — chowane, gdy instalacja jest zwinieta.</summary>
        readonly List<Control> _tresc = new();
        /// <summary>Rozwijanie karty — przycisk w ramce przy prawej krawedzi, zeby bylo go widac na pierwszy rzut oka.</summary>
        readonly Przycisk _rozwin = new()
        {
            Rodzaj = RodzajPrzycisku.Drugi, Text = "▾", Width = 44, Height = 34,
            Font = Styl.Czcionka(12f, FontStyle.Bold), Padding = new Padding(0),
        };
        /// <summary>Wlacznik calej instalacji — widoczny takze przy zwinietej karcie; dziala jak zaznaczenie wszystkich firm.</summary>
        readonly CheckBox _wlaczona = new() { Text = "Aktywne", AutoSize = true, BackColor = Color.Transparent, ThreeState = true };
        readonly Label _licznikFirm = E.Opis("");
        readonly Przycisk _zaznaczWszystkie = new() { Rodzaj = RodzajPrzycisku.Tekstowy, Text = "Zaznacz wszystkie", Width = 170, Height = 20 };
        /// <summary>Zmiana idzie z wlacznika albo z pojedynczej firmy — blokada, zeby jedna nie wywolala drugiej i dwoch zapisow.</summary>
        bool _wTrakcie;
        bool _rozwinieta;

        /// <summary>Klikniecie naglowka — ekran zwija pozostale instalacje, zeby otwarta byla jedna.</summary>
        public event Action<WidokInstalacji>? Przelaczono;

        /// <summary>Zakres zapisu: do pliku idzie tylko to, co nalezy do tej sekcji — reszta zostaje, jak byla.</summary>
        enum Sekcja { Wszystko, Firmy, Sql, R2 }

        public bool Rozwinieta
        {
            get => _rozwinieta;
            set
            {
                if (_rozwinieta == value) return;
                _rozwinieta = value;
                foreach (var c in _tresc) Uklad.Pokaz(c, value);
                Wyglad();
                Karta.Przelicz();
            }
        }

        void Przelacz() => Przelaczono?.Invoke(this);

        /// <summary>Rozwinieta instalacja ma mietowy naglowek i zielona obwodke — widac, nad czym sie pracuje.</summary>
        /// <summary>Wsuniecie plyt sekcji w karte — i zarazem odstep miedzy nimi.</summary>
        const int Wciecie = 12;

        void Wyglad()
        {
            _rozwin.Text = _rozwinieta ? "▴" : "▾";
            // Rozwinieta instalacja jest cala mietowa — widac, nad ktora sie pracuje, takze po przewinieciu.
            Karta.KolorTla = _rozwinieta ? Styl.Mieta : Color.White;
            Karta.KolorNaglowka = null;   // naglowek nie potrzebuje wlasnego paska, skoro cala karta jest mietowa
            Karta.KolorRamki = _rozwinieta ? ColorTranslator.FromHtml("#BFD7C7") : Styl.Obrys;
            // Przycisk sam maluje swoje tlo — musi wiedziec, co jest pod nim.
            _rozwin.Podklad = Karta.KolorTla;
            _rozwin.Invalidate();
            Karta.Invalidate();
        }

        public WidokInstalacji(WykrytaInstalacja w, PlikKonfiguracji.InstalacjaWPliku? stara, Kontekst k)
        {
            _w = w; _stara = stara; _k = k;
            _uzytkownik = stara?.UzytkownikR2Id;
            int szer = Uklad.SzerokoscTresci;
            int szerSekcji = szer - 2 * Wciecie;   // plyty sekcji sa wsuniete w karte z obu stron
            Karta.Width = szer;

            // ---- naglowek karty w trzech rzedach: wlacznik + nazwa + strzalka, pod nimi sciezka, na dole znaczniki.
            // Nic sie nie przepycha o te sama linie, wiec nazwa nie musi byc ucinana.
            // Caly naglowek jest klikalny — rozwija i zwija instalacje.

            // Rzad 1: wlacznik calej instalacji (widoczny takze przy zwinietej karcie) i nazwa.
            _wlaczona.Font = Styl.Czcionka(Styl.Tresc);
            _wlaczona.ForeColor = Styl.Tekst;
            // Taki sam stan jak znaczniki firm nizej: nowa instalacja ma firmy zaznaczone, z konfiguracji — te aktywne.
            _wlaczona.Checked = w.Firmy.Any(f => f.BazaIstnieje
                                                 && (stara is null || stara.Firmy.Any(a => a.FirmIde == f.FirmIde && a.Aktywna)));
            // Instalacji, ktorej nie da sie odczytac (brak firm, a jest w konfiguracji), wlacznik nie dotyczy.
            _wlaczona.Enabled = w.Firmy.Any(f => f.BazaIstnieje);
            _wlaczona.Location = new Point(Uklad.Margines, 22);
            int lewa = Uklad.Margines + _wlaczona.PreferredSize.Width + 20;

            // Strzalka po prawej — tam, gdzie oko szuka rozwijania listy; caly naglowek i tak reaguje na klikniecie.
            _rozwin.Location = new Point(szer - Uklad.Margines - _rozwin.Width, 18);
            _rozwin.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var nazwa = E.Tekst(w.Nazwa, true);
            nazwa.Font = Styl.Naglowek(Styl.NaglowekKarty);
            nazwa.ForeColor = Styl.Granat;
            nazwa.AutoSize = false;
            nazwa.AutoEllipsis = true;   // bardzo dluga nazwa konczy sie wielokropkiem, nie wchodzi pod strzalke
            nazwa.Size = new Size(Math.Max(140, szer - lewa - _rozwin.Width - Uklad.Margines - 16), 26);
            nazwa.Location = new Point(lewa, 19);
            nazwa.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // Rzad 2: skad ta instalacja jest — katalog, serwer, bazy, ile firm.
            // Krotkie separatory i bez slowa „bazy" — cala linia ma sie zmiescic bez wielokropka.
            string firmy = w.Firmy.Count > 0
                ? $"  ·  {w.Firmy.Count} {Odmiana(w.Firmy.Count, "firma", "firmy", "firm")}"
                : "";
            var meta = E.Opis((w.PrefiksBazy == "" && stara is not null
                ? $"{w.KatalogBin}  ·  {stara.SerwerSql}  ·  {stara.BazaBin} (z konfiguracji)"
                : $"{w.KatalogBin}  ·  {w.SerwerSql}  ·  {w.PrefiksBazy.ToLowerInvariant()}_*") + firmy);
            meta.AutoSize = false;
            meta.AutoEllipsis = true;    // jedna linia o stalej wysokosci — naglowki wszystkich kart sa rowne
            // Sciezka i znaczniki wyrownane do nazwy — znacznik „Aktywne" ma wlasna kolumne po lewej.
            meta.Size = new Size(szer - lewa - Uklad.Margines - _rozwin.Width - 8, 20);
            meta.Location = new Point(lewa, 52);
            meta.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Karta.Controls.AddRange(new Control[] { _rozwin, nazwa, meta });

            // Rzad 3: znaczniki wersji — od lewej, pod sciezka, zamiast cisnac sie w linii nazwy.
            var znaczniki = Znaczniki().ToList();
            int x = lewa;
            foreach (var z in znaczniki)
            {
                z.Location = new Point(x, 78);
                Karta.Controls.Add(z);
                x += z.Width + 6;
            }

            int wysokoscNaglowka = znaczniki.Count > 0 ? 78 + znaczniki.Max(z => z.Height) + 14 : 86;
            Karta.WysokoscNaglowka = wysokoscNaglowka;
            Karta.PoczatekTresci = wysokoscNaglowka;
            foreach (Control c in Karta.Controls) c.Click += (_, _) => Przelacz();
            Karta.Click += (_, _) => Przelacz();
            // Wlacznik dokladamy po tej petli — klikniecie go przestawia synchronizacje, a nie zwija karte.
            Karta.Controls.Add(_wlaczona);

            // Rozwinieta karta jest cala mietowa, a sekcje leza na niej jako biale plyty — to one, a nie
            // kreski czy paski, wyznaczaja granice. Stad bloki budujemy na wezszej mierze i wsuwamy do srodka.
            Karta.OdstepCzesci = Wciecie;

            int y = wysokoscNaglowka + Wciecie;

            void DodajBlok(Blok b)
            {
                b.Plyta = true;
                b.Location = new Point(Wciecie, y);
                Uklad.Pokaz(b, false);      // karta startuje zwinieta; Rozwinieta pokazuje tresc
                Karta.Controls.Add(b);
                _tresc.Add(b);
                y += b.Height + Wciecie;
            }

            // Zapis sekcji przyciskiem w jej wlasnym rzedzie, zaraz za „Testuj…”. Pasek zostaje po to,
            // zeby bylo gdzie napisac, co blokuje zapis albo co sie zapisalo.
            PasekZapisu WRzedzie(string tekst, Sekcja sekcja, Blok blok, bool autozapis = false)
            {
                var pasek = new PasekZapisu(k, tekst, bledy => Przygotuj(sekcja, bledy), autozapis, wRzedzie: true)
                {
                    Width = szerSekcji - 2 * Uklad.Margines,
                };
                blok.Wiersz(pasek, Uklad.Mala);
                return pasek;
            }

            // Stopka tylko dla jednej sekcji: sledzi wylacznie jej pola i zapisuje wylacznie jej czesc wpisu.
            PasekZapisu DodajPasek(string tekst, Sekcja sekcja, Blok blok, bool autozapis = false)
            {
                var pasek = new PasekZapisu(k, tekst, bledy => Przygotuj(sekcja, bledy), autozapis)
                {
                    Location = new Point(0, y), Width = szer,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };
                Uklad.Pokaz(pasek, false);
                Karta.Controls.Add(pasek);
                _tresc.Add(pasek);
                y += pasek.Height;
                pasek.SledzZmiany(blok);
                return pasek;
            }

            // ---- stan instalacji: blad odczytu, konflikt baz, instalacja nie do odczytania
            if (w.Blad is not null || (stara is not null && Dubel(stara) is not null) || (w.Firmy.Count == 0 && stara is not null))
            {
                var stan = new Blok(null, null, szerSekcji);
                if (w.Blad is not null)
                    stan.Wiersz(new Znacznik { Text = w.Blad.Length > 110 ? w.Blad[..110] + "…" : w.Blad, Poziom = Poziom.Blad }, Uklad.Mala);
                if (stara is not null && Dubel(stara) is string dubelTeraz)
                    stan.Wiersz(new Znacznik { Text = dubelTeraz, Poziom = Poziom.Blad }, Uklad.Mala);
                if (w.Firmy.Count == 0 && stara is not null)
                {
                    _zachowaj = true;
                    stan.Wiersz(E.Opis($"Dotychczasowa konfiguracja zostaje bez zmian (firmy: {string.Join(", ", stara.Firmy.Select(f => f.BazaDane))}). "
                                       + "Connector próbuje ją synchronizować — sprawdź Dziennik."));
                    stan.Wiersz(_usun);
                }
                var bstan = stan.Domknij();
                DodajBlok(bstan);
                // Usuniecie z konfiguracji to nie przestawienie znacznika — zostaje przy nim przycisk zapisu.
                if (_zachowaj) _pasekStan = DodajPasek("Zapisz tę instalację", Sekcja.Wszystko, bstan);
            }

            // ---- firmy
            if (!_zachowaj)
            {
                // Firmy sa lista w ramce: kazda ma wlasny znacznik, nazwe, baze pod spodem i NIP po prawej.
                // Jedna regula na kazda liczbe firm — „Aktywne” w naglowku przestawia je wszystkie naraz.
                // Licznik i skrot stoja w linii tytulu sekcji — tytul rysuje Blok, reszte dokladamy tutaj.
                var bf = new Blok("Firmy do synchronizacji", null, szerSekcji);
                var tytulFirm = bf.Tytul!;
                _licznikFirm.Location = new Point(tytulFirm.Left + tytulFirm.PreferredWidth + 12, tytulFirm.Top + 2);
                _zaznaczWszystkie.Location = new Point(szerSekcji - Uklad.Margines - _zaznaczWszystkie.Width, tytulFirm.Top - 2);
                _zaznaczWszystkie.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                bf.Controls.AddRange(new Control[] { _licznikFirm, _zaznaczWszystkie });
                bf.Wiersz(E.Opis("Zaznaczone firmy trafiają do wezurlop. Zaznaczenie zapisuje się od razu, bez przycisku."), Uklad.Mala);

                var lista = new RamkaListy
                {
                    Width = szerSekcji - 2 * Uklad.Margines,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };
                int yf = 0;
                foreach (var f in w.Firmy)
                {
                    bool bylaAktywna = stara?.Firmy.Any(a => a.FirmIde == f.FirmIde && a.Aktywna) ?? false;
                    var cb = new CheckBox
                    {
                        Checked = f.BazaIstnieje && (stara is null || bylaAktywna),
                        Enabled = f.BazaIstnieje,
                        AutoSize = false, Size = new Size(18, 18), BackColor = Color.Transparent,
                    };
                    _firmy.Add((cb, f));

                    var wiersz = new Panel
                    {
                        BackColor = Color.Transparent, Width = lista.Width, Height = 50, Location = new Point(0, yf),
                        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    };
                    cb.Location = new Point(14, 16);
                    var nazwaFirmy = E.Tekst(f.Nazwa, true);
                    nazwaFirmy.ForeColor = f.BazaIstnieje ? Styl.Granat : Styl.Szary;
                    nazwaFirmy.Location = new Point(40, 9);
                    var pod = E.Opis(f.BazaIstnieje ? $"baza {f.BazaDane}" : $"brak bazy {f.BazaDane}");
                    pod.Location = new Point(41, 28);
                    wiersz.Controls.AddRange(new Control[] { cb, nazwaFirmy, pod });

                    if (f.BazaIstnieje)
                    {
                        var nip = E.Opis("NIP " + EkranStatus.FormatNip(f.Nip));
                        nip.AutoSize = false;
                        nip.Size = new Size(160, 20);
                        nip.TextAlign = ContentAlignment.MiddleRight;
                        nip.Location = new Point(wiersz.Width - 160 - 14, 14);
                        nip.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                        wiersz.Controls.Add(nip);
                    }
                    else
                    {
                        // Brak bazy to nie szczegol — firmy nie da sie synchronizowac, wiec mowi o tym znacznik.
                        var ostrzezenie = new Znacznik { Text = "nie da się synchronizować", Poziom = Poziom.Blad };
                        ostrzezenie.Location = new Point(wiersz.Width - ostrzezenie.Width - 14, 13);
                        ostrzezenie.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                        wiersz.Controls.Add(ostrzezenie);
                    }

                    lista.Controls.Add(wiersz);
                    yf += wiersz.Height;
                    lista.Kreski.Add(yf);
                }
                lista.Kreski.RemoveAt(lista.Kreski.Count - 1);   // pod ostatnim wierszem jest juz ramka
                lista.Height = yf;
                // Wiekszy odstep po liscie: inaczej jej dolna ramka laduje tuz przy pasku granicznym sekcji.
                bf.Wiersz(lista, Uklad.Ogromna);
                // Autozapis: znacznik aktywna/nieaktywna zapisuje sie sam, zostaje tylko miejsce na komunikat.
                _pasekFirmy = WRzedzie("Zapisz firmy", Sekcja.Firmy, bf, autozapis: true);
                var bfirmy = bf.Domknij();
                DodajBlok(bfirmy);
                _pasekFirmy.SledzZmiany(bfirmy);
            }

            // ---- polaczenie z baza
            _sqlSerwer = E.Pole(stara?.SerwerSql ?? w.SerwerSql);
            _sqlLogin = E.Pole(stara?.LoginSql ?? KontoOdczytuSql.LoginDla(w.BazaBin));
            _sqlHaslo = E.Pole(stara?.HasloSql is string hs ? SafeJawny(hs) : "", true);

            if (!_zachowaj)
            {
                // Oba rzedy przyciskow maja te same szerokosci: „Testuj…” rowne sobie i „Zapisz…” rowne sobie,
                // wiec sekcje czytaja sie jak jedna kolumna, a nie jak dwa osobne uklady.
                _pasekSql = new PasekZapisu(k, "Zapisz połączenie z bazą", bledy => Przygotuj(Sekcja.Sql, bledy), wRzedzie: true)
                {
                    Width = szerSekcji - 2 * Uklad.Margines,
                };
                _pasekR2 = new PasekZapisu(k, w.WersjaBiblioteki is null ? "Zapisz konto i bibliotekę" : "Zapisz konto R2Płatnika",
                                           bledy => Przygotuj(Sekcja.R2, bledy), wRzedzie: true)
                {
                    Width = szerSekcji - 2 * Uklad.Margines,
                };
                _sqlTestuj.Width = _testuj.Width =
                    Math.Max(_sqlTestuj.GetPreferredSize(Size.Empty).Width, _testuj.GetPreferredSize(Size.Empty).Width);
                _pasekSql.Przycisk.Width = _pasekR2.Przycisk.Width =
                    Math.Max(_pasekSql.Przycisk.Width, _pasekR2.Przycisk.Width);

                // Tylko login SQL: polaczenia nawiazuje usluga, a ta pracuje jako SYSTEM — logowanie
                // zintegrowane daloby wiec konto systemowe, ktore do baz R2 dostepu nie ma (22.24 ustalen).
                var bs = new Blok("Połączenie z bazą programu kadrowego",
                                  "Tylko odczyt. Usługa pracuje jako konto systemowe, dlatego potrzebuje własnego konta SQL — "
                                  + "założysz je przyciskiem „Zarządzaj kontami MSSQL…” nad listą.", szerSekcji);
                bs.PolaRowno(new[] { "Serwer SQL", "Login", "Hasło" }, _sqlSerwer, _sqlLogin, _sqlHaslo);
                bs.Przyciski(_sqlTestuj, _pasekSql.Przycisk);
                bs.Wiersz(_pasekSql, Uklad.Mala);
                var bsql = bs.Domknij();
                DodajBlok(bsql);
                _pasekSql.SledzZmiany(bsql);
            }

            // ---- konto w programie kadrowym (razem z biblioteka API — jedno i drugie dotyczy zapisu do R2)
            _login = E.Pole(stara?.LoginR2 ?? "");
            _haslo = E.Pole(stara is null ? "" : SafeJawny(stara.HasloR2), true);
            _dll = E.Pole(stara?.KatalogDll ?? "");
            if (!_zachowaj)
            {
                bool zBiblioteka = w.WersjaBiblioteki is null;
                var bk = new Blok("Konto connectora w R2Płatniku",
                                  "Konto dedykowane — nikt nie może się na nim logować w programie, bo R2 blokuje podwójne logowanie.", szerSekcji);
                bk.PolaRowno(new[] { "Login", "Hasło" }, _login, _haslo);
                bk.Przyciski(_testuj, _pasekR2!.Przycisk);
                bk.Wiersz(_pasekR2, Uklad.Mala);
                var bkonto = bk.Domknij();
                DodajBlok(bkonto);
                _pasekR2.SledzZmiany(bkonto);

                // Biblioteka API, gdy brak w BIN — ta sama sekcja zapisu, bo bez niej konto i tak nic nie zapisze.
                if (zBiblioteka)
                {
                    var bb = new Blok("Biblioteka API programu kadrowego",
                                      "Ta instalacja nie ma biblioteki API. Wskaż katalog z R2PApi10.dll (docelowo: magazyn bibliotek connectora).", szerSekcji);
                    bb.PolaRowno(new[] { "Katalog z biblioteką" }, _dll);
                    var bbiblioteka = bb.Domknij();
                    DodajBlok(bbiblioteka);
                    _pasekR2.SledzZmiany(bbiblioteka);
                }
            }

            Karta.Height = wysokoscNaglowka;
            Wyglad();

            // ---- zdarzenia
            _sqlTestuj.Click += (_, _) => TestujSql();

            _testuj.Click += (_, _) => Testuj();

            // Zaznaczenie firmy zapisuje sie od razu — przycisku przy tej sekcji nie ma.
            foreach (var (pole, _) in _firmy)
                pole.CheckedChanged += (_, _) =>
                {
                    if (_wTrakcie) return;
                    Bez(OdswiezFirmy);
                    Autozapis();
                };

            // Wlacznik w naglowku przestawia wszystkie firmy naraz — jeden zapis, nie jeden na firme.
            _wlaczona.Click += (_, _) =>
            {
                // ThreeState daje klikaniem takze stan posredni — nam ma sluzyc tylko do pokazania „czesc firm”.
                bool wszystkie = _firmy.Where(f => f.Pole.Enabled).All(f => f.Pole.Checked);
                Bez(() =>
                {
                    foreach (var (pole, _) in _firmy) if (pole.Enabled) pole.Checked = !wszystkie;
                    OdswiezFirmy();
                });
                Autozapis();
            };
            _zaznaczWszystkie.Click += (_, _) =>
            {
                bool wszystkie = _firmy.Where(f => f.Pole.Enabled).All(f => f.Pole.Checked);
                Bez(() =>
                {
                    foreach (var (pole, _) in _firmy) if (pole.Enabled) pole.Checked = !wszystkie;
                    OdswiezFirmy();
                });
                Autozapis();
            };
            Bez(OdswiezFirmy);
        }

        /// <summary>Licznik, napis na skrocie i stan wlacznika — wszystko wynika z tego, co zaznaczone.</summary>
        void OdswiezFirmy()
        {
            int zdatne = _firmy.Count(f => f.Pole.Enabled);
            int wlaczone = _firmy.Count(f => f.Pole.Checked);
            _licznikFirm.Text = _firmy.Count == 0 ? "" : $"{wlaczone} z {_firmy.Count}";
            bool wszystkie = zdatne > 0 && _firmy.Where(f => f.Pole.Enabled).All(f => f.Pole.Checked);
            _zaznaczWszystkie.Text = wszystkie ? "Odznacz wszystkie" : "Zaznacz wszystkie";
            Uklad.Pokaz(_zaznaczWszystkie, zdatne > 1);   // przy jednej firmie skrot nie ma czego skracac
            _wlaczona.CheckState = wlaczone == 0 ? CheckState.Unchecked
                                 : wszystkie ? CheckState.Checked
                                 : CheckState.Indeterminate;
        }

        /// <summary>Zmiana, ktora nie ma wywolac drugiego zapisu ani odbic sie z powrotem na wlaczniku.</summary>
        void Bez(Action co)
        {
            _wTrakcie = true;
            try { co(); } finally { _wTrakcie = false; }
        }

        void Autozapis()
        {
            _pasekFirmy?.Zmieniono();
            _pasekFirmy?.ZapiszTeraz();
        }

        /// <summary>Aktywna firma, ktorej baze synchronizuje juz inna instalacja z konfiguracji (ta sama firma dwa razy).</summary>
        string? Dubel(PlikKonfiguracji.InstalacjaWPliku ta)
        {
            foreach (var inna in _k.Plik?.Instalacje ?? new())
            {
                if (inna.KatalogBin.Equals(_w.KatalogBin, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var f in ta.Firmy.Where(f => f.Aktywna))
                    if (inna.SerwerSql.Equals(ta.SerwerSql, StringComparison.OrdinalIgnoreCase)
                        && inna.Firmy.Any(x => x.Aktywna && x.BazaDane.Equals(f.BazaDane, StringComparison.OrdinalIgnoreCase)))
                        return $"Baza {f.BazaDane} jest już synchronizowana przez instalację {inna.Nazwa} — odznacz firmę tutaj albo tam";
            }
            return null;
        }

        /// <summary>
        /// Zapis jednej sekcji tej instalacji: podmiana (albo dodanie / usuniecie) jej wpisu w pliku,
        /// ale poza wskazana sekcja wpis zostaje taki, jaki jest w konfiguracji — rowniez wtedy,
        /// gdy ktos ma w formularzu niedokonczone zmiany w drugiej sekcji.
        /// </summary>
        ZmianaSekcji? Przygotuj(Sekcja sekcja, List<string> bledy)
        {
            var nowa = Zbierz(bledy);
            if (bledy.Count > 0) return null;
            if (nowa is not null && _stara is not null && sekcja != Sekcja.Wszystko) nowa = TylkoSekcja(_stara, nowa, sekcja);
            if (nowa is null && _stara is null)
            {
                bledy.Add(sekcja == Sekcja.Firmy ? "Zaznacz firmę do synchronizacji"
                                                 : "Najpierw zaznacz firmę do synchronizacji");
                return null;
            }
            if (nowa is not null && Dubel(nowa) is string dubel) { bledy.Add(dubel); return null; }
            // Stanu „w konfiguracji" nie ruszamy tutaj — sekcja bywa sprawdzana przy kazdej zmianie pola,
            // a po udanym zapisie ekran i tak wczytuje sie od nowa (Kontekst.Zapisano).
            return ZmianaSekcji.DlaInstalacji(_w.KatalogBin, nowa);
        }

        /// <summary>Wpis z konfiguracji z naniesionymi polami jednej sekcji formularza.</summary>
        static PlikKonfiguracji.InstalacjaWPliku TylkoSekcja(
            PlikKonfiguracji.InstalacjaWPliku stara, PlikKonfiguracji.InstalacjaWPliku zFormularza, Sekcja sekcja)
        {
            var wynik = new PlikKonfiguracji.InstalacjaWPliku
            {
                // Nazwa, katalog, baza wspolna i wersja ida z wykrycia — to nie sa pola formularza.
                Nazwa = zFormularza.Nazwa,
                KatalogBin = zFormularza.KatalogBin,
                BazaBin = zFormularza.BazaBin,
                WersjaR2 = zFormularza.WersjaR2,
                KatalogDll = stara.KatalogDll,
                SerwerSql = stara.SerwerSql,
                LoginSql = stara.LoginSql,
                HasloSql = stara.HasloSql,
                LoginR2 = stara.LoginR2,
                HasloR2 = stara.HasloR2,
                UzytkownikR2Id = stara.UzytkownikR2Id,
                Firmy = stara.Firmy,
            };
            switch (sekcja)
            {
                case Sekcja.Firmy:
                    wynik.Firmy = zFormularza.Firmy;
                    break;
                case Sekcja.Sql:
                    wynik.SerwerSql = zFormularza.SerwerSql;
                    wynik.LoginSql = zFormularza.LoginSql;
                    wynik.HasloSql = zFormularza.HasloSql;
                    break;
                case Sekcja.R2:
                    wynik.LoginR2 = zFormularza.LoginR2;
                    wynik.HasloR2 = zFormularza.HasloR2;
                    wynik.UzytkownikR2Id = zFormularza.UzytkownikR2Id;
                    wynik.KatalogDll = zFormularza.KatalogDll;
                    break;
            }
            return wynik;
        }

        IEnumerable<Znacznik> Znaczniki()
        {
            // „Synchronizowana / wyłączona" mowi teraz wlacznik w naglowku — znacznik zostaje tylko tam,
            // gdzie wlacznik nie wystarcza, i oddaje miejsce nazwie instalacji.
            if (_stara is not null && _w.Firmy.Count == 0)
                yield return new Znacznik { Text = "w konfiguracji — nie można odczytać", Poziom = Poziom.Blad };
            else if (_stara is null)
                yield return new Znacznik { Text = "nowa — jeszcze nie zapisana", Poziom = Poziom.Info };
            if (_w.WersjaProgramu is string p) yield return new Znacznik { Text = "program " + p, Poziom = Poziom.Info };
            if (_w.WersjaBazy is string b) yield return new Znacznik { Text = "baza " + b, Poziom = Poziom.Info };
            yield return _w.WersjaBiblioteki is string d
                ? new Znacznik { Text = "biblioteka API " + d, Poziom = Poziom.Ok }
                : new Znacznik { Text = "brak biblioteki API", Poziom = Poziom.Uwaga };
        }

        static string SafeJawny(string s) { try { return PlikKonfiguracji.Jawny(s); } catch { return ""; } }

        /// <summary>Logowanie do R2Platnika osobnym procesem — moze trwac kilkanascie sekund, wiec okno z kolowrotkiem.</summary>
        void Testuj()
        {
            var firma = _firmy.FirstOrDefault(f => f.Pole.Checked).Firma ?? _w.Firmy.FirstOrDefault();
            if (firma is null || _login.Text.Trim() == "")
            {
                DialogPytania.Informacja(Karta.FindForm()!, "Test logowania",
                    "Żeby sprawdzić konto, podaj login connectora i zaznacz firmę, w której ma się zalogować.");
                return;
            }
            DialogPracy.Wykonaj(Karta.FindForm(), "Test logowania do R2Płatnika",
                $"{_w.Nazwa} · firma {firma.Nazwa} · konto {_login.Text.Trim()}",
                "Loguję do R2Płatnika…", async () =>
                {
                    var w = await new UruchamiaczWorkera(_k.SciezkaWorkera, TimeSpan.FromSeconds(60)).WykonajAsync(new ZadanieWorkera
                    {
                        Operacja = Operacje.Test, KatalogBin = _w.KatalogBin,
                        KatalogDll = string.IsNullOrWhiteSpace(_dll.Text) ? null : _dll.Text.Trim(),
                        Login = _login.Text.Trim(), Haslo = _haslo.Text, FirmIde = firma.FirmIde,
                    }, CancellationToken.None);
                    if (w.Status != Statusy.Zapisano)
                        return new DialogPracy.Wynik(Poziom.Blad, w.Komunikat ?? w.Kod ?? "Logowanie się nie udało");

                    // Numer konta R2 zapisujemy po cichu — jest czescia tej sekcji, ale nikomu nic nie mowi.
                    _uzytkownik = w.UzytkownikId;
                    _pasekR2?.Zmieniono();
                    return new DialogPracy.Wynik(Poziom.Ok, "Powodzenie");
                });
        }

        /// <summary>Odczyt z bazy tym kontem SQL, ktore jest w polach — wynik w tym samym oknie co test logowania.</summary>
        void TestujSql()
        {
            var firma = _firmy.FirstOrDefault(f => f.Pole.Checked).Firma ?? _w.Firmy.FirstOrDefault();
            if (firma is null)
            {
                DialogPytania.Informacja(Karta.FindForm()!, "Test połączenia z bazą",
                    "Ta instalacja nie ma firmy, na której dałoby się sprawdzić odczyt.");
                return;
            }
            DialogPracy.Wykonaj(Karta.FindForm(), "Test połączenia z bazą",
                $"{_sqlSerwer.Text.Trim()} · baza {firma.BazaDane} · login {_sqlLogin.Text.Trim()}",
                "Czytam bazę…", async () =>
                {
                    // Sprawdzamy tylko, czy to konto czyta z tej bazy — ile kogo jest, pokazuje Podgląd wysyłki.
                    await new WuConnector.Core.Odczyt.CzytnikSql(new WuConnector.Core.Odczyt.ZrodloFirmy(
                        _sqlSerwer.Text.Trim(), _w.BazaBin, firma.BazaDane, firma.FirmIde,
                        _sqlLogin.Text.Trim(), _sqlHaslo.Text))
                        .CzytajAsync(DateTime.Today.Year - 1, DateTime.Today.Year + 1);
                    return new DialogPracy.Wynik(Poziom.Ok, "Powodzenie");
                });
        }

        public PlikKonfiguracji.InstalacjaWPliku? Zbierz(List<string> bledy)
        {
            if (_zachowaj) return _usun.Checked ? null : _stara;
            var firmy = _firmy.Where(f => f.Pole.Checked || (_stara?.Firmy.Any(x => x.FirmIde == f.Firma.FirmIde) ?? false)).ToList();
            if (firmy.Count == 0) return null;   // nowa instalacja bez zaznaczonych firm — nie trafia do konfiguracji
            // Brak loginu czy hasla nie blokuje zapisu — usluga pomija niekompletna instalacje (PlikKonfiguracji.Braki),
            // a okno pokazuje, czego brakuje.

            // Haslo bez zmian — zostaje dotychczasowy zaszyfrowany zapis; zmienione — szyfrujemy DPAPI.
            string haslo = _haslo.Text == "" ? ""
                : _stara is not null && SafeJawny(_stara.HasloR2) == _haslo.Text ? _stara.HasloR2 : PlikKonfiguracji.Zaszyfruj(_haslo.Text);
            return new PlikKonfiguracji.InstalacjaWPliku
            {
                Nazwa = _w.Nazwa,
                KatalogBin = _w.KatalogBin,
                KatalogDll = string.IsNullOrWhiteSpace(_dll.Text) ? null : _dll.Text.Trim(),
                SerwerSql = _sqlSerwer.Text.Trim(),
                BazaBin = _w.BazaBin,
                LoginSql = _sqlLogin.Text.Trim(),
                HasloSql = _sqlHaslo.Text == "" ? ""
                         : _stara?.HasloSql is string hs && SafeJawny(hs) == _sqlHaslo.Text ? hs
                         : PlikKonfiguracji.Zaszyfruj(_sqlHaslo.Text),
                LoginR2 = _login.Text.Trim(),
                HasloR2 = haslo,
                UzytkownikR2Id = _uzytkownik,
                WersjaR2 = _w.WersjaBazy,
                // Odznaczona firma z konfiguracji zostaje jako wylaczona — widoczna, z haslami, do ponownego wlaczenia.
                Firmy = firmy.Select(f => new FirmaR2 { FirmIde = f.Firma.FirmIde, BazaDane = f.Firma.BazaDane, Aktywna = f.Pole.Checked }).ToList(),
            };
        }
    }
}


