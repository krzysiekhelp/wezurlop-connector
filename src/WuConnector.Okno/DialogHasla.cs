namespace WuConnector.Okno;

/// <summary>
/// Okno modalne z nowym haslem konta SQL. Haslo pokazujemy raz — serwer trzyma tylko jego skrot,
/// wiec po zamknieciu tego okna nikt go juz nie odczyta. Stad przycisk „Kopiuj” i wyrazne ostrzezenie.
/// </summary>
public sealed class DialogHasla : Form
{
    DialogHasla(string login, string haslo, string gdzieWpisac)
    {
        Text = "Hasło konta";
        Font = Styl.Czcionka(10f);
        BackColor = Styl.Tlo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = Styl.IkonaAplikacji();
        ClientSize = new Size(600, 320);

        var karta = new Karta { Location = new Point(Uklad.Margines, Uklad.Margines), Width = ClientSize.Width - 2 * Uklad.Margines };
        int top = Uklad.NaglowekKarty(karta, $"Hasło konta {login}",
            "Zapisz je teraz — po zamknięciu tego okna nie da się go odczytać, bo serwer trzyma tylko skrót hasła.");

        int szerokosc = karta.Width - 2 * Uklad.Margines;
        var pole = new TextBox
        {
            Text = haslo, ReadOnly = true, Font = new Font("Consolas", 12f),
            BorderStyle = BorderStyle.FixedSingle, Width = szerokosc - 130, Height = Styl.WysokoscKontrolki,
            TextAlign = HorizontalAlignment.Center,
        };
        var kopiuj = new Przycisk { Text = "Kopiuj", Rodzaj = RodzajPrzycisku.Drugi, Width = 118 };
        var potwierdzenie = E.Opis("");

        var rzad = new Panel { BackColor = Color.Transparent, Width = szerokosc, Height = Styl.WysokoscKontrolki };
        pole.Location = new Point(0, 0);
        kopiuj.Location = new Point(szerokosc - kopiuj.Width, 0);
        rzad.Controls.AddRange(new Control[] { pole, kopiuj });
        kopiuj.Click += (_, _) =>
        {
            Clipboard.SetText(haslo);
            potwierdzenie.Text = "Skopiowano do schowka.";
            potwierdzenie.ForeColor = Styl.ZielonyCiemny;
        };

        var b = new Blok(null, null, karta.Width);
        b.Wiersz(rzad, Uklad.Mala);
        b.Wiersz(potwierdzenie, Uklad.Mala);
        b.Wiersz(new Komunikat { Text = gdzieWpisac, Poziom = Poziom.Uwaga }, Uklad.Mala);
        b.Location = new Point(0, top);
        karta.Controls.Add(b.Domknij());
        karta.Height = top + b.Height + Uklad.Srednia;

        var ok = new Przycisk { Text = "Zapisałem hasło", Width = 200 };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        AcceptButton = ok;
        CancelButton = ok;

        Controls.AddRange(new Control[] { karta, ok });
        ClientSize = new Size(ClientSize.Width, karta.Bottom + 78);
        ok.Location = new Point(ClientSize.Width - Uklad.Margines - ok.Width, ClientSize.Height - 58);
        Shown += (_, _) => { pole.Focus(); pole.SelectAll(); };
    }

    public static void Pokaz(IWin32Window rodzic, string login, string haslo, string gdzieWpisac)
    {
        using var okno = new DialogHasla(login, haslo, gdzieWpisac);
        okno.ShowDialog(rodzic);
    }

    /// <summary>Do zrzutow dokumentacyjnych.</summary>
    public static DialogHasla DoZrzutu(string login, string haslo, string gdzieWpisac) => new(login, haslo, gdzieWpisac);
}
