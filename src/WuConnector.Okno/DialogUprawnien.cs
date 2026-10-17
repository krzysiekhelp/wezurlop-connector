using WuConnector.Core.Konfiguracja;

namespace WuConnector.Okno;

/// <summary>
/// Okno modalne nad „Zarządzaniem kontami MSSQL”: do ktorych baz R2Platnika konto ma miec odczyt.
/// Zaznacza sie baze firmowa — wspolna (_bin) idzie z nia automatycznie, bo connector czyta z niej
/// firmy, wersje i funkcje slownikowa fn_Kod.
/// </summary>
public sealed class DialogUprawnien : Form
{
    readonly Panel _bazy = new() { AutoScroll = true, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
    readonly List<CheckBox> _pola = new();
    bool _wZmianie;   // zapora przed odbijaniem sie zdarzen miedzy baza firmowa a wspolna
    readonly Przycisk _zapisz = new() { Text = "Zapisz uprawnienia", Width = 220 };
    readonly Przycisk _anuluj = new() { Text = "Anuluj", Rodzaj = RodzajPrzycisku.Drugi, Width = 130 };
    readonly Komunikat _ostrzezenie = new() { Visible = false };

    /// <summary>Bazy zaznaczone w chwili zamkniecia — razem ze wspolnymi, ktore poszly za firmowymi.</summary>
    public List<string> Zaznaczone => _pola.Where(p => p.Checked).Select(p => ((KontaSql.BazaR2)p.Tag!).Nazwa).ToList();

    DialogUprawnien(KontaSql.Konto konto, IReadOnlyList<KontaSql.BazaR2> bazy, Func<string, string> nazwaInstalacji)
    {
        Text = $"Uprawnienia konta {konto.Nazwa}";
        Font = Styl.Czcionka(10f);
        BackColor = Styl.Tlo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = Styl.IkonaAplikacji();
        ClientSize = new Size(700, 560);

        var karta = new Karta { Location = new Point(Uklad.Margines, Uklad.Margines), Width = ClientSize.Width - 2 * Uklad.Margines };
        int top = Uklad.NaglowekKarty(karta, "Dostęp do baz R2Płatnika",
            "Zaznaczenie dowolnej bazy instalacji bierze obie — connector czyta dane z firmowej, a z bazy wspólnej "
            + "firmy, wersję i słowniki. Nadanie dostępu to rola db_datareader w bazie i prawo wykonania funkcji fn_Kod "
            + "w bazie wspólnej: żadnego zapisu, żadnych innych baz.");

        _bazy.Width = karta.Width - 2 * Uklad.Margines;
        _bazy.Height = 300;
        WypelnijBazy(bazy, konto, nazwaInstalacji);

        var b = new Blok(null, null, karta.Width);
        b.Wiersz(_bazy, Uklad.Srednia);
        b.Wiersz(_ostrzezenie, Uklad.Mala);
        b.Location = new Point(0, top);
        karta.Controls.Add(b.Domknij());
        karta.Height = top + b.Height + Uklad.Srednia;

        _zapisz.Location = new Point(ClientSize.Width - Uklad.Margines - _zapisz.Width, 0);
        _anuluj.Location = new Point(_zapisz.Left - _anuluj.Width - 12, 0);
        _zapisz.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _anuluj.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = _anuluj;

        Controls.AddRange(new Control[] { karta, _zapisz, _anuluj });
        ClientSize = new Size(ClientSize.Width, karta.Bottom + 78);
        _zapisz.Top = _anuluj.Top = ClientSize.Height - 58;
        Odswiez();
    }

    void WypelnijBazy(IReadOnlyList<KontaSql.BazaR2> bazy, KontaSql.Konto konto, Func<string, string> nazwaInstalacji)
    {
        int y = 8;
        foreach (var grupa in bazy.GroupBy(x => x.Prefiks).OrderBy(g => g.Key))
        {
            var naglowek = E.Opis(nazwaInstalacji(grupa.Key));
            naglowek.Location = new Point(10, y);
            _bazy.Controls.Add(naglowek);
            y += 20;

            var firmowe = new List<CheckBox>();
            CheckBox? wspolna = null;
            foreach (var baza in grupa.OrderByDescending(x => x.Wspolna).ThenBy(x => x.Nazwa))
            {
                var pole = new CheckBox
                {
                    Text = baza.Nazwa + (baza.Wspolna ? "   ·   wspólna: firmy, wersja, słowniki" : ""),
                    AutoSize = true, Location = new Point(22, y), BackColor = Color.White,
                    ForeColor = Styl.Tekst, Tag = baza,
                    Checked = konto.BazyZOdczytem.Contains(baza.Nazwa, StringComparer.OrdinalIgnoreCase),
                };
                pole.CheckedChanged += (_, _) => Odswiez();
                _bazy.Controls.Add(pole);
                _pola.Add(pole);
                if (baza.Wspolna && grupa.Count() > 1) wspolna = pole; else firmowe.Add(pole);
                y += 24;
            }

            // Zaznaczanie dziala w obie strony: firmowa pociaga wspolna, a wspolna — cala instalacje.
            // Connector i tak potrzebuje obu, wiec kazdy inny stan bylby tylko polowicznym nadaniem.
            if (wspolna is not null)
            {
                wspolna.CheckedChanged += (_, _) =>
                {
                    if (_wZmianie) return;
                    _wZmianie = true;
                    foreach (var p in firmowe) p.Checked = wspolna.Checked;
                    _wZmianie = false;
                };
                foreach (var p in firmowe)
                    p.CheckedChanged += (_, _) =>
                    {
                        if (_wZmianie) return;
                        _wZmianie = true;
                        wspolna.Checked = firmowe.Any(x => x.Checked);
                        _wZmianie = false;
                    };
            }
            y += 6;
        }
        if (_pola.Count == 0)
        {
            var pusto = E.Opis("Na tym serwerze nie ma baz R2Płatnika.");
            pusto.Location = new Point(10, 10);
            _bazy.Controls.Add(pusto);
        }
    }

    /// <summary>Puste zaznaczenie to tez decyzja — odebranie dostępu. Mówimy o tym, zanim ktoś kliknie zapis.</summary>
    void Odswiez()
    {
        int ile = _pola.Count(p => p.Checked);
        _ostrzezenie.Text = ile == 0
            ? "Nic nie zaznaczono — zapis odbierze kontu dostęp do wszystkich baz R2Płatnika."
            : $"Konto będzie czytać {ile} {(ile == 1 ? "bazę" : ile % 10 is >= 2 and <= 4 && ile % 100 is < 12 or > 14 ? "bazy" : "baz")}.";
        _ostrzezenie.Poziom = ile == 0 ? Poziom.Uwaga : Poziom.Info;
        Uklad.Pokaz(_ostrzezenie, true);
    }

    public static List<string>? Pokaz(IWin32Window rodzic, KontaSql.Konto konto,
                                      IReadOnlyList<KontaSql.BazaR2> bazy, Func<string, string> nazwaInstalacji)
    {
        using var okno = new DialogUprawnien(konto, bazy, nazwaInstalacji);
        return okno.ShowDialog(rodzic) == DialogResult.OK ? okno.Zaznaczone : null;
    }

    /// <summary>Do zrzutow dokumentacyjnych.</summary>
    public static DialogUprawnien DoZrzutu(KontaSql.Konto konto, IReadOnlyList<KontaSql.BazaR2> bazy, Func<string, string> nazwaInstalacji)
        => new(konto, bazy, nazwaInstalacji);
}
