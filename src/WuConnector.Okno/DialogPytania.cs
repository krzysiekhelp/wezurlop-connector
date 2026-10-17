namespace WuConnector.Okno;

/// <summary>
/// Male okno modalne z pytaniem albo potwierdzeniem — zamiast systemowego MessageBox, ktory odstaje
/// od reszty okna (inny kroj, inne przyciski, inne kolory). Uzywamy go wszedzie tam, gdzie operacja
/// jest nieodwracalna albo unieważnia cos, co dziala.
/// </summary>
public sealed class DialogPytania : Form
{
    DialogPytania(string tytul, string tresc, string? szczegol, string tekstTak, string? tekstNie, bool grozny)
    {
        Text = tytul;
        Font = Styl.Czcionka(10f);
        BackColor = Styl.Tlo;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = Styl.IkonaAplikacji();
        ClientSize = new Size(560, 240);

        var karta = new Karta { Location = new Point(Uklad.Margines, Uklad.Margines), Width = ClientSize.Width - 2 * Uklad.Margines };
        int top = Uklad.NaglowekKarty(karta, tytul, tresc);

        var b = new Blok(null, null, karta.Width, zLinia: szczegol is not null);
        if (szczegol is not null)
            b.Wiersz(new Komunikat { Text = szczegol, Poziom = grozny ? Poziom.Blad : Poziom.Uwaga }, Uklad.Mala);
        b.Location = new Point(0, top);
        karta.Controls.Add(b.Domknij());
        karta.Height = top + (szczegol is null ? 0 : b.Height) + Uklad.Srednia;

        var tak = new Przycisk { Text = tekstTak, Width = Math.Max(150, TextRenderer.MeasureText(tekstTak, Styl.Czcionka(Styl.Tresc, FontStyle.Bold)).Width + 44) };
        tak.Rodzaj = grozny ? RodzajPrzycisku.Drugi : RodzajPrzycisku.Glowny;
        if (grozny) tak.ForeColor = Styl.Koral;
        tak.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        Controls.Add(karta);
        Controls.Add(tak);
        if (tekstNie is not null)
        {
            var nie = new Przycisk { Text = tekstNie, Rodzaj = RodzajPrzycisku.Drugi, Width = 130 };
            nie.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(nie);
            CancelButton = nie;
            ClientSize = new Size(ClientSize.Width, karta.Bottom + 78);
            tak.Location = new Point(ClientSize.Width - Uklad.Margines - tak.Width, ClientSize.Height - 58);
            nie.Location = new Point(tak.Left - nie.Width - 12, ClientSize.Height - 58);
            nie.Select();   // domyslnie „nie” — przy nieodwracalnych rzeczach Enter nie ma nic psuc
        }
        else
        {
            AcceptButton = tak;
            CancelButton = tak;
            ClientSize = new Size(ClientSize.Width, karta.Bottom + 78);
            tak.Location = new Point(ClientSize.Width - Uklad.Margines - tak.Width, ClientSize.Height - 58);
        }
    }

    /// <summary>Pytanie o zgodę. `grozny` = operacja nieodwracalna; wtedy przycisk jest czerwony, a domyślne „nie”.</summary>
    public static bool Pytanie(IWin32Window rodzic, string tytul, string tresc, string? szczegol, string tekstTak, bool grozny = false)
    {
        using var okno = new DialogPytania(tytul, tresc, szczegol, tekstTak, "Anuluj", grozny);
        return okno.ShowDialog(rodzic) == DialogResult.OK;
    }

    /// <summary>Komunikat do przeczytania i zamknięcia — po nim wołający odświeża widok.</summary>
    public static void Informacja(IWin32Window rodzic, string tytul, string tresc, string? szczegol = null)
    {
        using var okno = new DialogPytania(tytul, tresc, szczegol, "OK", null, false);
        okno.ShowDialog(rodzic);
    }

    /// <summary>Do zrzutow dokumentacyjnych.</summary>
    public static DialogPytania DoZrzutu(string tytul, string tresc, string? szczegol, string tekstTak, bool grozny)
        => new(tytul, tresc, szczegol, tekstTak, "Anuluj", grozny);
}
