using System.Text.RegularExpressions;

namespace WuConnector.Okno;

/// <summary>Dziennik uslugi — ostatnie wpisy z filtrem poziomu.</summary>
public sealed class EkranDziennik : Ekran
{
    static readonly Regex Linia = new(@"^(\S+ \S+) (INFO|UWAG|BLAD|KRYT) (.*)$");

    readonly Kontekst _k;
    readonly ListView _lista = new()
    {
        View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None,
        Font = Styl.Czcionka(9.5f), ForeColor = Styl.Tekst, Dock = DockStyle.Fill,
    };
    readonly ListaWyboru _filtr = new() { Width = 260 };
    readonly Przycisk _odswiez = new() { Text = "Odśwież", Rodzaj = RodzajPrzycisku.Drugi, Width = 110 };
    readonly Label _gdzie = E.Opis("");

    public override string Tytul => "Dziennik";
    public override string Podtytul => "Co connector robił — bez treści danych kadrowych i bez haseł";

    public EkranDziennik(Kontekst k)
    {
        _k = k;
        _filtr.Items.AddRange(new object[] { "Wszystkie wpisy", "Ostrzeżenia i błędy", "Tylko błędy" });
        _filtr.SelectedIndex = 0;
        _filtr.SelectedIndexChanged += (_, _) => Wypelnij();
        _odswiez.Click += (_, _) => Wypelnij();

        // Pasek narzedzi w siatce: filtr i przycisk w jednym rzedzie, sciezka dziennika wyrownana do ich linii.
        var pasek = new Karta { Height = 80, Width = Uklad.SzerokoscTresci };
        _filtr.Location = new Point(Uklad.Margines, 21);
        _filtr.Height = Styl.WysokoscKontrolki;
        _odswiez.Location = new Point(Uklad.Margines + _filtr.Width + 12, 21);
        _odswiez.Height = Styl.WysokoscKontrolki;
        _gdzie.Location = new Point(_odswiez.Right + Uklad.Duza, 30);
        _gdzie.AutoSize = false; _gdzie.AutoEllipsis = true; _gdzie.Height = 20;
        pasek.Resize += (_, _) => _gdzie.Width = Math.Max(100, pasek.Width - _gdzie.Left - Uklad.Margines);
        pasek.Controls.AddRange(new Control[] { _filtr, _odswiez, _gdzie });
        Dodaj(pasek);

        _lista.Columns.Add("CZAS", 160);
        _lista.Columns.Add("POZIOM", 90);
        _lista.Columns.Add("ZDARZENIE", 800);
        var karta = new Karta { Height = 460, Padding = new Padding(Uklad.Margines, Uklad.Srednia, Uklad.Margines, Uklad.Srednia), Width = Uklad.SzerokoscTresci };
        karta.Controls.Add(_lista);
        _lista.Resize += (_, _) => _lista.Columns[2].Width =
            Math.Max(300, _lista.ClientSize.Width - _lista.Columns[0].Width - _lista.Columns[1].Width - SystemInformation.VerticalScrollBarWidth - 4);
        Dodaj(karta);
    }

    public override Task PokazAsync() { Wypelnij(); return Task.CompletedTask; }

    void Wypelnij()
    {
        _gdzie.Text = _k.KatalogDziennika;
        var linie = EkranStatus.CzytajDziennik(_k.KatalogDziennika, 1500);
        _lista.BeginUpdate();
        _lista.Items.Clear();
        foreach (var l in Enumerable.Reverse(linie))
        {
            var m = Linia.Match(l);
            if (!m.Success) continue;
            string poz = m.Groups[2].Value;
            if (_filtr.SelectedIndex == 1 && poz == "INFO") continue;
            if (_filtr.SelectedIndex == 2 && poz is "INFO" or "UWAG") continue;
            var it = new ListViewItem(new[] { m.Groups[1].Value[..19], poz switch { "UWAG" => "uwaga", "BLAD" or "KRYT" => "błąd", _ => "info" }, m.Groups[3].Value });
            if (poz == "UWAG") it.BackColor = Styl.Zolty;
            if (poz is "BLAD" or "KRYT") it.BackColor = Styl.Rozowy;
            _lista.Items.Add(it);
        }
        // Trzecia kolumna dobiera sie do szerokosci listy — dziennik przewija sie tylko w pionie.
        _lista.Columns[2].Width = Math.Max(300, _lista.ClientSize.Width - _lista.Columns[0].Width - _lista.Columns[1].Width - SystemInformation.VerticalScrollBarWidth - 4);
        _lista.EndUpdate();
    }
}


