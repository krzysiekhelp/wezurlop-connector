namespace WuConnector.Okno;

/// <summary>Ekran w obszarze tresci. PokazAsync wolane przy kazdym wejsciu na ekran.</summary>
public abstract class Ekran : UserControl
{
    /// <summary>
    /// Tag kontrolki, ktora nie zmienia konfiguracji, tylko sposob jej pokazania (np. „pokaż" przy kluczu).
    /// Pasek zapisu jej nie sledzi — inaczej samo odsloniecie klucza wygladaloby na niezapisana zmiane.
    /// </summary>
    public const string BezZapisu = "podglad";

    public abstract string Tytul { get; }
    public abstract string Podtytul { get; }
    public virtual Task PokazAsync() => Task.CompletedTask;

    protected readonly FlowLayoutPanel Tresc;
    /// <summary>Karty ekranu — do zrzutow kart, ktore nie mieszcza sie w oknie.</summary>
    public IEnumerable<Control> Karty => Tresc.Controls.Cast<Control>();
    public bool Przewijany => Tresc.DisplayRectangle.Height > Tresc.ClientSize.Height;

    protected Ekran()
    {
        BackColor = Styl.Tlo;
        Dock = DockStyle.Fill;
        Tresc = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = Styl.Tlo, Padding = new Padding(0, 0, 12, 24),
        };
        Controls.Add(Tresc);
        Tresc.Resize += (_, _) => DopasujSzerokosc();
    }

    /// <summary>Karty na cala szerokosc obszaru tresci.</summary>
    protected void DopasujSzerokosc()
    {
        // Kolumna tresci ma stala szerokosc — tekst zachowuje czytelna miare niezaleznie od rozmiaru okna.
        int w = Tresc.Width - Tresc.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6;
        w = Math.Min(w, Uklad.SzerokoscTresci);
        foreach (Control c in Tresc.Controls) if (c.Tag as string != "stala") c.Width = Math.Max(400, w);
    }

    /// <summary>Po zmianie marginesow okna karty musza na nowo dobrac szerokosc.</summary>
    public void Odswiez() => DopasujSzerokosc();

    protected T Dodaj<T>(T c) where T : Control
    {
        c.Margin = new Padding(0, 0, 0, 16);
        Tresc.Controls.Add(c);
        DopasujSzerokosc();
        return c;
    }
}

public sealed class OknoGlowne : Form
{
    readonly Kontekst _k;
    readonly Panel _menu = new() { Dock = DockStyle.Left, Width = 250, BackColor = Color.White, Padding = new Padding(18, 22, 18, 18) };
    readonly Panel _obszar = new() { Dock = DockStyle.Fill, BackColor = Styl.Tlo, Padding = new Padding(36, 28, 24, 16) };
    readonly Label _tytul = E.Tytul("");
    readonly Label _podtytul = E.Podtytul("");
    readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Styl.Tlo };

    // Zaslona „wczytuję” nad obszarem tresci. Wejscie na ekran potrafi chwile potrwac (R2 Platnik pyta
    // o wersje instalacji i baz), a bez tego okno wygladalo na zawieszone. Praca ekranow jest naprawde
    // asynchroniczna, wiec watek interfejsu jest w tym czasie wolny i kolowrotek sie kreci.
    // Nie Dock.Fill: dwie kontrolki Fill w jednym rodzicu dziela miedzy siebie miejsce w kolejnosci
    // ukladania i zaslona potrafi dostac zerowy rozmiar. Ustawiamy jej prostokat wprost na obszar ekranu.
    readonly Panel _ladowanie = new() { BackColor = Styl.Tlo, Visible = false };
    readonly Kolowrotek _kolowrotek = new();
    readonly Label _ladowanieOpis = E.Opis("Wczytuję…");
    // Zwloka przed pokazaniem: ekran, ktory wchodzi od razu, nie ma mrugac zaslona.
    readonly System.Windows.Forms.Timer _zwlokaLadowania = new() { Interval = 180 };
    /// <summary>Pasek z wynikiem zapisu wykonanego przy starcie (po podniesieniu uprawnien) — inaczej nikt by go nie zobaczyl.</summary>
    readonly Panel _belka = new() { Dock = DockStyle.Top, Height = 62, BackColor = Styl.Mieta, Padding = new Padding(0, 8, 10, 8), Visible = false };
    readonly Label _komunikat = E.Tekst("");
    readonly List<(PozycjaMenu Pozycja, Ekran Ekran)> _ekrany = new();
    readonly string? _ekranStartowy;
    Ekran? _biezacy;

    public OknoGlowne(Kontekst k, string? ekranStartowy = null, string? komunikatZapisu = null)
    {
        _ekranStartowy = ekranStartowy;
        _k = k;
        Text = "WU_Connector — R2Płatnik ↔ wezurlop";
        Font = Styl.Czcionka(10f);
        BackColor = Styl.Tlo;
        // Okno ma staly rozmiar, bo jego zawartosc tez ma staly: menu 250 + kolumna tresci 840 + marginesy.
        // Rozciagniete zostawialo pol ekranu pustki, zwezone chowalo karty za poziomym suwakiem — ani jedno,
        // ani drugie niczego nie dawalo. Na mniejszym ekranie schodzimy do jego rozmiaru.
        var praca = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1180, 780);
        // Szerokosc tak dobrana, zeby kolumna tresci miescila pelne 840 px razem z paskiem przewijania
        // i marginesami — inaczej karty sa wezsze, niz zaprojektowano, i opisy siegaja do samej ramki.
        Size = new Size(Math.Min(1220, praca.Width - 20), Math.Min(780, praca.Height - 20));
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Styl.IkonaAplikacji();

        // ---- menu boczne z logo
        var logo = new PictureBox
        {
            Image = Styl.Obraz("logo.png"), SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Top, Height = 40, BackColor = Color.White,
        };
        var podLogo = E.Opis("WU_Connector · program kadrowy");
        podLogo.Dock = DockStyle.Top; podLogo.AutoSize = false; podLogo.Height = 44; podLogo.Padding = new Padding(2, 6, 0, 0);
        var wersja = E.Opis($"wersja {typeof(OknoGlowne).Assembly.GetName().Version?.ToString(3)}");
        wersja.Dock = DockStyle.Bottom;
        var linia = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Styl.Linia };

        var pozycje = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White, Padding = new Padding(0, 8, 0, 0) };
        Dodaj(pozycje, "", "Status", new EkranStatus(k));
        Dodaj(pozycje, "", "R2 Płatnik", new EkranInstalacje(k));
        Dodaj(pozycje, "", "Połączenie", new EkranPolaczenie(k));
        Dodaj(pozycje, "", "Podgląd wysyłki", new EkranPodglad(k));
        Dodaj(pozycje, "", "Dziennik", new EkranDziennik(k));

        _menu.Controls.Add(pozycje);
        _menu.Controls.Add(podLogo);
        _menu.Controls.Add(logo);
        _menu.Controls.Add(wersja);

        // ---- obszar tresci
        var naglowek = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Styl.Tlo };
        _tytul.Location = new Point(0, 0);
        _podtytul.Location = new Point(2, 42);
        naglowek.Controls.Add(_tytul);
        naglowek.Controls.Add(_podtytul);


        // Wynik zapisu, ktory wykonal sie przed otwarciem okna (po UAC) — pokazujemy go u gory, raz.
        _komunikat.Location = new Point(18, 17);
        _komunikat.BackColor = Styl.Mieta;
        _belka.Controls.Add(_komunikat);
        if (komunikatZapisu is not null)
        {
            _komunikat.Text = komunikatZapisu;
            _belka.Visible = true;
        }

        // Zaslona musi byc nad ekranem, ale pod belka komunikatu — stad kolejnosc dodawania i BringToFront nizej.
        _ladowanie.Controls.AddRange(new Control[] { _kolowrotek, _ladowanieOpis });
        _ladowanie.Resize += (_, _) =>
        {
            _kolowrotek.Location = new Point((_ladowanie.Width - _kolowrotek.Width) / 2, _ladowanie.Height / 2 - 46);
            _ladowanieOpis.Location = new Point((_ladowanie.Width - _ladowanieOpis.Width) / 2, _ladowanie.Height / 2 - 6);
        };
        _zwlokaLadowania.Tick += (_, _) =>
        {
            _zwlokaLadowania.Stop();
            _ladowanie.Bounds = _host.Bounds;
            _ladowanie.Visible = true;
            _ladowanie.BringToFront();
        };
        _host.Resize += (_, _) => { if (_ladowanie.Visible) _ladowanie.Bounds = _host.Bounds; };
        _obszar.Controls.Add(_ladowanie);
        _obszar.Controls.Add(_host);
        _obszar.Controls.Add(_belka);
        _obszar.Controls.Add(naglowek);

        // Na pelnym ekranie kolumna tresci (840 px) nie rozciaga sie — zeby nie zostawala pusta polowa ekranu,
        // srodkujemy ja razem z naglowkiem, zamiast odbierac uzytkownikowi maksymalizacje okna.
        Resize += (_, _) => Wysrodkuj();
        Controls.Add(_obszar);
        Controls.Add(linia);
        Controls.Add(_menu);
        linia.BringToFront();

        // Po podniesieniu uprawnien wracamy na ten ekran, na ktorym uzytkownik zatwierdzil akcje.
        Load += async (_, _) => await PokazAsync(
            _ekrany.FirstOrDefault(e => e.Ekran.Tytul == _ekranStartowy).Ekran ?? _ekrany[0].Ekran);
    }

    void Dodaj(FlowLayoutPanel menu, string glif, string nazwa, Ekran ekran)
    {
        var p = new PozycjaMenu { Glif = glif, Text = nazwa, Width = 212, Margin = new Padding(0, 0, 0, 4) };
        p.Click += async (_, _) => await PokazAsync(ekran);
        menu.Controls.Add(p);
        _ekrany.Add((p, ekran));
    }

    public IReadOnlyList<Ekran> Ekrany => _ekrany.Select(e => e.Ekran).ToList();

    /// <summary>Nazwa ekranu, na ktorym stoi uzytkownik — podniesione okno otwiera sie na tym samym.</summary>
    public string BiezacyEkran => _biezacy?.Tytul ?? "";

    /// <summary>
    /// Okno nie ma czym wypelnic calego ekranu: kolumna tresci ma stala szerokosc, bo dluzsze wiersze gorzej
    /// sie czyta. Dlatego maksymalizacja powieksza okno do rozsadnego rozmiaru i srodkuje je, zamiast
    /// rozciagac na 1900 px i zostawiac polowe ekranu pustej.
    /// </summary>
    const int MaksymalnaSzerokosc = 1280, MaksymalnaWysokosc = 960;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct GraniceOkna
    {
        public Point Rezerwa, MaksRozmiar, MaksPozycja, MinPrzeciaganie, MaksPrzeciaganie;
    }

    /// <summary>
    /// Windows pyta okno o jego granice przed maksymalizacja (WM_GETMINMAXINFO) — tu mu je podajemy.
    /// Robimy to tak, a nie przestawianiem WindowState w OnResize, bo tamto walczy z systemem w trakcie
    /// animacji okna (w probie konczylo sie zminimalizowanym oknem).
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        const int WmGetMinMaxInfo = 0x0024, WmSysCommand = 0x0112, ScMaksymalizuj = 0xF030;
        // Blokujemy kazda droge do maksymalizacji: przycisk, dwuklik w pasek tytulu, Win+strzalka, przeciagniecie
        // okna do gornej krawedzi. Wszystkie koncza sie tym samym poleceniem systemowym.
        if (m.Msg == WmSysCommand && ((int)m.WParam & 0xFFF0) == ScMaksymalizuj) return;
        if (m.Msg == WmGetMinMaxInfo)
        {
            var praca = Screen.FromHandle(Handle).WorkingArea;
            int w = Math.Min(praca.Width, MaksymalnaSzerokosc), h = Math.Min(praca.Height, MaksymalnaWysokosc);
            var g = System.Runtime.InteropServices.Marshal.PtrToStructure<GraniceOkna>(m.LParam);
            g.MaksRozmiar = new Point(w, h);
            g.MaksPozycja = new Point((praca.Width - w) / 2, (praca.Height - h) / 2);   // wzgledem monitora
            g.MaksPrzeciaganie = new Point(w, h);                                       // mysza tez nie szerzej
            System.Runtime.InteropServices.Marshal.StructureToPtr(g, m.LParam, false);
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Lewy margines obszaru tresci tak dobrany, by kolumna stala pośrodku wolnego miejsca.</summary>
    void Wysrodkuj()
    {
        int dostepne = ClientSize.Width - _menu.Width - 1;
        int kolumna = Uklad.SzerokoscTresci + 12 + SystemInformation.VerticalScrollBarWidth + 6;
        int lewy = Math.Max(36, (dostepne - kolumna) / 2);
        if (_obszar.Padding.Left == lewy) return;
        _obszar.Padding = new Padding(lewy, 28, Math.Max(24, lewy - 12), 16);
        _biezacy?.Odswiez();
    }

    public async Task PokazAsync(Ekran e)
    {
        foreach (var (p, x) in _ekrany) p.Wybrana = x == e;
        _tytul.Text = e.Tytul;
        _podtytul.Text = e.Podtytul;
        if (_biezacy is not null && _biezacy != e) _belka.Visible = false;   // komunikat dotyczy ekranu, na ktorym stanelismy
        if (_biezacy != e)
        {
            _host.Controls.Clear();
            _host.Controls.Add(e);
            _biezacy = e;
        }

        // Zaslona pojawia sie dopiero po 180 ms, wiec szybkie ekrany wchodza bez mrugniecia,
        // a wolne (R2 Platnik: odpytanie instalacji i baz) od razu mowia, ze cos sie dzieje.
        _zwlokaLadowania.Stop();
        _zwlokaLadowania.Start();
        try { await e.PokazAsync(); }
        finally
        {
            _zwlokaLadowania.Stop();
            _ladowanie.Visible = false;
        }
    }

    /// <summary>
    /// Akcja wymagajaca uprawnien przejeta przez podniesione okno — to chowamy do zasobnika, zeby nie bylo dwoch.
    /// Wola ekran po tym, jak UAC sie powiodlo.
    /// </summary>
    public void PrzekazanoPodniesionemu() => Hide();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Zamkniecie okna chowa je do zasobnika; wyjscie z aplikacji — z menu ikony.
        if (e.CloseReason == CloseReason.UserClosing && !Program.Wyjscie) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }
}

