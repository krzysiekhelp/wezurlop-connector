using System.Drawing.Drawing2D;

namespace WuConnector.Okno;

/// <summary>
/// Okno modalne na czas jednej proby: logowania do R2Platnika albo odczytu z SQL. Dopoki praca trwa,
/// krecy sie kolowrotek — bez niego test po kilkunastu sekundach wyglada jak zawieszone okno. Po
/// zakonczeniu to samo okno pokazuje wynik i czeka na „OK”, wiec komunikatu nie da sie przeoczyc.
/// </summary>
public sealed class DialogPracy : Form
{
    /// <summary>Co pokazac po zakonczeniu pracy: ton, zdanie i ewentualny szczegol (np. tresc bledu).</summary>
    public sealed record Wynik(Poziom Poziom, string Tekst, string? Szczegol = null);

    readonly Karta _karta;
    readonly Panel _rzad;
    readonly Kolowrotek _kolowrotek = new();
    /// <summary>W miejscu kolowrotka — ten sam rozmiar, wiec wiersz nie drgnie, gdy praca sie skonczy.</summary>
    readonly IkonaStanu _ikona = new();
    readonly Label _stan;
    readonly Label _szczegol = E.Opis("");
    readonly Przycisk _ok = new() { Text = "OK", Width = 150, Visible = false };
    readonly Func<Task<Wynik>>? _praca;

    DialogPracy(string tytul, string opis, string wTrakcie, Func<Task<Wynik>>? praca, Wynik? gotowy = null)
    {
        _praca = praca;
        Text = tytul;
        Font = Styl.Czcionka(10f);
        BackColor = Styl.Tlo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        // Dopoki praca trwa, okna nie da sie zamknac — zadanie i tak by sie nie zatrzymalo.
        ControlBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = Styl.IkonaAplikacji();
        ClientSize = new Size(560, 240);

        _karta = new Karta { Location = new Point(Uklad.Margines, Uklad.Margines), Width = ClientSize.Width - 2 * Uklad.Margines };
        int top = Uklad.NaglowekKarty(_karta, tytul, opis);

        // Jeden wiersz na caly dialog: po lewej kolowrotek (potem ikona wyniku), obok zdanie o stanie.
        int szerokoscRzedu = _karta.Width - 2 * Uklad.Margines;
        _stan = E.Tekst(wTrakcie);
        _stan.MaximumSize = new Size(szerokoscRzedu - 44, 0);
        _rzad = new Panel { BackColor = Color.Transparent, Width = szerokoscRzedu, Height = 34 };
        _kolowrotek.Location = new Point(0, 3);
        _ikona.Location = new Point(0, 3);
        _stan.Location = new Point(44, 8);
        Uklad.Pokaz(_ikona, false);
        _rzad.Controls.AddRange(new Control[] { _kolowrotek, _ikona, _stan });

        // Schowane przez Uklad.Pokaz, a nie samym Visible — inaczej blok liczylby ich wysokosc mimo ukrycia.
        Uklad.Pokaz(_szczegol, false);

        var b = new Blok(null, null, _karta.Width);
        b.Wiersz(_rzad, Uklad.Mala);
        b.Wiersz(_szczegol, Uklad.Mala);
        b.Location = new Point(0, top);
        _karta.Controls.Add(b.Domknij());
        _karta.Height = top + b.Height + Uklad.Srednia;

        _ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.AddRange(new Control[] { _karta, _ok });
        Ustaw();

        if (gotowy is not null)
        {
            // Tryb „tylko wynik” (zrzuty): etykiety znaja swoj rozmiar dopiero z uchwytem, wiec po pokazaniu
            // okna karta musi przeliczyc sie jeszcze raz — inaczej przycisk wszedlby na tekst.
            Pokaz(gotowy);
            Shown += (_, _) => { _karta.Przelicz(); Ustaw(); };
        }
        else Shown += async (_, _) => await PracujAsync();
    }

    void Ustaw()
    {
        // Dopoki nie ma przycisku, okno konczy sie tuz pod karta — zadnego pustego pasa pod spodem.
        ClientSize = new Size(ClientSize.Width, _karta.Bottom + (_ok.Visible ? 78 : Uklad.Margines));
        _ok.Location = new Point(ClientSize.Width - Uklad.Margines - _ok.Width, ClientSize.Height - 58);
    }

    async Task PracujAsync()
    {
        Wynik w;
        // Blad pokazujemy jego wlasnymi slowami — po to sie testuje, zeby wiedziec, co nie zagralo.
        try { w = await _praca!(); }
        catch (Exception e) { w = new Wynik(Poziom.Blad, e.Message); }
        Pokaz(w);
    }

    void Pokaz(Wynik w)
    {
        // Kolowrotek znika, a w jego miejsce wchodzi ikona stanu — wiersz zostaje ten sam.
        _kolowrotek.Stop();
        Uklad.Pokaz(_kolowrotek, false);
        _ikona.Poziom = w.Poziom;
        Uklad.Pokaz(_ikona, true);

        _stan.Font = Styl.Czcionka(12f, FontStyle.Bold);
        _stan.ForeColor = w.Poziom switch
        {
            Poziom.Ok => Styl.ZielonyCiemny,
            Poziom.Uwaga => Styl.Pomarancz,
            Poziom.Blad => Styl.Koral,
            _ => Styl.Tekst,
        };
        _stan.Text = w.Tekst;
        _rzad.Height = Math.Max(34, _stan.Bottom + 4);

        if (w.Szczegol is string s)
        {
            _szczegol.Text = s;
            Uklad.Pokaz(_szczegol, true);
        }

        _ok.Visible = true;
        AcceptButton = _ok;
        CancelButton = _ok;
        ControlBox = true;
        // Wiersz mogl urosnac (dluzszy komunikat bledu) — blok i karta dobieraja wysokosc same.
        (_rzad.Parent as Blok)?.Przelicz();
        _karta.Przelicz();
        Ustaw();
        _ok.Select();
    }

    /// <summary>Wykonuje prace z okienkiem oczekiwania i pokazuje jej wynik. Wraca, gdy uzytkownik zamknie okno.</summary>
    public static void Wykonaj(IWin32Window? rodzic, string tytul, string opis, string wTrakcie, Func<Task<Wynik>> praca)
    {
        using var okno = new DialogPracy(tytul, opis, wTrakcie, praca);
        if (rodzic is not null) okno.ShowDialog(rodzic); else okno.ShowDialog();
    }

    /// <summary>Do zrzutow dokumentacyjnych: okno w trakcie pracy albo z gotowym wynikiem.</summary>
    public static DialogPracy DoZrzutu(string tytul, string opis, string wTrakcie, Wynik? wynik = null)
        => new(tytul, opis, wTrakcie, wynik is null ? () => new TaskCompletionSource<Wynik>().Task : null, wynik);
}

/// <summary>Wynik jednym znakiem: haczyk, wykrzyknik albo krzyzyk w kolowym tle — w miejscu kolowrotka.</summary>
public sealed class IkonaStanu : Control
{
    Poziom _poziom = Poziom.Ok;
    public Poziom Poziom { get => _poziom; set { _poziom = value; Invalidate(); } }

    public IkonaStanu()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(28, 28);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var kolo = new Rectangle(1, 1, Width - 3, Height - 3);
        var kolor = _poziom switch
        {
            Poziom.Uwaga => Styl.Pomarancz,
            Poziom.Blad => Styl.Koral,
            _ => Styl.ZielonyCiemny,
        };
        using (var b = new SolidBrush(kolor)) g.FillEllipse(b, kolo);

        using var pioro = new Pen(Color.White, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float x = kolo.X, y = kolo.Y, w = kolo.Width, h = kolo.Height;
        switch (_poziom)
        {
            case Poziom.Blad:
                g.DrawLine(pioro, x + w * 0.32f, y + h * 0.32f, x + w * 0.68f, y + h * 0.68f);
                g.DrawLine(pioro, x + w * 0.68f, y + h * 0.32f, x + w * 0.32f, y + h * 0.68f);
                break;
            case Poziom.Uwaga:
                g.DrawLine(pioro, x + w * 0.5f, y + h * 0.26f, x + w * 0.5f, y + h * 0.58f);
                g.DrawLine(pioro, x + w * 0.5f, y + h * 0.72f, x + w * 0.5f, y + h * 0.73f);
                break;
            default:
                g.DrawLines(pioro, new[]
                {
                    new PointF(x + w * 0.27f, y + h * 0.52f),
                    new PointF(x + w * 0.44f, y + h * 0.69f),
                    new PointF(x + w * 0.74f, y + h * 0.33f),
                });
                break;
        }
    }
}

/// <summary>Kreci sie, dopoki trwa praca — luk obiegajacy okrag, tak jak na stronie wezurlop.</summary>
public sealed class Kolowrotek : Control
{
    readonly System.Windows.Forms.Timer _zegar = new() { Interval = 55 };
    int _kat;

    public Kolowrotek()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(28, 28);
        _zegar.Tick += (_, _) => { _kat = (_kat + 24) % 360; Invalidate(); };
        _zegar.Start();
    }

    public void Stop() => _zegar.Stop();

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(3, 3, Width - 7, Height - 7);
        using (var tlo = new Pen(Styl.Niebieski, 3f)) g.DrawEllipse(tlo, r);
        using var luk = new Pen(Styl.Zielony, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(luk, r, _kat, 110);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _zegar.Dispose();
        base.Dispose(disposing);
    }
}
