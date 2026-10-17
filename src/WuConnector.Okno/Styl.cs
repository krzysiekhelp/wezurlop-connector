using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace WuConnector.Okno;
/// <summary>
/// Paleta i typografia wg SPOJNOSC-WIZUALNA.md 1.0 (nadzor wezurlop, 20.09.2026).
/// Dwie reguly, ktore znacza wiecej niz same wartosci:
///   1. zielen oznacza AKCJE (przycisk glowny, aktywna pozycja, odnosnik) — nigdy "wszystko w porzadku",
///   2. stan normalny nie dostaje koloru; kolorem swieci wylacznie odstepstwo (uwaga, blad).
/// </summary>
public static class Styl
{
    public static readonly Color Granat = ColorTranslator.FromHtml("#15181D");        // tekst glowny (naglowki, dane)
    public static readonly Color Tekst = ColorTranslator.FromHtml("#2A2F39");
    public static readonly Color Szary = ColorTranslator.FromHtml("#6B7280");         // podpis, opis, etykieta
    public static readonly Color Zielony = ColorTranslator.FromHtml("#12874A");       // AKCJA
    public static readonly Color ZielonyCiemny = ColorTranslator.FromHtml("#0E6E3C"); // akcja — najechanie
    public static readonly Color Pomarancz = ColorTranslator.FromHtml("#C07C00");     // uwaga
    public static readonly Color Koral = ColorTranslator.FromHtml("#B42318");         // blad
    public static readonly Color Linia = ColorTranslator.FromHtml("#E6E8EC");
    public static readonly Color Obrys = ColorTranslator.FromHtml("#D4D8DF");         // pole tekstowe, przycisk drugorzedny
    public static readonly Color Mieta = ColorTranslator.FromHtml("#E8F4EC");         // akcja — tlo (zaznaczona pozycja)
    public static readonly Color Niebieski = ColorTranslator.FromHtml("#F0F2F5");     // stan normalny: bez koloru
    public static readonly Color Zolty = ColorTranslator.FromHtml("#FDF3E2");         // tlo uwagi
    public static readonly Color Rozowy = ColorTranslator.FromHtml("#FDECEA");        // tlo bledu
    public static readonly Color Tlo = ColorTranslator.FromHtml("#F5F6F8");
    public static readonly Color KropkaStanu = ColorTranslator.FromHtml("#B9BFC9");

    /// <summary>Krój systemowy — ustalone z nadzorem: osadzony IBM Plex wyglada gorzej przy 125% i 150%.</summary>
    public static readonly string Rodzina = Dostepna("Segoe UI Variable Text") ?? "Segoe UI";
    public static readonly string RodzinaNaglowka = Dostepna("Segoe UI Variable Display") ?? "Segoe UI";
    public static readonly string Ikony = Dostepna("Segoe Fluent Icons") ?? "Segoe MDL2 Assets";

    /// <summary>Skala wg §3 (px → pt): 20 → 15, 16 → 12, 14 → 10.5, 13 → 10, 12 → 9, 11 → 8.25.</summary>
    public const float TytulEkranu = 15f, NaglowekKarty = 12f, Tresc = 10.5f, Podpis = 9f, NaglowekKolumny = 8.25f;

    /// <summary>Wysokosc kontrolki wg §4: pole, lista i przycisk w jednym rzedzie maja te sama.</summary>
    public const int WysokoscKontrolki = 38, WysokoscMniejszej = 32, Promien = 8, PromienKarty = 10;

    /// <summary>Format daty i godziny identyczny po obu stronach (§6), zawsze czas lokalny.</summary>
    public static string DataGodzina(DateTimeOffset t) => t.ToLocalTime().ToString("dd.MM.yyyy, HH:mm");
    public static string DataGodzina(DateTime t) => t.ToString("dd.MM.yyyy, HH:mm");

    /// <summary>Czas wzgledny jest pierwsza forma, bezwzgledny druga (§6).</summary>
    public static string Wzglednie(DateTime t)
    {
        var d = DateTime.Now - t;
        return d < TimeSpan.FromMinutes(1) ? "przed chwilą"
             : d < TimeSpan.FromHours(1) ? $"{(int)d.TotalMinutes} min temu"
             : d < TimeSpan.FromDays(1) ? $"{(int)d.TotalHours} h temu"
             : $"{(int)d.TotalDays} dni temu";
    }

    public static Font Czcionka(float rozmiar, FontStyle s = FontStyle.Regular) => new(Rodzina, rozmiar, s);

    /// <summary>
    /// Liczby w kolumnach (§7): cyfry o stalej szerokosci, zeby sasiednie wiersze ustawialy sie w slupku.
    /// „Segoe UI" ma cyfry tabelaryczne; wariant „Variable Text" — nie, dlatego tu jest osobny kroj.
    /// </summary>
    public static Font Cyfry(float rozmiar, FontStyle s = FontStyle.Regular) => new("Segoe UI", rozmiar, s);
    public static Font Naglowek(float rozmiar) => new(RodzinaNaglowka, rozmiar, FontStyle.Bold);
    public static Font Ikona(float rozmiar) => new(Ikony, rozmiar);

    static string? Dostepna(string nazwa)
        => new InstalledFontCollection().Families.Any(f => f.Name.Equals(nazwa, StringComparison.OrdinalIgnoreCase)) ? nazwa : null;

    static readonly Dictionary<string, Image> Obrazy = new();

    /// <summary>
    /// Obraz z zasobow, wczytywany raz. Dwa powody, zeby trzymac go w pamieci, a nie czytac przy kazdym uzyciu:
    /// ikona zasobnika odswieza sie co pol minuty, a `Image.FromStream` wymaga, by strumien zyl tak dlugo jak obraz
    /// — wczesniejsza wersja zamykala strumien od razu i rysowanie moglo sie wywrocic.
    /// </summary>
    public static Image Obraz(string nazwa)
    {
        if (!Obrazy.TryGetValue(nazwa, out var wzorzec))
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(nazwa)
                          ?? throw new InvalidOperationException("Brak zasobu " + nazwa);
            var pamiec = new MemoryStream();
            s.CopyTo(pamiec);
            pamiec.Position = 0;
            Obrazy[nazwa] = wzorzec = Image.FromStream(pamiec);
        }
        // Kopia, bo wolajacy (PictureBox, `using`) zamyka obraz po sobie — wzorzec zostaje w pamieci na nastepny raz.
        return (Image)wzorzec.Clone();
    }

    public static GraphicsPath Zaokraglony(Rectangle r, int promien)
    {
        int d = promien * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Ikona aplikacji — maskotka wezurlop.pl, ta sama, ktora serwis ma jako favicone.</summary>
    public static Icon IkonaAplikacji()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ikona.ico")
                      ?? throw new InvalidOperationException("Brak zasobu ikona.ico");
        return new Icon(s);
    }

    /// <summary>
    /// Ikona zasobnika: ta sama maskotka co ikona aplikacji, z kropka stanu w rogu
    /// (zielona / pomaranczowa / czerwona wedlug wieku ostatniego cyklu).
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr uchwyt);

    static readonly Dictionary<int, Icon> IkonyStanu = new();

    public static Icon IkonaZasobnika(Color stan)
    {
        // Ikony sa cztery (stan connectora) i zyja do konca procesu — budowanie ich co cykl zegara
        // zostawialo po sobie uchwyt systemowy, bo `Icon.FromHandle` nie przejmuje go na wlasnosc.
        if (IkonyStanu.TryGetValue(stan.ToArgb(), out var gotowa)) return gotowa;
        return IkonyStanu[stan.ToArgb()] = ZbudujIkone(stan);
    }

    static Icon ZbudujIkone(Color stan)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var maskotka = Obraz("mascot.png"))
            {
                // Maskotka wpisana w kwadrat, z miejscem na kropke w prawym dolnym rogu.
                float skala = Math.Min(26f / maskotka.Width, 26f / maskotka.Height);
                int w = (int)(maskotka.Width * skala), h = (int)(maskotka.Height * skala);
                g.DrawImage(maskotka, (28 - w) / 2, (28 - h) / 2, w, h);
            }
            using (var kropka = new SolidBrush(stan)) g.FillEllipse(kropka, 20, 20, 11, 11);
            using (var obw = new Pen(Color.White, 1.6f)) g.DrawEllipse(obw, 20, 20, 11, 11);
        }
        // Kopia zarzadzana (Clone) ma wlasny uchwyt; ten z GetHicon trzeba zwolnic samemu, inaczej wycieka.
        IntPtr uchwyt = bmp.GetHicon();
        try { using var tymczasowa = Icon.FromHandle(uchwyt); return (Icon)tymczasowa.Clone(); }
        finally { DestroyIcon(uchwyt); }
    }
}

/// <summary>Biala karta z zaokraglonymi rogami i delikatna ramka — jak .steps article / .feature-card.</summary>
public class Karta : Panel
{
    public Color KolorTla { get; set; } = Color.White;
    public int Promien { get; set; } = Styl.PromienKarty;
    public bool Ramka { get; set; } = true;
    public Color KolorRamki { get; set; } = Styl.Linia;

    /// <summary>
    /// Pasek naglowka karty (np. rozwinieta instalacja): tlo i wysokosc. Gorne rogi bierze z karty,
    /// wiec pasek nie wychodzi poza zaokraglenie. Zero wysokosci = brak paska.
    /// </summary>
    public Color? KolorNaglowka { get; set; }
    public int WysokoscNaglowka { get; set; }

    /// <summary>Y, od ktorego zaczynaja sie bloki (pod naglowkiem karty) — punkt odniesienia przy przeliczaniu.</summary>
    public int PoczatekTresci { get; set; }

    /// <summary>
    /// Odstep miedzy sekcjami. Zero = sekcje stykaja sie (zwykle karty). Wiecej niz zero = sekcje sa
    /// osobnymi plytami i miedzy nimi widac tlo karty — stad granica, bez kresek i paskow.
    /// </summary>
    public int OdstepCzesci { get; set; }

    /// <summary>
    /// Ustawia bloki i stopke jeden pod drugim i dociaga wysokosc karty. Wolane, gdy zawartosc urosla —
    /// komunikat z R2 bywa dluzszy niz jedna linia, a karta nie moze go przykryc ani uciac.
    /// </summary>
    public void Przelicz()
    {
        var czesci = Controls.Cast<Control>().Where(c => c is Blok or PasekZapisu).OrderBy(c => c.Top).ToList();
        if (czesci.Count == 0) return;
        int y = PoczatekTresci + OdstepCzesci;
        Control? ostatnia = null;
        foreach (var c in czesci)
        {
            if (!Uklad.Widoczny(c)) continue;   // karta zwinieta — tresc jest, tylko schowana
            if (c is Blok b) b.Przelicz();
            c.Top = y;
            y += c.Height + OdstepCzesci;
            ostatnia = c;
        }
        Height = ostatnia is null
            ? (WysokoscNaglowka > 0 ? WysokoscNaglowka : PoczatekTresci)
            // Plyty maja juz odstep pod ostatnia sekcja; bez plyt stopka siega dolu karty i zostaje
            // piksel na dolna kreske ramki, ktorej by inaczej nie bylo widac.
            : OdstepCzesci > 0 ? y
            : y + (ostatnia is PasekZapisu ? 1 : Uklad.Srednia);
    }

    public Karta()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Padding = new Padding(20);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = Styl.Zaokraglony(new Rectangle(0, 0, Width - 1, Height - 1), Promien);
        using (var b = new SolidBrush(KolorTla)) g.FillPath(b, p);
        if (KolorNaglowka is Color kolorNaglowka && WysokoscNaglowka > 0)
        {
            g.SetClip(p);   // pasek konczy sie na zaokragleniu karty, nie na prostokacie
            using (var b = new SolidBrush(kolorNaglowka)) g.FillRectangle(b, 0, 0, Width, WysokoscNaglowka);
            g.ResetClip();
            using var kreska = new Pen(KolorRamki);
            g.DrawLine(kreska, 0, WysokoscNaglowka, Width, WysokoscNaglowka);
        }
        if (Ramka) using (var pen = new Pen(KolorRamki)) g.DrawPath(pen, p);
    }
}

public enum RodzajPrzycisku { Glowny, Drugi, Tekstowy }

/// <summary>.btn-primary (zielony gradient) i .btn-ghost (bialy z ramka).</summary>
public class Przycisk : Button
{
    public RodzajPrzycisku Rodzaj { get; set; } = RodzajPrzycisku.Glowny;
    /// <summary>Kolor pod zaokraglonym przyciskiem, gdy tlo rodzica nie jest jednolite (np. mietowy naglowek karty).</summary>
    public Color? Podklad { get; set; }
    bool _nad;

    public Przycisk()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Styl.Czcionka(Styl.Tresc, FontStyle.Bold);
        Height = Styl.WysokoscKontrolki;
        Cursor = Cursors.Hand;
        Padding = new Padding(18, 0, 18, 0);
    }

    public override Size GetPreferredSize(Size proposed)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        return new Size(t.Width + Padding.Horizontal + 8, Height);
    }

    protected override void OnMouseEnter(EventArgs e) { _nad = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _nad = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor is { A: 255 } c ? c : Color.White);
        if (Parent is Karta k) g.Clear(k.KolorTla);
        if (Podklad is Color pod) g.Clear(pod);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var p = Styl.Zaokraglony(r, Styl.Promien);
        Color tekst;
        if (Rodzaj == RodzajPrzycisku.Glowny)
        {
            var a = Enabled ? (_nad ? Styl.ZielonyCiemny : Styl.Zielony) : Color.FromArgb(160, 200, 185);
            using var b = new LinearGradientBrush(r, a, Enabled ? Styl.ZielonyCiemny : a, 135f);
            g.FillPath(b, p);
            tekst = Color.White;
        }
        else if (Rodzaj == RodzajPrzycisku.Drugi)
        {
            using (var b = new SolidBrush(_nad ? Styl.Niebieski : Color.White)) g.FillPath(b, p);
            using (var pen = new Pen(ColorTranslator.FromHtml("#DBE5EF"))) g.DrawPath(pen, p);
            tekst = Enabled ? Styl.Tekst : Styl.Szary;
        }
        else tekst = _nad ? Styl.ZielonyCiemny : Styl.Zielony;
        TextRenderer.DrawText(g, Text, Font, r, tekst, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public enum Poziom { Ok, Uwaga, Blad, Info, Wylaczony }

/// <summary>Znacznik stanu — pastylka jak etykiety "Urlop" / "Praca" w panelu aplikacji.</summary>
public class Znacznik : Control
{
    Poziom _poziom = Poziom.Info;
    public Poziom Poziom { get => _poziom; set { _poziom = value; Invalidate(); } }

    public Znacznik()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Styl.Czcionka(8.5f, FontStyle.Bold);
        Height = 24;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Width = TextRenderer.MeasureText((_poziom == Poziom.Ok ? "✓ " : "") + Text, Font).Width + 22;
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var (tlo, tekst) = _poziom switch
        {
            // Stan normalny bez koloru (§2): szare tlo, zwykly tekst — zielen zostaje dla akcji.
            Poziom.Ok => (Styl.Niebieski, Styl.Tekst),
            Poziom.Uwaga => (Styl.Zolty, Styl.Pomarancz),
            Poziom.Blad => (Styl.Rozowy, Styl.Koral),
            Poziom.Wylaczony => (Styl.Niebieski, Styl.Szary),
            _ => (Styl.Niebieski, Styl.Szary),
        };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = Styl.Zaokraglony(new Rectangle(0, 0, Width - 1, Height - 1), (Height - 1) / 2);
        using (var b = new SolidBrush(tlo)) e.Graphics.FillPath(b, p);
        string napis = _poziom == Poziom.Ok && !Text.StartsWith("✓") ? "✓ " + Text : Text;   // stan normalny bez koloru
        TextRenderer.DrawText(e.Graphics, napis, Font, ClientRectangle, tekst, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// Komunikat na pelna szerokosc bloku: wynik testu, blad, ostrzezenie. W odroznieniu od znacznika
/// (pastylka na jedno slowo) lamie tekst i rosnie w dol — dlugi komunikat z R2 czy z SQL ma sie zmiescic,
/// a nie wyjsc poza karte.
/// </summary>
public class Komunikat : Control
{
    const int Bok = 12, Gora = 9;
    Poziom _poziom = Poziom.Info;
    public Poziom Poziom { get => _poziom; set { _poziom = value; Invalidate(); } }

    public Komunikat()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = Styl.Czcionka(Styl.Tresc);
        Height = Styl.WysokoscKontrolki;
    }

    protected override void OnTextChanged(EventArgs e) { Przelicz(); base.OnTextChanged(e); }
    protected override void OnResize(EventArgs e) { Przelicz(); base.OnResize(e); }

    void Przelicz()
    {
        if (Width <= 2 * Bok) return;
        int wysokoscTekstu = TextRenderer.MeasureText(Text, Font, new Size(Width - 2 * Bok, 0), TextFormatFlags.WordBreak).Height;
        int nowa = Math.Max(Styl.WysokoscMniejszej, wysokoscTekstu + 2 * Gora);
        if (nowa != Height) Height = nowa;   // SizeChanged podnosi wysokosc karty (Karta.Przelicz)
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var (tlo, tekst) = _poziom switch
        {
            Poziom.Ok => (Styl.Mieta, Styl.ZielonyCiemny),
            Poziom.Uwaga => (Styl.Zolty, Styl.Pomarancz),
            Poziom.Blad => (Styl.Rozowy, Styl.Koral),
            Poziom.Wylaczony => (Styl.Niebieski, Styl.Szary),
            _ => (Styl.Niebieski, Styl.Tekst),
        };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var p = Styl.Zaokraglony(new Rectangle(0, 0, Width - 1, Height - 1), Styl.Promien);
        using (var b = new SolidBrush(tlo)) e.Graphics.FillPath(b, p);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Bok, Gora, Width - 2 * Bok, Height - 2 * Gora), tekst,
                              TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }
}

/// <summary>Pozycja menu bocznego: ikona + napis; wybrana na niebieskim tle jak "Kalendarz" w panelu.</summary>
public class PozycjaMenu : Control
{
    public string Glif { get; set; } = "";
    bool _wybrana, _nad;
    public bool Wybrana { get => _wybrana; set { _wybrana = value; Invalidate(); } }

    public PozycjaMenu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 44;
        Cursor = Cursors.Hand;
        Font = Styl.Czcionka(10.5f, FontStyle.Bold);
        BackColor = Color.White;
    }

    protected override void OnMouseEnter(EventArgs e) { _nad = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _nad = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_wybrana || _nad)
        {
            using var p = Styl.Zaokraglony(new Rectangle(0, 2, Width - 1, Height - 5), 10);
            // Aktywna pozycja = akcja (§2): tlo akcji, nie neutralne.
            using var b = new SolidBrush(_wybrana ? Styl.Mieta : Styl.Niebieski);
            g.FillPath(b, p);
        }
        var kolor = _wybrana ? Styl.Zielony : Styl.Tekst;
        using (var f = Styl.Ikona(13f))
            TextRenderer.DrawText(g, Glif, f, new Rectangle(14, 0, 28, Height), _wybrana ? Styl.Zielony : Styl.Szary,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(50, 0, Width - 50, Height), kolor, TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Etykieta z odstepami — najczesciej uzywane zestawy.</summary>
public static class E
{
    public static Label Tytul(string t) => new() { Text = t, Font = Styl.Naglowek(Styl.TytulEkranu), ForeColor = Styl.Granat, AutoSize = true, BackColor = Color.Transparent };
    public static Label Podtytul(string t) => new() { Text = t, Font = Styl.Czcionka(10.5f), ForeColor = Styl.Szary, AutoSize = true, BackColor = Color.Transparent };
    public static Label Naglowek(string t) => new() { Text = t, Font = Styl.Naglowek(Styl.NaglowekKarty), ForeColor = Styl.Granat, AutoSize = true, BackColor = Color.Transparent };
    public static Label Tekst(string t, bool pogrubiony = false) => new()
    { Text = t, Font = Styl.Czcionka(Styl.Tresc, pogrubiony ? FontStyle.Bold : FontStyle.Regular), ForeColor = Styl.Tekst, AutoSize = true, BackColor = Color.Transparent };
    public static Label Opis(string t) => new() { Text = t, Font = Styl.Czcionka(Styl.Podpis), ForeColor = Styl.Szary, AutoSize = true, BackColor = Color.Transparent };

    public static TextBox Pole(string wartosc = "", bool haslo = false) => new()
    {
        Text = wartosc, Font = Styl.Czcionka(10.5f), ForeColor = Styl.Tekst, BorderStyle = BorderStyle.FixedSingle,
        UseSystemPasswordChar = haslo, Height = Styl.WysokoscKontrolki,
    };
}
