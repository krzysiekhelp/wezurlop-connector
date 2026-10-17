using WuConnector.Core.Konfiguracja;

namespace WuConnector.Okno;

/// <summary>
/// Przycisk „Zapisz" jednej sekcji (karty). Stan niesie sam przycisk: aktywny znaczy „jest zmiana i da sie ja zapisac".
/// Napis obok pojawia sie tylko wtedy, gdy jest cos do powiedzenia — co blokuje zapis albo co zostalo zapisane.
/// Kazda sekcja zapisuje sie osobno — nie trzeba wypelniac calej konfiguracji naraz.
/// </summary>
public sealed class PasekZapisu : Panel
{
    readonly Kontekst _k;
    readonly Func<List<string>, ZmianaSekcji?> _przygotuj;
    readonly Przycisk _zapisz;
    readonly Label _stan = E.Opis("");
    readonly bool _auto, _wRzedzie;
    bool _zmiany;

    /// <param name="przygotuj">
    /// Sprawdza sekcje i zwraca zmiane do naniesienia na plik (albo null, gdy nie ma czego zapisac).
    /// Bledy blokujace zapis dopisuje do listy.
    /// </param>
    /// <param name="autozapis">
    /// Sekcja zapisuje sie sama (np. zaznaczenie firmy) — stopka nie ma przycisku, zostaje w niej tylko
    /// miejsce na wynik. Zapis wola wtedy <see cref="ZapiszTeraz"/>.
    /// </param>
    /// <param name="wRzedzie">
    /// Przycisk zapisu stoi w rzedzie przyciskow sekcji (obok „Testuj…”), a nie w stopce karty — wtedy
    /// wolajacy sam wstawia <see cref="Przycisk"/> w rzad, a ten panel jest juz tylko miejscem na komunikat.
    /// </param>
    public PasekZapisu(Kontekst k, string tekst, Func<List<string>, ZmianaSekcji?> przygotuj,
                       bool autozapis = false, bool wRzedzie = false)
    {
        _k = k;
        _przygotuj = przygotuj;
        // Stopka karty: linia u gory, akcja glowna po lewej, stan po prawej (makieta 20.09.2026).
        // Tlo malujemy sami, o piksel od krawedzi — inaczej stopka przykryla by boczna ramke karty.
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        // Sekcja z autozapisem nie ma przycisku — jej stopka to waski pasek, ktory rosnie dopiero przy komunikacie.
        _auto = autozapis;
        _wRzedzie = wRzedzie;
        Height = wRzedzie ? 0 : autozapis ? 2 * Uklad.Mala + 1 : Styl.WysokoscKontrolki + 2 * Uklad.Srednia + 1;
        _zapisz = new Przycisk { Text = tekst, Location = new Point(Uklad.Margines, Uklad.Srednia + 1) };
        _zapisz.Width = Math.Max(200, _zapisz.GetPreferredSize(Size.Empty).Width);
        _zapisz.Enabled = false;   // dopoki nic nie zmieniono, nie ma czego zapisywac
        _zapisz.Visible = !autozapis;
        _stan.Location = wRzedzie ? new Point(0, 0)
            : autozapis ? new Point(Uklad.Margines, Uklad.Mala)
            : new Point(Uklad.Margines + _zapisz.Width + 12, Uklad.Srednia + 12);
        _stan.Visible = !autozapis && !wRzedzie;   // pusty napis nie ma zajmowac miejsca
        // Dluzszy komunikat (np. czego brakuje do uruchomienia uslugi) lamie sie i podnosi stopke, zamiast z niej wyjsc.
        int minimalna = Height;
        _stan.SizeChanged += (_, _) => Dopasuj(minimalna);
        _stan.VisibleChanged += (_, _) => Dopasuj(minimalna);
        _zapisz.Podklad = wRzedzie ? Color.White : Tlo;   // przycisk maluje wlasne tlo i musi znac kolor pod soba
        _zapisz.Click += (_, _) => Zapisz();
        Controls.Add(_stan);
        if (!wRzedzie) Controls.Add(_zapisz);   // w rzedzie przycisk wstawia sekcja, nie ten panel
    }

    /// <summary>Przycisk zapisu — do wstawienia w rzad przyciskow sekcji, gdy pasek dziala „w rzedzie”.</summary>
    public Przycisk Przycisk => _zapisz;

    static readonly Color Tlo = ColorTranslator.FromHtml("#FCFCFD");

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_wRzedzie) { base.OnPaint(e); return; }   // w rzedzie sekcji nie ma stopki do pomalowania
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        // Ostatnia stopka konczy karte — jej dol musi isc za zaokragleniem, zeby rog nie zrobil sie kanciasty.
        bool ostatnia = Parent is Karta k && k.Ramka && Bottom >= k.Height - 2;
        var r = new Rectangle(1, 0, Width - 2, Height - (ostatnia ? 1 : 0));
        using (var b = new SolidBrush(Tlo))
        {
            if (!ostatnia) g.FillRectangle(b, r);
            else
            {
                using var sciezka = ZaokraglonyDol(r, Styl.PromienKarty);
                g.FillPath(b, sciezka);
            }
        }
        base.OnPaint(e);
    }

    /// <summary>Prostokat z zaokraglonymi tylko dolnymi rogami — dopasowany do dolu karty.</summary>
    static System.Drawing.Drawing2D.GraphicsPath ZaokraglonyDol(Rectangle r, int promien)
    {
        int d = promien * 2;
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddLine(r.X, r.Y, r.Right, r.Y);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    void Dopasuj(int minimalna)
    {
        int trzeba = _stan.Visible ? _stan.Bottom + (_auto || _wRzedzie ? Uklad.Mala : Uklad.Srednia) : 0;
        int nowa = Math.Max(minimalna, trzeba);
        if (nowa == Height) return;
        Height = nowa;
        // W rzedzie sekcji rodzicem jest blok — on przestawia wiersze, a karta pod nim dobiera wysokosc.
        (Parent as Blok)?.Przelicz();
        ((Parent as Blok)?.Parent as Karta)?.Przelicz();
        (Parent as Karta)?.Przelicz();
    }

    /// <summary>Oznacza „niezapisane zmiany" przy kazdej zmianie pol karty. Wolac po ustawieniu wartosci poczatkowych.</summary>
    public void SledzZmiany(Control karta)
    {
        foreach (Control c in karta.Controls)
        {
            if (c == this || c.Tag as string == Ekran.BezZapisu) continue;
            switch (c)
            {
                case TextBox t: t.TextChanged += (_, _) => Zmieniono(); break;
                case CheckBox cb: cb.CheckedChanged += (_, _) => Zmieniono(); break;
                case RadioButton r: r.CheckedChanged += (_, _) => Zmieniono(); break;
                case ComboBox co: co.SelectedIndexChanged += (_, _) => Zmieniono(); break;
                case NumericUpDown n: n.ValueChanged += (_, _) => Zmieniono(); break;
                case DateTimePicker d: d.ValueChanged += (_, _) => Zmieniono(); break;
            }
            if (c is not NumericUpDown) SledzZmiany(c);
        }
    }

    /// <summary>Zmiany wprowadzane przez program (np. wynik testu, nowe konto SQL) — tez wymagaja zapisu.</summary>
    public void Zmieniono()
    {
        _zmiany = true;
        Odswiez();
    }

    /// <summary>
    /// Przycisk aktywny = jest zmiana i sekcja przejdzie walidacje. Gdy walidacja nie przechodzi,
    /// obok stoi powod — inaczej szary przycisk nie mowilby, czego brakuje.
    /// </summary>
    void Odswiez()
    {
        var bledy = new List<string>();
        bool mozna = _zmiany && _przygotuj(bledy) is not null && bledy.Count == 0;
        _zapisz.Enabled = mozna;
        Pokaz(_zmiany && bledy.Count > 0 ? string.Join(" · ", bledy) : "", Styl.Szary);
    }

    /// <summary>Zapis bez przycisku — dla sekcji z autozapisem.</summary>
    public void ZapiszTeraz() => Zapisz();

    void Zapisz()
    {
        var bledy = new List<string>();
        var zmiana = _przygotuj(bledy);
        if (bledy.Count > 0 || zmiana is null)
        {
            Pokaz(bledy.Count > 0 ? string.Join(" · ", bledy) : "Nie ma czego zapisać", Styl.Koral);
            _zapisz.Enabled = false;
            return;
        }
        try
        {
            // Bez uprawnien administratora zapis przejmuje okno podniesione przez UAC — dopiero teraz, po zatwierdzeniu.
            var (wynik, opis, kompletna) = _k.ZapiszLubPodnies(zmiana, (FindForm() as OknoGlowne)?.BiezacyEkran ?? "");
            switch (wynik)
            {
                case Kontekst.WynikZapisu.Zapisano:
                    _zmiany = false;
                    _zapisz.Enabled = false;
                    Pokaz(opis, kompletna ? Styl.ZielonyCiemny : Styl.Granat);
                    break;
                case Kontekst.WynikZapisu.Podniesiono:
                    Pokaz(opis, Styl.Granat);
                    (FindForm() as OknoGlowne)?.PrzekazanoPodniesionemu();
                    break;
                default:
                    Pokaz(opis, Styl.Koral);   // odmowa UAC — formularz zostaje nietkniety
                    break;
            }
        }
        catch (UnauthorizedAccessException)
        {
            Pokaz("Brak uprawnień do zapisu w " + _k.Katalog, Styl.Koral);
        }
        catch (Exception e) { Pokaz("Nie zapisano: " + e.Message, Styl.Koral); }
    }

    void Pokaz(string t, Color kolor)
    {
        _stan.ForeColor = kolor;
        _stan.MaximumSize = new Size(Math.Max(200, Width - _stan.Left - Uklad.Margines), 0);
        _stan.Text = t;
        if (_auto || _wRzedzie) _stan.Visible = t != "";
    }
}

