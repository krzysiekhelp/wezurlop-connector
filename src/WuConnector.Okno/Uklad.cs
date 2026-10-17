namespace WuConnector.Okno;

/// <summary>
/// Siatka okna wg makiety z 20.09.2026: kolumna tresci o stalej szerokosci, dwie kolumny pol po 344,
/// odstepy ze skali 4/8/12/16/24, pola z etykieta nad kontrolka i jednakowej wysokosci.
/// Wszystkie ekrany ukladaja sie tymi samymi wartosciami — nic nie jest pozycjonowane "na oko".
/// </summary>
public static class Uklad
{
    public const int SzerokoscTresci = 840;   // miara czytelnosci: linia tekstu nie dluzsza niz ~90 znakow
    public const int Kolumna = 344, Odstep = 24, Margines = 24;
    public const int Mala = 8, Srednia = 16, Duza = 24, Ogromna = 32;

    public static int X(int kolumna) => Margines + kolumna * (Kolumna + Odstep);

    /// <summary>
    /// PUŁAPKA WinForms: `Control.Visible` zwraca false takze wtedy, gdy kontrolka jest widoczna, ale okno
    /// nie zostalo jeszcze pokazane. Uklad liczony w konstruktorze bral wiec wszystko za schowane i zwijal
    /// karty do samego naglowka. Dlatego o schowaniu decyduje tu jawna lista, a nie stan z systemu.
    /// </summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> Schowane = new();

    public static void Pokaz(Control c, bool widoczny)
    {
        c.Visible = widoczny;
        if (widoczny) Schowane.Remove(c); else Schowane.AddOrUpdate(c, "");
    }

    public static bool Widoczny(Control c) => !Schowane.TryGetValue(c, out _);

    /// <summary>
    /// Wysokosc etykiety po zawinieciu tekstu. UWAGA: `Label.PreferredHeight` to wysokosc JEDNEJ linii,
    /// niezaleznie od dlugosci tekstu — uzyty do ukladu ucinal dluzsze komunikaty.
    /// </summary>
    /// <summary>
    /// Wysokosc zawinietego tekstu. Mierzymy na szerokosci mniejszej o 8 px niz rzeczywista — TextRenderer
    /// i etykieta licza zawijanie odrobine inaczej, a wolimy jedna linie zapasu niz ucieta ostatnia.
    /// </summary>
    public static int WysokoscTekstu(string tekst, Font czcionka, int szerokosc)
        => tekst.Length == 0
            ? TextRenderer.MeasureText("X", czcionka).Height
            : TextRenderer.MeasureText(tekst, czcionka, new Size(Math.Max(40, szerokosc - 8), 4096), TextFormatFlags.WordBreak).Height;

    public static int WysokoscEtykiety(Label l)
        => Math.Max(l.AutoSize ? l.PreferredHeight : l.Height,
                    l.MaximumSize.Width > 0 ? WysokoscTekstu(l.Text, l.Font, l.MaximumSize.Width) : 0);

    /// <summary>
    /// Naglowek karty: tytul, pod nim jedno zdanie wyjasnienia. Zwraca Y, od ktorego zaczyna sie tresc.
    /// </summary>
    public static int NaglowekKarty(Karta karta, string tytul, string? opis = null)
        => NaglowekKarty(karta, tytul, opis, out _);

    /// <param name="etykietaOpisu">Zdanie wyjasnienia — do schowania, gdy karta ma sie zwinac do minimum.</param>
    public static int NaglowekKarty(Karta karta, string tytul, string? opis, out Label? etykietaOpisu)
    {
        etykietaOpisu = null;
        // ustawione na koncu metody: karta wie, od ktorego Y zaczyna uklad blokow (Karta.Przelicz)
        var t = E.Naglowek(tytul);
        t.Location = new Point(Margines, 20);
        karta.Controls.Add(t);
        int y = 20 + 24;

        if (opis is not null)
        {
            var o = E.Opis(opis);
            o.MaximumSize = new Size(karta.Width - 2 * Margines, 0);
            o.Location = new Point(Margines + 1, y);
            karta.Controls.Add(o);
            etykietaOpisu = o;
            y += WysokoscEtykiety(o);
        }
        karta.PoczatekTresci = y + Duza;
        return karta.PoczatekTresci;
    }

    /// <summary>Cienka linia dzielaca bloki w karcie — porzadkuje lepiej niz sam odstep.</summary>
    /// <summary>Kreska rozdzielajaca wiersze. Zaczyna sie o piksel od krawedzi, zeby nie przykryc ramki karty.</summary>
    public static Panel Linia(int y, int szerokosc) => new()
    {
        BackColor = Styl.Linia, Height = 1, Location = new Point(1, y), Width = szerokosc - 2,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
    };

    /// <summary>Zielona kreska przed tytulem sekcji — wariant „sam odstep”, do podmiany z paskiem tytulu.</summary>
    public const int SzerokoscAkcentu = 11;

    public static Panel Akcent(int y) => new()
    {
        BackColor = Styl.Zielony, Width = 3, Height = 15, Location = new Point(Margines, y + 4),
    };

    public const int WysokoscPaskaTytulu = 34;

    /// <summary>
    /// Pasek z tytulem sekcji: jasne tlo z kreska nad i pod, tresc sekcji zostaje na bieli. Pasek jest
    /// zarazem granica — sekcje zaczyna jej nazwa, a nie pusty pas ani kreska przez cala karte.
    /// </summary>
    public static Panel PasekTytulu(string tytul, int szerokosc, int y)
    {
        var pasek = new PasekSekcji
        {
            Location = new Point(1, y), Width = szerokosc - 2, Height = WysokoscPaskaTytulu,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        var t = E.Tekst(tytul, true);
        t.Location = new Point(Margines - 1, 9);
        pasek.Controls.Add(t);
        return pasek;
    }

    sealed class PasekSekcji : Panel
    {
        public PasekSekcji()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = ColorTranslator.FromHtml("#F4F7FA");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var kreska = new Pen(Styl.Linia);
            e.Graphics.DrawLine(kreska, 0, 0, Width, 0);
            e.Graphics.DrawLine(kreska, 0, Height - 1, Width, Height - 1);
            base.OnPaint(e);
        }
    }
}

/// <summary>Etykieta nad kontrolka — jedyny dopuszczalny sposob podpisania pola w tym oknie.</summary>
public sealed class Pole : Panel
{
    public Control Kontrolka { get; }

    public Pole(string etykieta, Control kontrolka, int szerokosc = Uklad.Kolumna)
    {
        Kontrolka = kontrolka;
        BackColor = Color.Transparent;
        Width = szerokosc;
        Height = 22 + Styl.WysokoscKontrolki;

        var l = E.Opis(etykieta);
        l.Location = new Point(0, 0);
        kontrolka.Location = new Point(0, 22);
        // Pola tekstowe i listy ciagna sie na cala kolumne; liczby, godziny i wlasne rzedy zostaja przy swojej szerokosci.
        if (kontrolka is TextBox or ComboBox) kontrolka.Width = szerokosc;
        if (kontrolka is TextBox) kontrolka.Height = Styl.WysokoscKontrolki;
        Controls.Add(l);
        Controls.Add(kontrolka);
    }
}

/// <summary>Lista wyboru o tej samej wysokosci co pole tekstowe (zwykly ComboBox ma wysokosc z czcionki).</summary>
public sealed class ListaWyboru : ComboBox
{
    public ListaWyboru()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        Font = Styl.Czcionka(Styl.Tresc);
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Styl.WysokoscKontrolki - 8;
        BackColor = Color.White;
        ForeColor = Styl.Tekst;
    }

    /// <summary>
    /// Lista plaska nie rysuje ramki, a pole tekstowe obok ma — bez tego wygladalaby jak sam tekst.
    /// Obrys dorysowujemy po malowaniu systemowym, tym samym kolorem co pola (Styl.Obrys).
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        const int WmPaint = 0x000F;
        if (m.Msg != WmPaint) return;
        using var g = Graphics.FromHwnd(Handle);
        using var p = new Pen(Enabled ? Styl.Obrys : Styl.Linia);
        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index >= 0)
            TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString(), Font,
                                  new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                                  Enabled ? Styl.Tekst : Styl.Szary, TextFormatFlags.VerticalCenter);
        base.OnDrawItem(e);
    }
}

/// <summary>
/// Blok w karcie: linia u gory, naglowek, jedno zdanie wyjasnienia i zawartosc ukladana w siatce.
/// Buduje sie go od gory do dolu (PolaWiersz / Przyciski / Wiersz), a na koncu Domknij().
/// </summary>
public sealed class Blok : Panel
{
    int _y;
    readonly int _szerokosc;

    /// <summary>Rzedy w kolejnosci ulozenia — potrzebne, gdy ktorys urosnie i trzeba przesunac te pod nim.</summary>
    readonly List<(Control[] Kontrolki, int OdstepPo)> _rzedy = new();
    int _poczatekRzedow;

    public Blok(string? tytul, string? opis, int szerokosc, bool zLinia = true)
    {
        _szerokosc = szerokosc;
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        Width = szerokosc;
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // Sekcja jest plyta na tle karty, wiec granic nie rysujemy — tytul po prostu otwiera jej tresc.
        if (zLinia) _y = Uklad.Srednia;
        if (tytul is not null)
        {
            Tytul = E.Tekst(tytul, true);
            Tytul.Location = new Point(Uklad.Margines, _y);
            Controls.Add(Tytul);
            _y += 22;
        }
        if (opis is not null)
        {
            var o = E.Opis(opis);
            o.MaximumSize = new Size(szerokosc - 2 * Uklad.Margines, 0);
            o.Location = new Point(Uklad.Margines + 1, _y);
            Controls.Add(o);
            _opis = o;
            _opisY = _y;
            _y += Uklad.WysokoscEtykiety(o) + Uklad.Mala;
        }
    }

    readonly Label? _opis;
    readonly int _opisY;

    /// <summary>Etykieta tytulu — sekcja moze dostawic obok niej wlasne drobiazgi (licznik, skrot).</summary>
    public Label? Tytul { get; }

    /// <summary>
    /// Sekcja jako biala plyta na tle karty: wlasne tlo i delikatna ramka, zamiast kreski czy paska
    /// przez cala szerokosc. Granica miedzy sekcjami to wtedy tlo karty widoczne miedzy plytami.
    /// </summary>
    public bool Plyta { get; set; }

    public Color KolorPlyty { get; set; } = Color.White;
    public Color KolorRamkiPlyty { get; set; } = ColorTranslator.FromHtml("#D4E6DB");

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Plyta)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var sciezka = Styl.Zaokraglony(new Rectangle(0, 0, Width - 1, Height - 1), Styl.Promien);
            using (var pedzel = new SolidBrush(KolorPlyty)) e.Graphics.FillPath(pedzel, sciezka);
            using (var pioro = new Pen(KolorRamkiPlyty)) e.Graphics.DrawPath(pioro, sciezka);
        }
        base.OnPaint(e);
    }

    /// <summary>
    /// Blok bywa wezszy niz kolumna, dla ktorej go zbudowano (waskie okno, pasek przewijania) — wtedy
    /// zdanie wyjasnienia musi zlamac sie na nowo, a wiersze pod nim zjechac nizej.
    /// </summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_opis is null || Width <= 2 * Uklad.Margines) return;
        _opis.MaximumSize = new Size(Width - 2 * Uklad.Margines, 0);
        int nowy = _opisY + Uklad.WysokoscEtykiety(_opis) + Uklad.Mala;
        if (nowy == _poczatekRzedow) return;
        _poczatekRzedow = nowy;
        Przelicz();
        (Parent as Karta)?.Przelicz();
    }

    /// <summary>Wiersz siatki: jedno albo dwa pola, zawsze w tych samych kolumnach.</summary>
    public void PolaWiersz(params Pole[] pola)
    {
        for (int i = 0; i < pola.Length; i++)
        {
            pola[i].Location = new Point(Uklad.X(i), _y);
            Controls.Add(pola[i]);
        }
        Zapamietaj(pola, Uklad.Srednia);
    }

    /// <summary>
    /// Pola rozlozone rowno na calej szerokosci bloku — rzad konczy sie tam, gdzie reszta sekcji,
    /// a nie w polowie karty. Dla danych, ktore naleza do siebie (serwer, login, haslo).
    /// </summary>
    public void PolaRowno(string[] etykiety, params Control[] kontrolki)
    {
        int n = kontrolki.Length;
        int calosc = _szerokosc - 2 * Uklad.Margines;
        int szerokosc = (calosc - (n - 1) * Uklad.Odstep) / n;
        var pola = new Pole[n];
        for (int i = 0; i < n; i++)
        {
            pola[i] = new Pole(etykiety[i], kontrolki[i], szerokosc)
            {
                Location = new Point(Uklad.Margines + i * (szerokosc + Uklad.Odstep), _y),
            };
            Controls.Add(pola[i]);
        }
        Zapamietaj(pola, Uklad.Srednia);
    }

    /// <summary>Akcje w osobnym rzedzie pod polami — nigdy obok nich.</summary>
    public void Przyciski(params Control[] przyciski)
    {
        int x = Uklad.Margines;
        foreach (var p in przyciski)
        {
            p.Location = new Point(x, _y);
            Controls.Add(p);
            x += p.Width + 12;
        }
        Zapamietaj(przyciski, Uklad.Srednia);
    }

    /// <summary>Dowolna kontrolka na pelna szerokosc bloku.</summary>
    public void Wiersz(Control c, int odstepPo = Uklad.Srednia)
    {
        int szerokoscTresci = _szerokosc - 2 * Uklad.Margines;
        c.Location = new Point(Uklad.Margines, _y);
        if (c is Komunikat) c.Width = szerokoscTresci;
        Controls.Add(c);

        // Etykieta w wierszu nie jest AutoSize: sami liczymy jej wysokosc z tekstu, zaraz po kazdej zmianie.
        // Z AutoSize etykieta zmieniala rozmiar dopiero przy ukladaniu okna, a wtedy bylo juz za pozno —
        // stopka karty potrafila przykryc dluzszy komunikat.
        if (c is Label l)
        {
            l.AutoSize = false;
            l.Width = szerokoscTresci;
            l.MaximumSize = new Size(szerokoscTresci, 0);
            l.TextChanged += (_, _) => Dopasuj(l);
            Dopasuj(l);
        }
        if (c is Komunikat) c.SizeChanged += (_, _) => { Przelicz(); (Parent as Karta)?.Przelicz(); };
        // Pokazanie albo schowanie wiersza (wynik testu, rzad „nowe konto”) przestawia to, co pod nim.
        c.VisibleChanged += (_, _) => { Przelicz(); (Parent as Karta)?.Przelicz(); };
        Zapamietaj(new[] { c }, odstepPo);
    }

    void Dopasuj(Label l)
    {
        l.Height = Uklad.WysokoscTekstu(l.Text, l.Font, l.Width);
        Przelicz();
        (Parent as Karta)?.Przelicz();
    }

    public void Odstep(int ile = Uklad.Mala) => Zapamietaj(Array.Empty<Control>(), ile);

    void Zapamietaj(Control[] rzad, int odstepPo)
    {
        if (_rzedy.Count == 0) _poczatekRzedow = _y;
        _rzedy.Add((rzad, odstepPo));
        _y += Wysokosc(rzad) + odstepPo;
    }

    /// <summary>Schowany wiersz nie zajmuje miejsca — inaczej karta zostawialaby po nim dziure.</summary>
    static int Wysokosc(Control[] rzad)
    {
        var widoczne = rzad.Where(Uklad.Widoczny).ToList();
        return widoczne.Count == 0 ? 0 : widoczne.Max(c => c is Label l ? Uklad.WysokoscEtykiety(l) : c.Height);
    }

    public Blok Domknij()
    {
        Przelicz();
        return this;
    }

    /// <summary>
    /// Uklada rzedy od nowa od zapamietanego poczatku — po tym, jak ktorys urosl. Odstepy zostaja te same,
    /// wiec blok zmienia sie dokladnie o przyrost zawartosci.
    /// </summary>
    public void Przelicz()
    {
        if (_rzedy.Count == 0) return;
        int y = _poczatekRzedow, dol = 0;
        foreach (var (rzad, odstepPo) in _rzedy)
        {
            foreach (var c in rzad) c.Top = y;
            int wysokosc = Wysokosc(rzad);
            if (wysokosc == 0) continue;         // caly rzad schowany — razem z odstepem po nim
            dol = Math.Max(dol, y + wysokosc);   // ostatni wiersz bez odstepu tez musi sie zmiescic
            y += wysokosc + odstepPo;
        }
        Height = Math.Max(Math.Max(y - Uklad.Mala, dol), Uklad.Duza);
    }
}

