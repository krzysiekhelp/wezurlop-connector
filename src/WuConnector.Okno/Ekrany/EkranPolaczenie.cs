using System.Text.RegularExpressions;
using WuConnector.Core.Konfiguracja;
using WuConnector.Core.Wymiana;

namespace WuConnector.Okno;

/// <summary>Adres aplikacji wezurlop klienta i klucz instalacji connectora — z testem polaczenia.</summary>
public sealed class EkranPolaczenie : Ekran
{
    static readonly Regex WzorKlucza = new("^[0-9a-fA-F]{64}$");

    readonly Kontekst _k;
    readonly TextBox _adres, _klucz;
    readonly CheckBox _pokaz = new() { Text = "pokaż", AutoSize = true, BackColor = Color.White, ForeColor = Styl.Szary, Tag = Ekran.BezZapisu };
    readonly Przycisk _testuj = new() { Text = "Testuj połączenie", Width = 190 };
    readonly Komunikat _wynik = new() { Visible = false };
    readonly Label _szczegol = E.Opis("");
    readonly NumericUpDown _odstep = Liczba(), _poza = Liczba();
    readonly DateTimePicker _od = Godzina(), _do = Godzina();
    readonly CheckBox[] _dni = new CheckBox[7];
    readonly RadioButton _pozaCo = new() { Text = "co", AutoSize = true, BackColor = Color.White, ForeColor = Styl.Tekst };
    readonly RadioButton _wstrzymaj = new() { Text = "wstrzymaj synchronizację", AutoSize = true, BackColor = Color.White, ForeColor = Styl.Tekst };
    readonly Label _podsumowanie = E.Tekst("", true);
    readonly Label _odcisk = E.Tekst("—", true);
    readonly Przycisk _kopiujOdcisk = new() { Text = "Kopiuj", Rodzaj = RodzajPrzycisku.Drugi, Width = 110 };
    readonly Przycisk _eksportSekretu = new() { Text = "Eksportuj sekret tożsamości…", Rodzaj = RodzajPrzycisku.Drugi, Width = 240 };
    readonly Przycisk _importSekretu = new() { Text = "Importuj sekret tożsamości…", Rodzaj = RodzajPrzycisku.Drugi, Width = 240 };
    readonly Label _stanSekretu = E.Opis("");

    static NumericUpDown Liczba() => new()
    { Minimum = Harmonogram.MinimalnyOdstepMinut, Maximum = 1440, Increment = 5, Width = 86, Font = Styl.Czcionka(11f), TextAlign = HorizontalAlignment.Right };

    static DateTimePicker Godzina() => new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 86, Font = Styl.Czcionka(11f) };

    static TimeSpan Godz(string s, int dom) => TimeSpan.TryParse(s, out var t) ? t : TimeSpan.FromHours(dom);

    Harmonogram ZbierzHarmonogram() => new()
    {
        OdstepMinut = (int)_odstep.Value,
        GodzinaOd = _od.Value.ToString("HH:mm"),
        GodzinaDo = _do.Value.ToString("HH:mm"),
        Dni = Enumerable.Range(0, 7).Where(i => _dni[i]?.Checked == true).Select(i => i + 1).ToList(),
        OdstepPozaMinut = (int)_poza.Value,
        WstrzymajPoza = _wstrzymaj.Checked,
    };

    void Podsumuj()
    {
        if (_dni.Any(d => d is null)) return;
        var h = ZbierzHarmonogram();
        var start = h.WOknie(DateTime.Now) ? DateTime.Now + h.Odstep(DateTime.Now) : h.NastepnyStartOkna(DateTime.Now);
        _podsumowanie.Text = h.Opis() + (start is DateTime s ? $"   ·   najbliższe okno pracy / cykl: {s:ddd HH:mm}" : "");
        _podsumowanie.ForeColor = h.Sprawdz() is null ? Styl.Granat : Styl.Koral;
    }

    public override string Tytul => "Połączenie";
    public override string Podtytul => "Aplikacja wezurlop, z którą connector wymienia dane";

    public EkranPolaczenie(Kontekst k)
    {
        _k = k;
        Uklad.Pokaz(_wynik, false);
        _adres = E.Pole(k.Plik?.AdresSerwera ?? "");
        _klucz = E.Pole(Jawny(k.Plik?.KluczApi), true);
        int szer = Uklad.SzerokoscTresci;
        int szerokaKolumna = 2 * Uklad.Kolumna + Uklad.Odstep;

        // ---------- adres i klucz
        var karta = new Karta { Width = szer };
        int y = Uklad.NaglowekKarty(karta, "Połączenie z wezurlop",
                                    "Connector sam łączy się z aplikacją — nie potrzebuje otwartych portów ani publicznego adresu.");

        var b1 = new Blok(null, null, szer);
        _adres.PlaceholderText = "https://";
        _klucz.Font = new Font("Consolas", 10f);
        b1.PolaWiersz(new Pole("Adres aplikacji wezurlop", _adres, szerokaKolumna));
        b1.Wiersz(E.Opis("Domena, pod którą działa aplikacja wniosków urlopowych tej firmy, np. https://urlopy.firma.pl"), Uklad.Srednia);
        b1.PolaWiersz(new Pole("Klucz instalacji", _klucz, szerokaKolumna));
        b1.Wiersz(_pokaz, Uklad.Mala);
        b1.Wiersz(E.Opis("64 znaki szesnastkowe. Wydaje go administrator aplikacji wezurlop; zapisywany zaszyfrowany kluczem tej maszyny."), Uklad.Srednia);
        b1.Przyciski(_testuj);
        b1.Wiersz(_wynik, Uklad.Mala);
        b1.Wiersz(_szczegol, 0);
        b1.Location = new Point(0, y);
        karta.Controls.Add(b1.Domknij());
        y += b1.Height;

        var zapiszPolaczenie = new PasekZapisu(k, "Zapisz połączenie", PrzygotujPolaczenie)
        { Location = new Point(0, y), Width = szer, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        karta.Controls.Add(zapiszPolaczenie);
        karta.Height = y + zapiszPolaczenie.Height;
        _pokaz.CheckedChanged += (_, _) => _klucz.UseSystemPasswordChar = !_pokaz.Checked;
        _testuj.Click += async (_, _) => await TestujAsync();
        zapiszPolaczenie.SledzZmiany(karta);
        Dodaj(karta);

        // ---------- tozsamosc osob (sekret i odcisk)
        var ks = new Karta { Width = szer };
        int ys = Uklad.NaglowekKarty(ks, "Tożsamość osób w wezurlop",
            "Identyfikatory osób connector liczy z sekretu tej instalacji — sekret nigdy nie opuszcza tej maszyny. "
            + "Przy przenosinach na inny komputer trzeba go przenieść, inaczej te same osoby założą się w wezurlop po raz drugi.");

        var bs = new Blok(null, null, szer);
        var wierszOdcisku = new Panel { BackColor = Color.Transparent, Width = szerokaKolumna, Height = Styl.WysokoscKontrolki };
        var lo = E.Opis("Odcisk tożsamości");
        lo.Location = new Point(0, 0);
        _odcisk.Font = new Font("Consolas", 11f);
        _odcisk.Location = new Point(0, 18);
        _kopiujOdcisk.Height = Styl.WysokoscMniejszej;
        _kopiujOdcisk.Location = new Point(140, 11);
        wierszOdcisku.Height = 50;
        wierszOdcisku.Controls.AddRange(new Control[] { lo, _odcisk, _kopiujOdcisk });
        bs.Wiersz(wierszOdcisku, Uklad.Srednia);
        bs.Przyciski(_eksportSekretu, _importSekretu);
        bs.Wiersz(_stanSekretu, Uklad.Mala);
        bs.Location = new Point(0, ys);
        ks.Controls.Add(bs.Domknij());
        ks.Height = ys + bs.Height + Uklad.Mala;

        _kopiujOdcisk.Click += (_, _) => { Clipboard.SetText(_odcisk.Text); _stanSekretu.ForeColor = Styl.Szary; _stanSekretu.Text = "Odcisk skopiowany."; };
        _eksportSekretu.Click += (_, _) =>
        {
            try { DialogSekretu.Eksport(FindForm(), k.SekretJawny(), k.OdciskSekretu() ?? "—"); }
            catch (Exception e) { PokazSekret("Nie udało się odczytać sekretu: " + e.Message, Styl.Koral); }
        };
        _importSekretu.Click += (_, _) =>
        {
            if (DialogSekretu.Import(FindForm(), k.OdciskSekretu() ?? "nieczytelny") is not string s) return;
            try
            {
                var (_, wynik, opis) = k.ImportujSekret(s);
                if (wynik == Kontekst.WynikZapisu.Zapisano) OdswiezSekret();
                PokazSekret(opis + (wynik == Kontekst.WynikZapisu.Zapisano ? " Sprawdź, czy zgadza się z poprzednią maszyną." : ""),
                            wynik == Kontekst.WynikZapisu.Odmowa ? Styl.Koral : Styl.Granat);
                if (wynik == Kontekst.WynikZapisu.Podniesiono) (FindForm() as OknoGlowne)?.PrzekazanoPodniesionemu();
            }
            catch (Exception e) { PokazSekret("Nie wgrano: " + e.Message, Styl.Koral); }
        };
        Dodaj(ks);
        OdswiezSekret();

        // ---------- harmonogram
        var h = k.Plik?.Harmonogram ?? Harmonogram.Domyslny();
        var cz = new Karta { Width = szer };
        int yh = Uklad.NaglowekKarty(cz, "Harmonogram synchronizacji",
            "Jedna synchronizacja: odczyt programu kadrowego, wysyłka zmian i zapis zaakceptowanych urlopów.");

        _odstep.Value = Math.Clamp(h.OdstepMinut, Harmonogram.MinimalnyOdstepMinut, 1440);
        _poza.Value = Math.Clamp(h.OdstepPozaMinut, Harmonogram.MinimalnyOdstepMinut, 1440);
        _od.Value = DateTime.Today + Godz(h.GodzinaOd, 6);
        _do.Value = DateTime.Today + Godz(h.GodzinaDo, 20);
        foreach (var kontrolka in new Control[] { _odstep, _poza, _od, _do })
        {
            kontrolka.Height = Styl.WysokoscKontrolki;
            kontrolka.Width = 110;
        }

        var bg = new Blok("W godzinach pracy", null, szer);
        var godziny = new Panel { BackColor = Color.Transparent, Width = Uklad.Kolumna, Height = Styl.WysokoscKontrolki };
        _od.Location = new Point(0, 0); _od.Width = 110;
        var doTekst = E.Tekst("do"); doTekst.Location = new Point(122, 9);
        _do.Location = new Point(150, 0); _do.Width = 110;
        godziny.Controls.AddRange(new Control[] { _od, doTekst, _do });
        bg.PolaWiersz(new Pole("Synchronizuj co (minut)", _odstep, Uklad.Kolumna), new Pole("Godziny pracy", godziny, Uklad.Kolumna));

        var dni = new Panel { BackColor = Color.Transparent, Width = szerokaKolumna, Height = 44 };
        var ldni = E.Opis("Dni pracy"); ldni.Location = new Point(0, 0);
        dni.Controls.Add(ldni);
        string[] nazwy = { "Pn", "Wt", "Śr", "Cz", "Pt", "So", "Nd" };
        for (int i = 0; i < 7; i++)
        {
            _dni[i] = new CheckBox
            {
                Text = nazwy[i], AutoSize = true, Checked = h.Dni.Contains(i + 1),
                BackColor = Color.White, ForeColor = Styl.Tekst, Location = new Point(i * 62, 22),
            };
            _dni[i].CheckedChanged += (_, _) => Podsumuj();
            dni.Controls.Add(_dni[i]);
        }
        bg.Wiersz(dni, Uklad.Mala);
        bg.Location = new Point(0, yh);
        cz.Controls.Add(bg.Domknij());
        yh += bg.Height;

        var bp = new Blok("Poza godzinami pracy", null, szer);
        var pozaRzad = new Panel { BackColor = Color.Transparent, Width = szerokaKolumna, Height = Styl.WysokoscKontrolki };
        _pozaCo.Location = new Point(0, 10); _pozaCo.Checked = !h.WstrzymajPoza;
        _poza.Location = new Point(48, 0); _poza.Width = 110;
        var minut = E.Tekst("minut"); minut.Location = new Point(168, 9);
        _wstrzymaj.Location = new Point(240, 10); _wstrzymaj.Checked = h.WstrzymajPoza;
        pozaRzad.Controls.AddRange(new Control[] { _pozaCo, _poza, minut, _wstrzymaj });
        bp.Wiersz(pozaRzad, Uklad.Srednia);
        bp.Wiersz(_podsumowanie, Uklad.Mala);
        bp.Wiersz(E.Opis("Serwer wezurlop może odstęp wydłużyć (np. prosząc o przerwę) — nigdy nie skróci go poniżej harmonogramu."), Uklad.Mala);
        bp.Location = new Point(0, yh);
        cz.Controls.Add(bp.Domknij());
        yh += bp.Height;

        var zapiszHarmonogram = new PasekZapisu(k, "Zapisz harmonogram", PrzygotujHarmonogram)
        { Location = new Point(0, yh), Width = szer, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        cz.Controls.Add(zapiszHarmonogram);
        cz.Height = yh + zapiszHarmonogram.Height;

        foreach (var n in new[] { _odstep, _poza }) n.ValueChanged += (_, _) => Podsumuj();
        foreach (var d in new[] { _od, _do }) d.ValueChanged += (_, _) => Podsumuj();
        foreach (var r in new[] { _pozaCo, _wstrzymaj }) r.CheckedChanged += (_, _) => { _poza.Enabled = _pozaCo.Checked; Podsumuj(); };
        _poza.Enabled = _pozaCo.Checked;
        zapiszHarmonogram.SledzZmiany(cz);
        Dodaj(cz);
        Podsumuj();

        // ---------- gdzie leza ustawienia
        var info = new Karta { Width = szer, KolorTla = Styl.Niebieski, Ramka = false };
        var bi = new Blok(null, null, szer, zLinia: false);
        bi.Wiersz(E.Tekst("Plik konfiguracji: " + k.SciezkaKonfiguracji), Uklad.Mala);
        bi.Location = new Point(0, Uklad.Srednia);
        info.Controls.Add(bi.Domknij());
        info.Height = Uklad.Srednia + bi.Height + Uklad.Srednia;
        Dodaj(info);
    }

    void PokazSekret(string t, Color kolor) { _stanSekretu.ForeColor = kolor; _stanSekretu.Text = t; }

    /// <summary>Odcisk sekretu widoczny na stale; konfiguracja z innej maszyny — komunikat i podpowiedz importu.</summary>
    void OdswiezSekret()
    {
        if (_k.SekretZInnejMaszyny())
        {
            _odcisk.Text = "nieczytelny";
            _odcisk.ForeColor = Styl.Koral;
            _eksportSekretu.Enabled = false;
            PokazSekret("Ta konfiguracja pochodzi z innej maszyny — sekretu nie da się tu odczytać. Wgraj go przyciskiem obok "
                        + "(„Eksportuj sekret tożsamości” na poprzednim komputerze). Bez tego usługa nie ruszy, a nowy sekret zdublowałby osoby.", Styl.Koral);
            return;
        }
        _odcisk.Text = _k.OdciskSekretu() ?? "—";
        _odcisk.ForeColor = Styl.Granat;
        _eksportSekretu.Enabled = _k.Plik is not null;
        if (_k.Plik is null) PokazSekret("Sekret powstanie przy pierwszym zapisie konfiguracji.", Styl.Szary);
    }

    static string Jawny(string? s) { try { return string.IsNullOrEmpty(s) ? "" : PlikKonfiguracji.Jawny(s); } catch { return ""; } }

    /// <summary>
    /// Test polaczenia na operacji `stan` (protokol z 20.09.2026): jedno wywolanie, ktore nie otwiera przebiegu
    /// i nie zostawia pustych cykli w statystyce serwera. Bez wykrytej firmy sprawdzamy sam klucz.
    /// </summary>
    async Task TestujAsync()
    {
        if (!Uri.TryCreate(_adres.Text.Trim(), UriKind.Absolute, out var adres)) { Pokaz("Nieprawidłowy adres", Poziom.Uwaga, ""); return; }
        string? nip = Nipy().FirstOrDefault();

        _testuj.Enabled = false;
        Pokaz("Łączę…", Poziom.Info, "");
        try
        {
            using var klient = new KlientTenanta(adres, _klucz.Text.Trim(), "okno-konfiguracji", limitCzasu: TimeSpan.FromSeconds(20));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stan = await klient.StanAsync(nip);
            string ms = $"{sw.ElapsedMilliseconds} ms";

            if (nip is null)
            {
                Pokaz($"klucz działa · {ms}", Poziom.Ok,
                      "Nie wykryto jeszcze firmy — po wykryciu instalacji test sprawdzi też, czy firma jest zatwierdzona.");
                return;
            }

            string firma = $"Firma NIP {EkranStatus.FormatNip(nip)}";
            var (tekst, poziom, szczegol) = stan.Firma?.Stan switch
            {
                "ok" => ($"połączenie działa · {ms}", Poziom.Ok,
                         $"{firma} zatwierdzona w wezurlop." + (stan.OstatniPrzebieg is DateTimeOffset t
                            ? $" Ostatni przebieg: {Styl.DataGodzina(t)}." : " Przebiegów jeszcze nie było.")),
                "firma_zgloszona" => ($"klucz działa, firma czeka na zatwierdzenie · {ms}", Poziom.Uwaga,
                         $"{firma} została zgłoszona — administrator wezurlop musi ją zatwierdzić. Synchronizacja ruszy sama po zatwierdzeniu."),
                "firma_nieznana" => ($"klucz działa, firma nieznana · {ms}", Poziom.Uwaga,
                         $"{firma} nie jest jeszcze zgłoszona. Connector zgłosi ją przy najbliższym cyklu; zatwierdza administrator wezurlop."),
                "firma_poza_zakresem" => ($"klucz działa, firma poza zakresem klucza · {ms}", Poziom.Blad,
                         $"{firma} nie należy do tego klucza — sprawdź, czy klucz jest wydany dla tej firmy."),
                _ => ($"połączenie działa · {ms}", Poziom.Ok, $"Serwer nie podał stanu firmy (protokół {stan.Protokol})."),
            };
            Pokaz(tekst, poziom, szczegol);
        }
        catch (BladProtokolu e)
        {
            var (t, p) = e.Status switch
            {
                0 => ("Brak połączenia z serwerem", Poziom.Blad),
                // Connector wysyła NIP wyłącznie nagłówkiem X-Nip. Jeśli serwer widzi do tego parametr
                // ?nip= z inną wartością, dokłada go coś po drodze — to błąd konfiguracji, nie usterka łącza.
                400 when e.Kod == "NIP_NIESPOJNY" =>
                    ("Serwer dostał dwa różne NIP-y w jednym żądaniu — sprawdź pośrednika (proxy) na drodze do serwera", Poziom.Blad),
                401 => ("Klucz nieprawidłowy albo wygasł", Poziom.Blad),
                403 => ("Firma nie jest zatwierdzona w wezurlop", Poziom.Uwaga),
                404 => ("Serwer nie zna operacji „stan” — zaktualizuj aplikację wezurlop", Poziom.Uwaga),
                409 => ("Niezgodna wersja protokołu — zaktualizuj connector", Poziom.Blad),
                422 => ("NIP firmy jest niepoprawny — popraw go w programie kadrowym", Poziom.Blad),
                429 or 503 => ("Serwer prosi o chwilę przerwy", Poziom.Uwaga),
                _ => ($"Serwer odpowiedział błędem {e.Status}", Poziom.Blad),
            };
            string szczegoly = e.Kod ?? "";
            if (e.Powod is string powod) szczegoly += " · " + powod;   // 401 i 409 niosa doprecyzowanie przyczyny
            if (e.ZdarzenieId is string zd) szczegoly += $" · zdarzenie {zd} (podaj je wsparciu wezurlop)";
            Pokaz(t, p, szczegoly);
        }
        finally { _testuj.Enabled = true; }
    }

    IEnumerable<string> Nipy()
    {
        var aktywne = _k.Plik?.Instalacje.SelectMany(i => i.Firmy.Where(f => f.Aktywna).Select(f => (i.KatalogBin, f.FirmIde))).ToHashSet();
        foreach (var w in _k.Wykryte)
            foreach (var f in w.Firmy)
                if (aktywne is null || aktywne.Count == 0 || aktywne.Contains((w.KatalogBin, f.FirmIde)))
                    yield return Nip.Normalizuj(f.Nip);
    }

    void Pokaz(string t, Poziom p, string szczegol) { _wynik.Text = t; _wynik.Poziom = p; Uklad.Pokaz(_wynik, true); _szczegol.Text = szczegol; }

    /// <summary>Adres i klucz — mozna zapisac tez sam adres (klucz dojdzie pozniej); zle wpisane wartosci blokuja zapis.</summary>
    ZmianaSekcji? PrzygotujPolaczenie(List<string> bledy)
    {
        string adres = _adres.Text.Trim(), klucz = _klucz.Text.Trim();
        if (adres != "" && (!Uri.TryCreate(adres, UriKind.Absolute, out var u) || !(u.Scheme == Uri.UriSchemeHttps || u.IsLoopback)))
            bledy.Add("Adres aplikacji wezurlop musi zaczynać się od https://");
        if (klucz != "" && !WzorKlucza.IsMatch(klucz))
            bledy.Add("Klucz instalacji musi mieć 64 znaki szesnastkowe");
        if (bledy.Count > 0) return null;
        // Klucz bez zmian zostaje w dotychczasowej postaci — nie szyfrujemy go drugi raz przy kazdym zapisie.
        string kluczDoPliku = klucz == "" ? ""
            : _k.Plik is not null && Jawny(_k.Plik.KluczApi) == klucz ? _k.Plik.KluczApi
            : PlikKonfiguracji.Zaszyfruj(klucz);
        return ZmianaSekcji.DlaPolaczenia(adres, kluczDoPliku);
    }

    ZmianaSekcji? PrzygotujHarmonogram(List<string> bledy)
    {
        var harm = ZbierzHarmonogram();
        if (harm.Sprawdz() is string blad) { bledy.Add(blad); return null; }
        return ZmianaSekcji.DlaHarmonogramu(harm);
    }
}

