namespace WuConnector.Okno;

/// <summary>
/// Sekret osobaId przy przenosinach na inna maszyne: pokazanie do przeniesienia i wgranie z poprzedniej.
/// Sekret jest szyfrowany kluczem maszyny, wiec sam plik konfiguracji na nowym komputerze nie wystarczy.
/// </summary>
public static class DialogSekretu
{
    public static void Eksport(IWin32Window? wlasciciel, string sekret, string odcisk)
    {
        using var f = Okno("Sekret tożsamości — do przeniesienia", 620, 340);
        var n = E.Naglowek("Przenieś sekret tożsamości na nową maszynę"); n.Location = new Point(24, 20);
        var o = E.Opis($"Odcisk: {odcisk}. Na nowej maszynie wklej poniższą wartość w „Importuj sekret tożsamości”\n" +
                       "i sprawdź, czy odcisk po imporcie jest taki sam.");
        o.Location = new Point(25, 54);
        var ostrz = new Znacznik
        {
            Text = "Nie wysyłaj tego mailem ani komunikatorem. Po przenosinach usuń z miejsca pośredniego.",
            Poziom = Poziom.Uwaga, Location = new Point(24, 100),
        };
        var pole = E.Pole(sekret);
        pole.SetBounds(24, 144, 560, 28);
        pole.ReadOnly = true;
        pole.Font = new Font("Consolas", 10f);

        var kopiuj = new Przycisk { Text = "Kopiuj do schowka", Width = 190, Location = new Point(24, 196) };
        var stan = E.Opis(""); stan.Location = new Point(230, 206);
        kopiuj.Click += (_, _) =>
        {
            Clipboard.SetText(sekret);
            stan.ForeColor = Styl.ZielonyCiemny;
            stan.Text = "Skopiowano — wklej na nowej maszynie i wyczyść schowek.";
        };
        var zamknij = new Przycisk { Text = "Zamknij", Rodzaj = RodzajPrzycisku.Drugi, Width = 130, Location = new Point(454, 260) };
        zamknij.Click += (_, _) => f.Close();
        f.CancelButton = zamknij;
        f.Controls.AddRange(new Control[] { n, o, ostrz, pole, kopiuj, stan, zamknij });
        f.ShowDialog(wlasciciel);
    }

    /// <summary>Zwraca sekret wpisany przez administratora albo null, gdy anulowano.</summary>
    public static string? Import(IWin32Window? wlasciciel, string odciskTeraz)
    {
        using var f = Okno("Importuj sekret tożsamości", 620, 300);
        var n = E.Naglowek("Sekret z poprzedniej maszyny"); n.Location = new Point(24, 20);
        var o = E.Opis($"Obecny odcisk: {odciskTeraz}. Po wgraniu zobaczysz odcisk przed i po —\n" +
                       "musi się zgadzać z tym z poprzedniej maszyny.");
        o.Location = new Point(25, 54);
        var ostrz = new Znacznik
        {
            Text = "Zły sekret = te same osoby przyjdą do wezurlop jako nowe i zdublują się.",
            Poziom = Poziom.Uwaga, Location = new Point(24, 100),
        };
        var pole = E.Pole("");
        pole.SetBounds(24, 144, 560, 28);
        pole.Font = new Font("Consolas", 10f);
        pole.PlaceholderText = "wartość z „Eksportuj sekret tożsamości” na poprzedniej maszynie";

        string? wynik = null;
        var ok = new Przycisk { Text = "Wgraj sekret", Width = 170, Location = new Point(264, 200) };
        var anuluj = new Przycisk { Text = "Anuluj", Rodzaj = RodzajPrzycisku.Drugi, Width = 130, Location = new Point(454, 200) };
        ok.Click += (_, _) => { wynik = pole.Text.Trim(); f.Close(); };
        anuluj.Click += (_, _) => f.Close();
        f.AcceptButton = ok;
        f.CancelButton = anuluj;
        f.Controls.AddRange(new Control[] { n, o, ostrz, pole, ok, anuluj });
        f.ShowDialog(wlasciciel);
        return string.IsNullOrEmpty(wynik) ? null : wynik;
    }

    static Form Okno(string tytul, int w, int h) => new()
    {
        Text = tytul, Font = Styl.Czcionka(10f), BackColor = Color.White,
        FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
        StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(w, h),
    };
}
