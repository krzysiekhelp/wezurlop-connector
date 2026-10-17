using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;
using WuConnector.Core.Stan;

namespace WuConnector.Okno;

/// <summary>Co poszloby do wezurlop w najblizszym cyklu — liczone na zywych danych, niczego nie wysyla.</summary>
public sealed class EkranPodglad : Ekran
{
    static readonly (string Zbior, string Nazwa)[] Nazwy =
    {
        (Zbiory.Pracownik, "Pracownicy"), (Zbiory.Zatrudnienie, "Zatrudnienia"), (Zbiory.Limit, "Limity urlopu"),
        (Zbiory.Nieobecnosc, "Nieobecności"), (Zbiory.Kalendarz, "Kalendarze"), (Zbiory.Slownik, "Słowniki"), (Zbiory.Firma, "Firma"),
    };

    readonly Kontekst _k;
    readonly Przycisk _odswiez = new() { Text = "Przygotuj podgląd", Width = 190 };
    readonly Label _info = E.Opis("Podgląd czyta bazę programu kadrowego i porównuje ją z tym, co wezurlop już potwierdził. Nic nie jest wysyłane.");
    readonly List<Control> _wyniki = new();
    bool _pokazano;

    public override string Tytul => "Podgląd wysyłki";
    public override string Podtytul => "Co connector wyśle do wezurlop w najbliższym cyklu";

    public EkranPodglad(Kontekst k)
    {
        _k = k;
        var karta = new Karta { Height = 96 };
        _odswiez.Location = new Point(24, 28);
        _info.Location = new Point(232, 40);
        _odswiez.Click += async (_, _) => await ZbudujAsync();
        karta.Controls.AddRange(new Control[] { _odswiez, _info });
        karta.Resize += (_, _) => _info.MaximumSize = new Size(Math.Max(200, karta.Width - 260), 0);
        _info.Location = new Point(232, 30);
        Dodaj(karta);
    }

    public override async Task PokazAsync()
    {
        if (!_pokazano) await ZbudujAsync();
    }

    async Task ZbudujAsync()
    {
        _pokazano = true;
        foreach (var c in _wyniki) Tresc.Controls.Remove(c);
        _wyniki.Clear();

        var konf = _k.Rozwinieta();
        if (konf is null)
        {
            _wyniki.Add(Dodaj(Komunikat("Brak konfiguracji — najpierw wskaż instalacje i zapisz konfigurację.", Poziom.Uwaga)));
            return;
        }

        _odswiez.Enabled = false;
        try
        {
            using var stan = new MagazynStanu(konf.SciezkaStanu);
            foreach (var inst in konf.Instalacje)
                foreach (var f in inst.Firmy.Where(f => f.Aktywna))
                {
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var d = await new CzytnikSql(new ZrodloFirmy(inst.SerwerSql, inst.BazaBin, f.BazaDane, f.FirmIde, inst.LoginSql, inst.HasloSql))
                                    .CzytajAsync(DateTime.Today.Year - 1, DateTime.Today.Year + 1);
                        var b = BudowniczyRekordow.Zbuduj(d, new UstawieniaBudowy(inst.UzytkownikR2Id, konf.SekretOsoby, DateTime.Today));
                        var zakres = new ZakresPorownania(b.GrupyWZakresie, b.PoczatekOkna);
                        var roznice = Nazwy.Select(n => (n.Nazwa, R: stan.Porownaj(d.Firma.Nip, n.Zbior, b.Zbiory[n.Zbior], false, zakres))).ToList();
                        _wyniki.Add(Dodaj(KartaFirmy(inst.Nazwa, d, b, roznice, sw.ElapsedMilliseconds)));
                    }
                    catch (Exception e)
                    {
                        _wyniki.Add(Dodaj(Komunikat($"{inst.Nazwa}, firma {f.FirmIde}: nie udało się odczytać bazy — {e.Message}", Poziom.Blad)));
                    }
                }
        }
        finally { _odswiez.Enabled = true; }
    }

    Karta KartaFirmy(string instalacja, SuroweDane d, WynikBudowy b, List<(string Nazwa, Roznice R)> roznice, long ms)
    {
        var k = new Karta();
        var n = E.Naglowek(d.Firma.Nazwa.ToUpperInvariant()); n.Location = new Point(24, 20);
        var nip = E.Opis($"NIP {EkranStatus.FormatNip(d.Firma.Nip)}   ·   instalacja {instalacja}   ·   odczyt {ms} ms"); nip.Location = new Point(25, 50);
        var zakres = new Znacznik
        {
            Text = $"{b.GrupyWZakresie.Count} z {d.Pracownicy.Count} kartotek · zatrudnieni na umowę o pracę",
            Poziom = Poziom.Info, Location = new Point(24, 80),
        };
        bool pierwsze = roznice.All(r => r.R.StanPotwierdzony == 0);
        k.Controls.AddRange(new Control[] { n, nip, zakres });
        if (pierwsze)
            k.Controls.Add(new Znacznik { Text = "pierwsze ładowanie — wyślemy wszystko", Poziom = Poziom.Uwaga, Location = new Point(zakres.Right + 8, 80) });

        // Tabela: zbior | w zakresie | nowe | zmienione | usuniete. Kolumny stale, liczby do prawej krawedzi kolumny.
        k.Width = Uklad.SzerokoscTresci;
        int y = 124;
        string[] kol = { "Dane", "W zakresie", "Nowe", "Zmienione", "Usunięte" };
        int[] x = { Uklad.Margines, 254, 364, 464, 574 };
        int[] szer = { 230, 100, 90, 100, 100 };
        for (int i = 0; i < kol.Length; i++)
            k.Controls.Add(new Label
            {
                Text = kol[i].ToUpperInvariant(), AutoSize = false, Size = new Size(szer[i], 16), Location = new Point(x[i], y),
                Font = Styl.Czcionka(Styl.NaglowekKolumny), ForeColor = Styl.Szary, BackColor = Color.Transparent,
                TextAlign = i == 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight,
            });
        y += 26;
        k.Controls.Add(Uklad.Linia(y - 4, Uklad.SzerokoscTresci));
        foreach (var (nazwa, r) in roznice)
        {
            int zakresN = r.Nowe + r.Zmienione + r.BezZmian;
            k.Controls.Add(Kom(nazwa, x[0], y, szer[0], false, true));
            k.Controls.Add(Kom(zakresN.ToString("N0"), x[1], y, szer[1], true));
            k.Controls.Add(Kom(r.Nowe.ToString("N0"), x[2], y, szer[2], true, r.Nowe > 0, r.Nowe > 0 ? Styl.ZielonyCiemny : Styl.Szary));
            k.Controls.Add(Kom(r.Zmienione.ToString("N0"), x[3], y, szer[3], true, r.Zmienione > 0, r.Zmienione > 0 ? Styl.Tekst : Styl.Szary));
            k.Controls.Add(Kom(r.UsunieciaWykryte.ToString("N0"), x[4], y, szer[4], true, r.UsunieciaWykryte > 0,
                               r.UsunieciaWykryte > 0 ? Styl.Koral : Styl.Szary));
            if (r.UsunieciaWstrzymane)
                k.Controls.Add(new Znacznik { Text = "wstrzymane bezpiecznikiem", Poziom = Poziom.Blad, Location = new Point(x[4] + szer[4] + 16, y - 3) });
            y += 30;
        }
        int razem = roznice.Sum(r => r.R.DoWyslania.Count + r.R.Usuniete.Count);
        var suma = E.Tekst(razem == 0 ? "Nic do wysłania — wezurlop ma aktualne dane." : $"Do wysłania w najbliższym cyklu: {razem:N0} pozycji", true);
        suma.ForeColor = razem == 0 ? Styl.ZielonyCiemny : Styl.Granat;
        suma.Location = new Point(24, y + 10);
        k.Controls.Add(suma);
        k.Height = y + 52;
        return k;
    }

    static Label Kom(string t, int x, int y, int szer, bool liczba, bool pogrubiony = false, Color? kolor = null)
    {
        var l = E.Tekst(t, pogrubiony);
        l.AutoSize = false;
        l.Size = new Size(szer, 20);
        l.Location = new Point(x, y);
        l.AutoEllipsis = true;
        if (liczba) { l.Font = Styl.Cyfry(Styl.Tresc, pogrubiony ? FontStyle.Bold : FontStyle.Regular); l.TextAlign = ContentAlignment.MiddleRight; }
        else l.TextAlign = ContentAlignment.MiddleLeft;
        if (kolor is Color c) l.ForeColor = c;
        return l;
    }

    static Karta Komunikat(string t, Poziom p)
    {
        var k = new Karta { Height = 70, KolorTla = p == Poziom.Blad ? Styl.Rozowy : Styl.Zolty, Ramka = false };
        var l = E.Tekst(t); l.Location = new Point(24, 24); l.MaximumSize = new Size(900, 0);
        k.Controls.Add(l);
        return k;
    }
}

