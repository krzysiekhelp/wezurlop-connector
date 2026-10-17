using System.Globalization;
using System.ServiceProcess;
using System.Text.RegularExpressions;

namespace WuConnector.Okno;

/// <summary>Stan uslugi i ostatni cykl kazdej firmy — z dziennika uslugi (dziala takze przy zatrzymanej usludze).</summary>
public sealed class EkranStatus : Ekran
{
    public const string NazwaUslugi = "WU_Connector";

    static readonly Regex Cykl = new(@"^(\S+ \S+) \w+ (.+?) \(NIP (\d+)\): cykl (\d+) ms, wyslano (\d+), usunieto (\d+), zadan (\d+), wynikow (\d+)");
    static readonly Regex Linia = new(@"^(\S+ \S+) (INFO|UWAG|BLAD|KRYT) (.*)$");

    readonly Kontekst _k;
    readonly Karta _hero = new() { Height = 200, KolorTla = Styl.Mieta, Ramka = false, Promien = 24 };
    readonly Label _stan = E.Naglowek("");
    readonly Label _stanOpis = E.Tekst("");
    readonly Znacznik _usluga = new();
    readonly Przycisk _start = new() { Text = "Uruchom usługę", Width = 170 };
    readonly Przycisk _stop = new() { Text = "Zatrzymaj", Rodzaj = RodzajPrzycisku.Drugi, Width = 120 };
    readonly Przycisk _synchronizuj = new() { Text = "Synchronizuj teraz", Width = 200 };
    readonly Label _wynikSynchronizacji = E.Opis("");
    readonly Karta _kartaFirm = new();
    readonly Panel _wiersze = new() { BackColor = Color.Transparent };
    readonly List<Poziom> _poziomy = new();
    readonly Karta _zdarzenia = new() { Height = 220 };

    // Siatka tabeli firm: x i szerokosc kazdej kolumny. Liczby wyrownane do prawej krawedzi swojej kolumny.
    // Kolumny sa stale, zeby wiersze ustawialy sie w slupku takze przy weszym oknie (ostatnia konczy sie na 764).
    const int KolFirma = Uklad.Margines, SzerFirma = 200;
    const int KolStan = KolFirma + SzerFirma + 16, SzerStan = 120;
    const int KolCzas = KolStan + SzerStan + 16, SzerCzas = 170;
    const int KolWyslano = KolCzas + SzerCzas + 10, SzerLiczby = 72;
    const int KolUsunieto = KolWyslano + SzerLiczby;
    const int KolZadan = KolUsunieto + SzerLiczby, SzerZadan = 64;
    const int WysokoscWiersza = 56;
    readonly FlowLayoutPanel _listaZdarzen = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White, AutoSize = true };
    int _gornaKrawedzZdarzen;

    public override string Tytul => "Status";
    public override string Podtytul => "Czy connector pracuje i co ostatnio przesłał do wezurlop";

    public EkranStatus(Kontekst k)
    {
        _k = k;
        _hero.Width = Uklad.SzerokoscTresci;
        _stan.Font = Styl.Naglowek(17f);
        _stan.Location = new Point(Uklad.Margines, 26);
        _stanOpis.Location = new Point(Uklad.Margines + 1, 64);
        _stanOpis.MaximumSize = new Size(560, 0);
        _usluga.Location = new Point(Uklad.Margines + 1, 102);
        _start.Location = new Point(Uklad.Margines, 138);
        _stop.Location = new Point(Uklad.Margines + 180, 138);
        _start.Click += (_, _) => Usluga(true);
        _stop.Click += (_, _) => Usluga(false);
        _synchronizuj.Location = new Point(Uklad.Margines, 138);
        _wynikSynchronizacji.Location = new Point(Uklad.Margines + 348, 150);
        _synchronizuj.Click += async (_, _) => await SynchronizujAsync();
        var maskotka = new PictureBox
        {
            Image = Styl.Obraz("mascot.png"), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(190, 178),
            Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = Styl.Mieta,
        };
        _hero.Controls.AddRange(new Control[] { _stan, _stanOpis, _usluga, _start, _stop, _synchronizuj, _wynikSynchronizacji, maskotka });
        _hero.Resize += (_, _) => maskotka.Location = new Point(_hero.Width - maskotka.Width - 30, 11);
        Dodaj(_hero);

        // ---- tabela firm: naglowek karty, rzad naglowkow kolumn, wiersze po 56 px
        _kartaFirm.Width = Uklad.SzerokoscTresci;
        int yf = Uklad.NaglowekKarty(_kartaFirm, "Firmy", "Ostatni zakończony cykl każdej firmy — z dziennika usługi.");
        _kartaFirm.Controls.Add(NaglowkiKolumn(yf));
        _wiersze.Location = new Point(0, yf + 26);
        _wiersze.Width = Uklad.SzerokoscTresci;
        _wiersze.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _kartaFirm.Controls.Add(_wiersze);
        Dodaj(_kartaFirm);

        _zdarzenia.Width = Uklad.SzerokoscTresci;
        _gornaKrawedzZdarzen = Uklad.NaglowekKarty(_zdarzenia, "Ostatnie ostrzeżenia i błędy") - Uklad.Duza + Uklad.Srednia;
        _listaZdarzen.Location = new Point(Uklad.Margines, _gornaKrawedzZdarzen);
        _listaZdarzen.Width = Uklad.SzerokoscTresci - 2 * Uklad.Margines;
        _listaZdarzen.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _zdarzenia.Controls.Add(_listaZdarzen);
        Dodaj(_zdarzenia);
    }

    public override Task PokazAsync()
    {
        OdswiezUsluge();
        var linie = CzytajDziennik(_k.KatalogDziennika, 2000);

        // ---- ostatni cykl kazdej firmy
        var ostatnie = new Dictionary<string, Match>();
        foreach (var l in linie) { var m = Cykl.Match(l); if (m.Success) ostatnie[m.Groups[3].Value] = m; }

        _wiersze.Controls.Clear();
        _poziomy.Clear();
        int yw = 0;
        if (ostatnie.Count == 0)
        {
            var pusto = E.Opis("Usługa jeszcze nie synchronizowała — uruchom ją albo zleć synchronizację powyżej.");
            pusto.Location = new Point(Uklad.Margines, 6);
            _wiersze.Controls.Add(pusto);
            yw = 34;
        }
        foreach (var m in ostatnie.Values)
        {
            var czas = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var wiek = DateTime.Now - czas;
            var poziom = wiek < TimeSpan.FromMinutes(5) ? Poziom.Ok : wiek < TimeSpan.FromHours(1) ? Poziom.Uwaga : Poziom.Blad;
            _poziomy.Add(poziom);
            var w = WierszFirmy(m, czas, poziom, yw > 0);
            w.Location = new Point(0, yw);
            _wiersze.Controls.Add(w);
            yw += w.Height;
        }
        _wiersze.Height = yw;
        _kartaFirm.Height = _wiersze.Top + yw + Uklad.Srednia;

        // ---- zdarzenia
        _listaZdarzen.Controls.Clear();
        var zd = linie.Select(l => Linia.Match(l)).Where(m => m.Success && m.Groups[2].Value != "INFO").TakeLast(8).Reverse().ToList();
        if (zd.Count == 0) _listaZdarzen.Controls.Add(E.Opis("Brak ostrzeżeń — wszystko w porządku."));
        foreach (var m in zd)
        {
            // Stale kolumny: znacznik, czas, tresc. Tresc konczy sie wielokropkiem — zdarzenie nie rozpycha karty.
            var wiersz = new Panel { Height = 32, Width = _listaZdarzen.ClientSize.Width, BackColor = Color.White, Margin = new Padding(0) };
            var zn = new Znacznik { Text = m.Groups[2].Value == "UWAG" ? "uwaga" : "błąd", Poziom = m.Groups[2].Value == "UWAG" ? Poziom.Uwaga : Poziom.Blad };
            zn.Location = new Point(0, 4);
            var czasZd = E.Opis(m.Groups[1].Value[..16]);
            czasZd.AutoSize = false;
            czasZd.Size = new Size(120, 20);
            czasZd.TextAlign = ContentAlignment.MiddleLeft;
            czasZd.Location = new Point(80, 5);
            var t = E.Tekst(m.Groups[3].Value);
            t.AutoSize = false;
            t.AutoEllipsis = true;
            t.Size = new Size(wiersz.Width - 208, 20);
            t.Location = new Point(208, 5);
            wiersz.Controls.AddRange(new Control[] { zn, czasZd, t });
            _listaZdarzen.Controls.Add(wiersz);
        }

        // ---- podsumowanie w naglowku karty
        bool wszystkieOk = _poziomy.Count > 0 && _poziomy.All(p => p == Poziom.Ok);
        _stan.Text = ostatnie.Count == 0 ? "Connector jeszcze nie synchronizował"
                   : wszystkieOk ? "Connector pracuje" : "Connector wymaga uwagi";
        _hero.KolorTla = ostatnie.Count == 0 ? Styl.Niebieski : wszystkieOk ? Styl.Mieta : Styl.Zolty;
        foreach (var pb in _hero.Controls.OfType<PictureBox>()) pb.BackColor = _hero.KolorTla;
        _listaZdarzen.Height = zd.Count == 0 ? 24 : zd.Count * 32;
        _zdarzenia.Height = _gornaKrawedzZdarzen + _listaZdarzen.Height + Uklad.Duza;
        _hero.Invalidate();
        _stanOpis.Text = wszystkieOk
            ? "Dane kadrowe trafiają do wezurlop, a zaakceptowane urlopy — do karty urlopowej R2Płatnika."
            : "Sprawdź status usługi, połączenie z wezurlop i ostatnie ostrzeżenia poniżej.";
        return Task.CompletedTask;
    }

    /// <summary>Rzad naglowkow kolumn tabeli firm — 11 px, wersaliki, kolor podpisu (SPOJNOSC-WIZUALNA §7).</summary>
    static Panel NaglowkiKolumn(int y)
    {
        var p = new Panel
        {
            BackColor = Color.Transparent, Location = new Point(0, y), Height = 26, Width = Uklad.SzerokoscTresci,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        p.Controls.Add(Naglowek("FIRMA", KolFirma, SzerFirma, false));
        p.Controls.Add(Naglowek("STAN", KolStan, SzerStan, false));
        p.Controls.Add(Naglowek("OSTATNIA SYNCHRONIZACJA", KolCzas, SzerCzas, false));
        p.Controls.Add(Naglowek("WYSŁANO", KolWyslano, SzerLiczby, true));
        p.Controls.Add(Naglowek("USUNIĘTO", KolUsunieto, SzerLiczby, true));
        p.Controls.Add(Naglowek("ZADAŃ", KolZadan, SzerZadan, true));
        p.Controls.Add(Uklad.Linia(25, Uklad.SzerokoscTresci));
        return p;
    }

    static Label Naglowek(string t, int x, int szer, bool doPrawej) => new()
    {
        Text = t, AutoSize = false, Size = new Size(szer, 16), Location = new Point(x, 4),
        Font = Styl.Czcionka(Styl.NaglowekKolumny), ForeColor = Styl.Szary, BackColor = Color.Transparent,
        TextAlign = doPrawej ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft,
    };

    /// <summary>Jeden wiersz tabeli: nazwa i NIP w jednej komorce, czas wzgledny nad bezwzglednym, liczby do prawej.</summary>
    Panel WierszFirmy(Match m, DateTime czas, Poziom p, bool zLinia)
    {
        var w = new Panel
        {
            BackColor = Color.Transparent, Height = WysokoscWiersza, Width = _wiersze.ClientSize.Width,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var nazwa = E.Tekst(m.Groups[2].Value.Split('/')[0], true);
        nazwa.AutoSize = false;
        nazwa.Size = new Size(SzerFirma, 20);
        nazwa.AutoEllipsis = true;
        nazwa.Location = new Point(KolFirma, 8);
        var nip = E.Opis("NIP " + FormatNip(m.Groups[3].Value));
        nip.Location = new Point(KolFirma + 1, 30);

        // Czas wzgledny pierwsza forma, bezwzgledny druga i ciszej (SPOJNOSC-WIZUALNA §6).
        var wzglednie = E.Tekst(Styl.Wzglednie(czas));
        wzglednie.Location = new Point(KolCzas, 8);
        var dokladnie = E.Opis($"{Styl.DataGodzina(czas)} · {int.Parse(m.Groups[4].Value) / 1000.0:0.0} s");
        dokladnie.Location = new Point(KolCzas + 1, 30);

        w.Controls.AddRange(new Control[] { nazwa, nip, wzglednie, dokladnie });
        w.Controls.Add(Liczba(m.Groups[5].Value, KolWyslano, SzerLiczby));
        w.Controls.Add(Liczba(m.Groups[6].Value, KolUsunieto, SzerLiczby));
        w.Controls.Add(Liczba(m.Groups[7].Value, KolZadan, SzerZadan));

        // Stan tylko wtedy, gdy odbiega od normy — normalny stan nie dostaje koloru (SPOJNOSC-WIZUALNA §3).
        var z = new Znacznik
        {
            Text = p == Poziom.Ok ? "aktualne" : p == Poziom.Uwaga ? "opóźnione" : "nieaktualne",
            Poziom = p,
        };
        z.Location = new Point(KolStan, 16);
        w.Controls.Add(z);
        if (zLinia) w.Controls.Add(Uklad.Linia(0, w.Width));   // kreska rozdziela wiersze, nie zamyka ostatniego
        return w;
    }

    static Label Liczba(string wartosc, int x, int szer) => new()
    {
        Text = wartosc, AutoSize = false, Size = new Size(szer, 20), Location = new Point(x, 8),
        Font = Styl.Cyfry(Styl.Tresc), ForeColor = Styl.Tekst, BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleRight,
    };

    void OdswiezUsluge()
    {
        try
        {
            using var sc = new ServiceController(NazwaUslugi);
            var s = sc.Status;
            (_usluga.Text, _usluga.Poziom) = s switch
            {
                ServiceControllerStatus.Running => ("usługa uruchomiona", Poziom.Ok),
                ServiceControllerStatus.Stopped => ("usługa zatrzymana", Poziom.Blad),
                _ => ("usługa: " + s.ToString().ToLowerInvariant(), Poziom.Uwaga),
            };
            _start.Visible = s != ServiceControllerStatus.Running;
            _stop.Visible = s == ServiceControllerStatus.Running;
            _synchronizuj.Visible = s == ServiceControllerStatus.Running;
            _stop.Location = _start.Visible ? new Point(208, 138) : new Point(240, 138);
        }
        catch (InvalidOperationException)
        {
            // Bez uslugi Windows connector moze dzialac jako zwykly program (testy, diagnostyka).
            bool program = System.Diagnostics.Process.GetProcessesByName("WuConnector.Service").Length > 0;
            (_usluga.Text, _usluga.Poziom) = program
                ? ("connector działa jako program (bez usługi Windows)", Poziom.Info)
                : ("usługa nie jest zainstalowana", Poziom.Wylaczony);
            _start.Visible = _stop.Visible = _synchronizuj.Visible = false;
        }
    }

    /// <summary>Zleca cykl uslugi (bez uprawnien administratora) i po chwili odswieza status.</summary>
    async Task SynchronizujAsync()
    {
        var (ok, opis) = Kontekst.ZlecSynchronizacje();
        _wynikSynchronizacji.ForeColor = ok ? Styl.Granat : Styl.Koral;
        _wynikSynchronizacji.Text = ok ? "Zlecono — trwa synchronizacja…" : opis;
        if (!ok) return;
        _synchronizuj.Enabled = false;   // jedno zlecenie na raz; usluga i tak laczy powtorzone zlecenia
        await Task.Delay(TimeSpan.FromSeconds(10));
        await PokazAsync();
        _wynikSynchronizacji.Text = $"Zlecono o {DateTime.Now.AddSeconds(-10):HH:mm:ss} — przebieg w Dzienniku";
        _synchronizuj.Enabled = true;
    }

    /// <summary>
    /// Start i zatrzymanie uslugi. Bez uprawnien administratora Kontekst podnosi je przez UAC dopiero teraz —
    /// po kliknieciu przycisku — i wykonuje jedna operacje w osobnym procesie.
    /// </summary>
    async void Usluga(bool uruchom)
    {
        _start.Enabled = _stop.Enabled = false;
        var (ok, opis) = _k.SterujUsluga(uruchom);
        _wynikSynchronizacji.ForeColor = ok ? Styl.Granat : Styl.Koral;
        _wynikSynchronizacji.Text = opis;
        if (ok && !Kontekst.Administrator) await Task.Delay(TimeSpan.FromSeconds(6));   // czas na UAC i na usluge
        _start.Enabled = _stop.Enabled = true;
        await PokazAsync();
    }

    public static List<string> CzytajDziennik(string katalog, int ile)
    {
        if (!Directory.Exists(katalog)) return new();
        var pliki = Directory.GetFiles(katalog, "dziennik-*.log").OrderBy(f => f).TakeLast(2);
        var linie = new List<string>();
        foreach (var f in pliki)
        {
            using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(s);
            while (r.ReadLine() is { } l) linie.Add(l);
        }
        return linie.TakeLast(ile).ToList();
    }

    static string Kiedy(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d < TimeSpan.FromMinutes(1)) return "przed chwilą";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes} min temu";
        return Styl.Wzglednie(t) + " · " + Styl.DataGodzina(t);
    }

    public static string FormatNip(string n) => n.Length == 10 ? $"{n[..3]}-{n[3..6]}-{n[6..8]}-{n[8..]}" : n;
}

