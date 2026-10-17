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

    // Pasek przypiety nad trescia — tak samo jak w zakladce „R2 Platnik": podsumowanie po lewej,
    // akcja po prawej. Dzieki Dock.Top zostaje na miejscu przy przewijaniu dlugiej karty.
    readonly Panel _gora = new() { Dock = DockStyle.Top, Height = 64, BackColor = Styl.Tlo };
    readonly Karta _pasekGorny = new() { Height = 60, Ramka = false, KolorTla = Styl.Tlo };
    readonly Label _stanPolaczenia = E.Tekst("");

    const int Wciecie = 12;

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
        _adres = E.Pole(k.Plik?.AdresSerwera ?? "");
        _klucz = E.Pole(Jawny(k.Plik?.KluczApi), true);
        int szer = Uklad.SzerokoscTresci;
        int szerSekcji = szer - 2 * Wciecie;

        // ---------- pasek przypiety: stan po lewej, test po prawej
        _pasekGorny.Width = szer;   // szerokosc PRZED kotwica — inaczej przycisk ucieka poza karte
        _stanPolaczenia.Location = new Point(2, 18);
        _testuj.Width = 190;
        _testuj.Location = new Point(szer - _testuj.Width, 10);
        _testuj.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _pasekGorny.Controls.AddRange(new Control[] { _stanPolaczenia, _testuj });
        _pasekGorny.Location = new Point(0, 0);
        _gora.Controls.Add(_pasekGorny);
        Controls.Add(_gora);   // po Tresc — doki licza sie od konca, wiec pasek bierze gore, a tresc reszte
        Tresc.Resize += (_, _) => DopasujGore();
        DopasujGore();
        OdswiezStanPaska();
        // Po zapisie adresu albo klucza linia stanu ma pokazac nowa wartosc, a nie te sprzed zapisu.
        k.Zapisano += () => BeginInvoke(new Action(OdswiezStanPaska));

        // ---------- jedna karta mietowa, sekcje jako biale plyty (jak rozwinieta instalacja w „R2 Platnik")
        var karta = new Karta { Width = szer, KolorTla = Styl.Mieta, KolorRamki = Styl.Zielony, OdstepCzesci = Wciecie };
        int y = Uklad.NaglowekKarty(karta, "wezurlop — adres, klucz i harmonogram",
                                    "Wszystko, czym connector łączy się z aplikacją tej firmy.") + Wciecie - Uklad.Duza;

        void Plyta(Blok b)
        {
            b.Plyta = true;
            b.Location = new Point(Wciecie, y);
            karta.Controls.Add(b);
            y += b.Height + Wciecie;
        }

        // ---------- sekcja 1: adres i klucz
        _adres.PlaceholderText = "https://";
        _klucz.Font = new Font("Consolas", 10f);
        var zapiszPolaczenie = new PasekZapisu(k, "Zapisz połączenie", PrzygotujPolaczenie, wRzedzie: true) { Width = szerSekcji - 2 * Uklad.Margines };
        var b1 = new Blok("Adres i klucz instalacji",
                          "Connector sam łączy się z aplikacją — nie potrzebuje otwartych portów ani publicznego adresu.",
                          szerSekcji);
        b1.PolaRowno(new[] { "Adres aplikacji wezurlop", "Klucz instalacji" }, _adres, _klucz);
        b1.Wiersz(_pokaz, 2);
        b1.Wiersz(E.Opis("Domena, pod którą działa aplikacja wniosków urlopowych tej firmy, np. https://urlopy.firma.pl   ·   "
                         + "klucz: 64 znaki szesnastkowe, wydaje go administrator aplikacji wezurlop; zapisywany zaszyfrowany kluczem tej maszyny."),
                  Uklad.Srednia);
        b1.Przyciski(zapiszPolaczenie.Przycisk);
        b1.Wiersz(zapiszPolaczenie, Uklad.Mala);
        var bpol = b1.Domknij();
        Plyta(bpol);
        _pokaz.CheckedChanged += (_, _) => _klucz.UseSystemPasswordChar = !_pokaz.Checked;
        _testuj.Click += async (_, _) => await TestujAsync();
        zapiszPolaczenie.SledzZmiany(bpol);

        // ---------- sekcja 2: tozsamosc osob (sekret i odcisk)
        var bs = new Blok("Tożsamość osób w wezurlop",
            "Identyfikatory osób connector liczy z sekretu tej instalacji — sekret nigdy nie opuszcza tej maszyny. "
            + "Przy przenosinach na inny komputer trzeba go przenieść, inaczej te same osoby założą się w wezurlop po raz drugi.",
            szerSekcji);
        var wierszOdcisku = new Panel { BackColor = Color.Transparent, Width = szerSekcji - 2 * Uklad.Margines, Height = Styl.WysokoscKontrolki };
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
        Plyta(bs.Domknij());

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
        OdswiezSekret();

        // ---------- sekcja 3: harmonogram
        var h = k.Plik?.Harmonogram ?? Harmonogram.Domyslny();

        _odstep.Value = Math.Clamp(h.OdstepMinut, Harmonogram.MinimalnyOdstepMinut, 1440);
        _poza.Value = Math.Clamp(h.OdstepPozaMinut, Harmonogram.MinimalnyOdstepMinut, 1440);
        _od.Value = DateTime.Today + Godz(h.GodzinaOd, 6);
        _do.Value = DateTime.Today + Godz(h.GodzinaDo, 20);
        foreach (var kontrolka in new Control[] { _odstep, _poza, _od, _do })
        {
            kontrolka.Height = Styl.WysokoscKontrolki;
            kontrolka.Width = 110;
        }

        // Jedna plyta na caly harmonogram; „w godzinach pracy" i „poza" rozdziela podtytul, nie osobna karta.
        int szerWiersza = szerSekcji - 2 * Uklad.Margines;
        var bg = new Blok("Harmonogram synchronizacji",
                          "Jedna synchronizacja: odczyt programu kadrowego, wysyłka zmian i zapis zaakceptowanych urlopów.",
                          szerSekcji);
        var godziny = new Panel { BackColor = Color.Transparent, Width = Uklad.Kolumna, Height = Styl.WysokoscKontrolki };
        _od.Location = new Point(0, 0); _od.Width = 110;
        var doTekst = E.Tekst("do"); doTekst.Location = new Point(122, 9);
        _do.Location = new Point(150, 0); _do.Width = 110;
        godziny.Controls.AddRange(new Control[] { _od, doTekst, _do });
        bg.PolaWiersz(new Pole("Synchronizuj co (minut)", _odstep, Uklad.Kolumna), new Pole("Godziny pracy", godziny, Uklad.Kolumna));

        var dni = new Panel { BackColor = Color.Transparent, Width = szerWiersza, Height = 44 };
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
        bg.Wiersz(dni, Uklad.Srednia);

        var podtytulPoza = E.Tekst("Poza godzinami pracy", true);
        bg.Wiersz(podtytulPoza, 4);
        var pozaRzad = new Panel { BackColor = Color.Transparent, Width = szerWiersza, Height = Styl.WysokoscKontrolki };
        _pozaCo.Location = new Point(0, 10); _pozaCo.Checked = !h.WstrzymajPoza;
        _poza.Location = new Point(48, 0); _poza.Width = 110;
        var minut = E.Tekst("minut"); minut.Location = new Point(168, 9);
        _wstrzymaj.Location = new Point(240, 10); _wstrzymaj.Checked = h.WstrzymajPoza;
        pozaRzad.Controls.AddRange(new Control[] { _pozaCo, _poza, minut, _wstrzymaj });
        bg.Wiersz(pozaRzad, Uklad.Srednia);
        bg.Wiersz(_podsumowanie, Uklad.Mala);
        bg.Wiersz(E.Opis("Serwer wezurlop może odstęp wydłużyć (np. prosząc o przerwę) — nigdy nie skróci go poniżej harmonogramu."), Uklad.Srednia);

        var zapiszHarmonogram = new PasekZapisu(k, "Zapisz harmonogram", PrzygotujHarmonogram, wRzedzie: true) { Width = szerSekcji - 2 * Uklad.Margines };
        bg.Przyciski(zapiszHarmonogram.Przycisk);
        bg.Wiersz(zapiszHarmonogram, Uklad.Mala);
        var bharm = bg.Domknij();
        Plyta(bharm);

        foreach (var n in new[] { _odstep, _poza }) n.ValueChanged += (_, _) => Podsumuj();
        foreach (var d in new[] { _od, _do }) d.ValueChanged += (_, _) => Podsumuj();
        foreach (var r in new[] { _pozaCo, _wstrzymaj }) r.CheckedChanged += (_, _) => { _poza.Enabled = _pozaCo.Checked; Podsumuj(); };
        _poza.Enabled = _pozaCo.Checked;
        zapiszHarmonogram.SledzZmiany(bharm);

        // Pozycje plyt ustala karta, nie moje sumowanie y: wysokosci sekcji zmieniaja sie jeszcze po
        // zbudowaniu (odcisk tozsamosci, podsumowanie harmonogramu), a stare „y" dawalo nachodzace plyty.
        // Ponizsze y sluzy juz tylko do ustalenia KOLEJNOSCI sekcji.
        karta.Przelicz();
        Dodaj(karta);
        Podsumuj();
    }

    /// <summary>Przypiety pasek trzyma te sama szerokosc co karta pod nim — inaczej przycisk ucieka na bok.</summary>
    void DopasujGore()
    {
        int w = Tresc.Width - Tresc.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6;
        _pasekGorny.Width = Math.Max(400, Math.Min(w, Uklad.SzerokoscTresci));
    }

    /// <summary>
    /// Linia stanu w pasku. Zglasza to, co wymaga reakcji (brak adresu, brak klucza), a poza tym mowi
    /// rzecz, ktorej nie widac z pol ponizej: kiedy connector ostatnio sie synchronizowal.
    /// Nie pisze „klucz zapisany": skoro klucz jest w konfiguracji, to zdanie nic nie wnosi.
    /// </summary>
    void OdswiezStanPaska()
    {
        string? adres = _k.Plik?.AdresSerwera;
        if (string.IsNullOrWhiteSpace(adres))
        {
            _stanPolaczenia.Text = "Adres aplikacji wezurlop nie jest jeszcze ustawiony";
            _stanPolaczenia.ForeColor = Styl.Koral;
            return;
        }
        string domena = adres.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(_k.Plik?.KluczApi))
        {
            _stanPolaczenia.Text = $"{domena}   ·   brak klucza instalacji";
            _stanPolaczenia.ForeColor = Styl.Koral;
            return;
        }
        _stanPolaczenia.Text = domena + (OstatniaSynchronizacja() is DateTime t
            ? $"   ·   ostatnia synchronizacja {Styl.DataGodzina(t)}"
            : "   ·   jeszcze nie synchronizowano");
        _stanPolaczenia.ForeColor = Styl.Granat;
    }

    /// <summary>Czas ostatniego zakonczonego cyklu z dziennika uslugi — ten sam zapis czyta ekran Status.</summary>
    DateTime? OstatniaSynchronizacja()
    {
        var ostatni = EkranStatus.CzytajDziennik(_k.KatalogDziennika, 300).LastOrDefault(l => l.Contains(": cykl "));
        return ostatni is not null && DateTime.TryParse(ostatni[..19], out var t) ? t : null;
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
    /// <summary>
    /// Wynik w oknie modalnym — tak samo jak test SQL i test logowania do R2 na zakladce „R2 Platnik".
    /// Tresc wyniku jest krotka: „Powodzenie" albo sama rzecz, ktora nie gra. Bez czasow i numerow
    /// protokolu, zgodnie z zasada przyjeta dla pozostalych testow.
    /// </summary>
    Task TestujAsync()
    {
        if (!Uri.TryCreate(_adres.Text.Trim(), UriKind.Absolute, out var adres))
        {
            DialogPytania.Informacja(FindForm()!, "Test połączenia",
                "Adres aplikacji wezurlop jest nieprawidłowy.", "Powinien zaczynać się od https:// i wskazywać domenę tej firmy.");
            return Task.CompletedTask;
        }
        string? nip = Nipy().FirstOrDefault();
        string klucz = _klucz.Text.Trim();

        DialogPracy.Wykonaj(FindForm(), "Test połączenia z wezurlop",
            adres.Host + (nip is null ? "" : $" · firma NIP {EkranStatus.FormatNip(nip)}"),
            "Łączę z wezurlop…", async () =>
            {
                using var klient = new KlientTenanta(adres, klucz, "okno-konfiguracji", limitCzasu: TimeSpan.FromSeconds(20));
                try
                {
                    var stan = await klient.StanAsync(nip);
                    if (nip is null)
                        return new DialogPracy.Wynik(Poziom.Ok, "Powodzenie",
                            "Klucz działa. Firmy jeszcze nie wykryto — po skonfigurowaniu instalacji test sprawdzi także, czy jest zatwierdzona.");

                    return stan.Firma?.Stan switch
                    {
                        "ok" => new DialogPracy.Wynik(Poziom.Ok, "Powodzenie", "Firma jest zatwierdzona w wezurlop."),
                        "firma_zgloszona" => new DialogPracy.Wynik(Poziom.Uwaga, "Firma czeka na zatwierdzenie",
                            "Klucz działa. Administrator wezurlop musi zatwierdzić firmę — synchronizacja ruszy sama po zatwierdzeniu."),
                        "firma_nieznana" => new DialogPracy.Wynik(Poziom.Uwaga, "Firma nie jest jeszcze zgłoszona",
                            "Klucz działa. Connector zgłosi firmę przy najbliższym cyklu; zatwierdza ją administrator wezurlop."),
                        "firma_poza_zakresem" => new DialogPracy.Wynik(Poziom.Blad, "Firma poza zakresem klucza",
                            "Sprawdź, czy klucz jest wydany dla tej firmy."),
                        _ => new DialogPracy.Wynik(Poziom.Ok, "Powodzenie", "Serwer nie podał stanu firmy."),
                    };
                }
                catch (BladProtokolu e)
                {
                    string tekst = e.Status switch
                    {
                        0 => "Brak połączenia z serwerem",
                        // Connector wysyła NIP wyłącznie nagłówkiem X-Nip. Jeśli serwer widzi do tego parametr
                        // ?nip= z inną wartością, dokłada go coś po drodze — to błąd konfiguracji, nie usterka łącza.
                        400 when e.Kod == "NIP_NIESPOJNY" => "Serwer dostał dwa różne NIP-y w jednym żądaniu",
                        401 => "Klucz nieprawidłowy albo wygasł",
                        403 => "Firma nie jest zatwierdzona w wezurlop",
                        404 => "Serwer nie zna operacji „stan” — zaktualizuj aplikację wezurlop",
                        409 => "Niezgodna wersja protokołu — zaktualizuj connector",
                        422 => "NIP firmy jest niepoprawny — popraw go w programie kadrowym",
                        429 or 503 => "Serwer prosi o chwilę przerwy",
                        _ => $"Serwer odpowiedział błędem {e.Status}",
                    };
                    string szczegol = e.Status == 400 && e.Kod == "NIP_NIESPOJNY"
                        ? "Connector wysyła NIP tylko nagłówkiem, więc parametr dokłada pośrednik po drodze albo działa starsza wersja connectora."
                        : "";
                    if (e.ZdarzenieId is string zd) szczegol += (szczegol.Length > 0 ? " " : "") + $"Zdarzenie {zd} — podaj je wsparciu wezurlop.";
                    return new DialogPracy.Wynik(e.Status is 403 or 404 or 429 or 503 ? Poziom.Uwaga : Poziom.Blad,
                                                 tekst, szczegol.Length > 0 ? szczegol : null);
                }
            });
        return Task.CompletedTask;
    }

    IEnumerable<string> Nipy()
    {
        var aktywne = _k.Plik?.Instalacje.SelectMany(i => i.Firmy.Where(f => f.Aktywna).Select(f => (i.KatalogBin, f.FirmIde))).ToHashSet();
        foreach (var w in _k.Wykryte)
            foreach (var f in w.Firmy)
                if (aktywne is null || aktywne.Count == 0 || aktywne.Contains((w.KatalogBin, f.FirmIde)))
                    yield return Nip.Normalizuj(f.Nip);
    }


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

