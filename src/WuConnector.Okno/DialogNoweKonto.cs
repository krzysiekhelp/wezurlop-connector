using WuConnector.Core.Konfiguracja;

namespace WuConnector.Okno;

/// <summary>
/// Male okno modalne nad „Zarządzaniem kontami MSSQL”: zalozenie konta albo zmiana jego nazwy.
/// Modalne, wiec dopoki nie zostanie zamkniete, okna pod spodem nie da sie uzyc — nazwa konta
/// to decyzja, ktora trzeba domknac, zanim cokolwiek pojdzie na serwer.
/// </summary>
public sealed class DialogNoweKonto : Form
{
    readonly TextBox _nazwa = E.Pole("");
    readonly TextBox _haslo = E.Pole("", true);
    readonly Pole _poleHasla;
    readonly Komunikat _blad = new() { Visible = false };
    readonly Przycisk _ok;
    readonly Przycisk _anuluj = new() { Text = "Anuluj", Rodzaj = RodzajPrzycisku.Drugi, Width = 130 };

    readonly IReadOnlyCollection<string> _zajete;
    readonly bool _noweKonto;

    public string Nazwa => _nazwa.Text.Trim();
    /// <summary>Okno zaklada wylacznie konta SQL — konto Windows musi juz istniec na serwerze.</summary>
    public bool Windows => false;

    /// <summary>Haslo podane przez uzytkownika albo null, gdy ma byc wygenerowane.</summary>
    public string? Haslo => Windows || _haslo.Text.Length == 0 ? null : _haslo.Text;

    DialogNoweKonto(bool noweKonto, string nazwaPoczatkowa, IReadOnlyCollection<string> zajete)
    {
        _noweKonto = noweKonto;
        _zajete = zajete;

        Text = noweKonto ? "Nowe konto SQL" : "Zmiana nazwy konta";
        Font = Styl.Czcionka(10f);
        BackColor = Styl.Tlo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = Styl.IkonaAplikacji();
        ClientSize = new Size(560, noweKonto ? 340 : 250);

        var karta = new Karta { Location = new Point(Uklad.Margines, Uklad.Margines), Width = ClientSize.Width - 2 * Uklad.Margines };
        int top = Uklad.NaglowekKarty(karta, noweKonto ? "Nowe konto na serwerze SQL" : "Nowa nazwa konta",
            noweKonto
                ? "Zakładamy konto SQL Servera — takim loguje się connector. "
                  + "Nazwa może być dowolna; wu_* podpowiadamy po to, żeby konta connectora łatwo rozpoznać. "
                  + "Hasło zostawione puste zostanie wygenerowane i pokazane po założeniu konta."
                : "Zmiana obejmie login na serwerze i użytkownika w bazach R2Płatnika. "
                  + "Jeśli konto jest wpisane w instalacji, login trzeba tam poprawić ręcznie.");

        _nazwa.Text = nazwaPoczatkowa;
        _haslo.PlaceholderText = "puste = wygenerowane";

        int szerokaKolumna = karta.Width - 2 * Uklad.Margines;
        var b = new Blok(null, null, karta.Width);
        b.PolaWiersz(new Pole("Nazwa konta", _nazwa, szerokaKolumna));
        _poleHasla = new Pole("Hasło", _haslo, szerokaKolumna);
        if (noweKonto) b.PolaWiersz(_poleHasla);
        b.Wiersz(_blad, Uklad.Mala);
        b.Location = new Point(0, top);
        karta.Controls.Add(b.Domknij());
        karta.Height = top + b.Height + Uklad.Srednia;
        Uklad.Pokaz(_blad, false);

        _ok = new Przycisk { Text = noweKonto ? "Utwórz konto" : "Zmień nazwę", Width = 180 };
        _ok.Location = new Point(ClientSize.Width - Uklad.Margines - _ok.Width, ClientSize.Height - 58);
        _anuluj.Location = new Point(_ok.Left - _anuluj.Width - 12, ClientSize.Height - 58);
        _ok.Click += (_, _) => { if (Sprawdz()) { DialogResult = DialogResult.OK; Close(); } };
        _anuluj.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = _ok;
        CancelButton = _anuluj;

        _nazwa.TextChanged += (_, _) => Sprawdz();

        Controls.AddRange(new Control[] { karta, _ok, _anuluj });
        ClientSize = new Size(ClientSize.Width, karta.Bottom + 78);
        _ok.Top = _anuluj.Top = ClientSize.Height - 58;
        Shown += (_, _) => { _nazwa.Focus(); _nazwa.SelectAll(); };
    }

    /// <summary>Sprawdza nazwe i pokazuje powod, gdy cos nie gra. Zwraca true, gdy mozna zatwierdzic.</summary>
    bool Sprawdz()
    {
        string n = Nazwa;
        string? powod =
            n.Length == 0 ? "Podaj nazwę konta."
            : !KontaSql.CzyPoprawnaNazwa(n, Windows)
                ? Windows
                    ? @"Konto Windows podaje się jako DOMENA\użytkownik — litery, cyfry i podkreślenie."
                    : "Nazwa może zawierać tylko litery, cyfry i podkreślenie (do 128 znaków)."
            : _zajete.Contains(n, StringComparer.OrdinalIgnoreCase) ? $"Konto {n} już istnieje na tym serwerze."
            : null;

        _blad.Text = powod ?? "";
        _blad.Poziom = Poziom.Uwaga;
        Uklad.Pokaz(_blad, powod is not null);
        _ok.Enabled = powod is null;
        return powod is null;
    }

    /// <summary>Do zrzutow dokumentacyjnych — okno budowane bez pokazywania go modalnie.</summary>
    public static DialogNoweKonto DoZrzutu(string proponowana) => new(true, proponowana, Array.Empty<string>());

    public static DialogNoweKonto? Nowe(IWin32Window rodzic, string proponowana, IReadOnlyCollection<string> zajete)
        => Pokaz(new DialogNoweKonto(true, proponowana, zajete), rodzic);

    public static DialogNoweKonto? ZmianaNazwy(IWin32Window rodzic, string stara, IReadOnlyCollection<string> zajete)
        => Pokaz(new DialogNoweKonto(false, stara, zajete.Where(z => !z.Equals(stara, StringComparison.OrdinalIgnoreCase)).ToList()), rodzic);

    static DialogNoweKonto? Pokaz(DialogNoweKonto okno, IWin32Window rodzic)
    {
        using (okno)
            return okno.ShowDialog(rodzic) == DialogResult.OK ? okno : null;
    }
}
