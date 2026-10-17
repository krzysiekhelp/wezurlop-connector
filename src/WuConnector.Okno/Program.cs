// Okno konfiguracji connectora i ikona w zasobniku.
//
//   WuConnector.Okno.exe                         — okno + ikona w zasobniku
//   WuConnector.Okno.exe --zasobnik              — start schowany w zasobniku (autostart przy logowaniu)
//   WuConnector.Okno.exe --konfiguracja <plik>   — inny plik niz domyslny w ProgramData
//   WuConnector.Okno.exe --zrzut <katalog>       — zapisuje obraz kazdego ekranu do PNG i konczy (dokumentacja, testy)
//   WuConnector.Okno.exe --konfiguruj            — samo okno (bez ikony), uzywane przy podniesieniu uprawnien
//   WuConnector.Okno.exe --zastosuj <plik>       — nanosi przekazana sekcje konfiguracji i otwiera okno (po UAC)
//   WuConnector.Okno.exe --ekran <nazwa>         — otwiera okno na tym ekranie (z --zastosuj: tam, gdzie byl uzytkownik)
//   WuConnector.Okno.exe --usluga start|stop     — jedna operacja na usludze i koniec (po UAC), bez okna
using WuConnector.Core.Konfiguracja;

namespace WuConnector.Okno;

internal static class Program
{
    public static bool Wyjscie { get; private set; }

    [STAThread]
    static int Main(string[] args)
    {
        string? Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        ApplicationConfiguration.Initialize();
        var k = new Kontekst(Arg("--konfiguracja") ?? Path.Combine(PlikKonfiguracji.DomyslnyKatalog, "konfiguracja.json"));

        if (Arg("--zrzut") is string katalog) return Zrzut(k, katalog);

        // Okno potrafilo zawisnac bez sladu — od teraz zostawia wlasny dziennik: puls co minute,
        // nieobslugiwane wyjatki, start i koniec. Zrzuty i operacje bez okna tego nie potrzebuja.
        DziennikOkna.Zacznij();

        // Jedna operacja na usludze po podniesieniu uprawnien — bez okna, bez ikony.
        if (Arg("--usluga") is string operacja)
        {
            var (ok, opis) = k.SterujUsluga(operacja == "start");
            if (!ok) MessageBox.Show(opis, "WU_Connector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return ok ? 0 : 1;
        }

        // Okno z podniesionymi uprawnieniami, otwarte z ikony w zasobniku — bez drugiej ikony; zamkniecie konczy proces.
        if (args.Contains("--konfiguruj"))
        {
            Wyjscie = true;
            // Sekcja przekazana przez okno bez uprawnien: zapisujemy ja, zanim uzytkownik cokolwiek zobaczy.
            string? wynikZapisu = Arg("--zastosuj") is string przekaz ? k.ZastosujPrzekazanie(przekaz) : null;
            Application.Run(new OknoGlowne(k, Arg("--ekran"), wynikZapisu));
            return 0;
        }

        using var jeden = new Mutex(true, "WU_Connector.Okno", out bool pierwszy);
        if (!pierwszy) return 0;   // okno juz dziala — ikona jest w zasobniku

        var okno = new OknoGlowne(k);
        using var ikona = new NotifyIcon { Icon = Styl.IkonaZasobnika(Styl.Szary), Text = "WU_Connector", Visible = true };
        var menu = new ContextMenuStrip { Font = Styl.Czcionka(10f) };
        menu.Items.Add("Otwórz okno connectora", null, (_, _) => Pokaz(okno));
        menu.Items.Add("Synchronizuj teraz", null, (_, _) =>
        {
            var (ok, opis) = Kontekst.ZlecSynchronizacje();
            ikona.ShowBalloonTip(5000, "WU_Connector", opis, ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zamknij okno (usługa pracuje dalej)", null, (_, _) => { Wyjscie = true; ikona.Visible = false; Application.Exit(); });
        ikona.ContextMenuStrip = menu;
        ikona.DoubleClick += (_, _) => Pokaz(okno);

        // Kolor kropki w ikonie = stan ostatnich cykli z dziennika uslugi.
        var zegar = new System.Windows.Forms.Timer { Interval = 30_000 };
        zegar.Tick += (_, _) => OdswiezIkone(ikona, k);
        zegar.Start();
        OdswiezIkone(ikona, k);

        if (!args.Contains("--zasobnik")) okno.Show();
        Application.Run();
        return 0;
    }

    static void Pokaz(Form f) { f.Show(); if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal; f.Activate(); }

    static void OdswiezIkone(NotifyIcon ikona, Kontekst k)
    {
        var ostatni = EkranStatus.CzytajDziennik(k.KatalogDziennika, 300).LastOrDefault(l => l.Contains(": cykl "));
        Color kolor = Styl.Szary;
        string opis = "WU_Connector: brak cykli";
        if (ostatni is not null && DateTime.TryParse(ostatni[..19], out var t))
        {
            var wiek = DateTime.Now - t;
            kolor = wiek < TimeSpan.FromMinutes(5) ? Styl.Zielony : wiek < TimeSpan.FromHours(1) ? Styl.Pomarancz : Styl.Koral;
            opis = $"WU_Connector: ostatni cykl {t:HH:mm}";
        }
        // Ikony stanu sa wspoldzielone i zyja do konca procesu — nie zwalniamy ich po podmianie.
        var nowa = Styl.IkonaZasobnika(kolor);
        if (!ReferenceEquals(ikona.Icon, nowa)) ikona.Icon = nowa;
        ikona.Text = opis;
    }

    /// <summary>Renderuje kazdy ekran poza widocznym obszarem i zapisuje PNG.</summary>
    static int Zrzut(Kontekst k, string katalog)
    {
        Directory.CreateDirectory(katalog);
        Wyjscie = true;
        var okno = new OknoGlowne(k) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false };
        okno.Shown += async (_, _) =>
        {
            int i = 1;
            foreach (var e in okno.Ekrany)
            {
                await okno.PokazAsync(e);
                await Task.Delay(400);
                Application.DoEvents();
                using var bmp = new Bitmap(okno.Width, okno.Height);
                okno.DrawToBitmap(bmp, new Rectangle(0, 0, okno.Width, okno.Height));
                bmp.Save(Path.Combine(katalog, $"{i:00}-{Bez(e.Tytul)}.png"), System.Drawing.Imaging.ImageFormat.Png);
                // Instalacje startuja zwiniete — na zrzucie chcemy widziec sekcje i ich stopki zapisu.
                if (e is EkranInstalacje instalacje)
                {
                    // Karta z kilkoma firmami — na tej maszynie kazda instalacja ma jedna.
                    var kilka = EkranInstalacje.KartaKilkuFirmDlaZrzutu(k);
                    var pomocniczy = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false };
                    pomocniczy.ClientSize = new Size(kilka.Width, 1200);
                    pomocniczy.Controls.Add(kilka);
                    pomocniczy.Show();
                    Application.DoEvents();
                    await Task.Delay(200);
                    using (var kb2 = new Bitmap(kilka.Width, kilka.Height))
                    {
                        kilka.DrawToBitmap(kb2, new Rectangle(0, 0, kilka.Width, kilka.Height));
                        kb2.Save(Path.Combine(katalog, $"{i:00}-{Bez(e.Tytul)}-kilka-firm.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    pomocniczy.Close();

                    instalacje.RozwinPierwszaDlaZrzutu();
                    Application.DoEvents();
                    await Task.Delay(200);
                    // Cale okno z rozwinieta instalacja — tylko tak widac prawdziwe marginesy kolumny tresci.
                    using (var ob = new Bitmap(okno.Width, okno.Height))
                    {
                        okno.DrawToBitmap(ob, new Rectangle(0, 0, okno.Width, okno.Height));
                        ob.Save(Path.Combine(katalog, $"{i:00}-{Bez(e.Tytul)}-okno-rozwiniete.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    foreach (var karta in e.Karty.Where(c => c is Karta { Height: > 120 }))
                    {
                        using var kb = new Bitmap(karta.Width, karta.Height);
                        karta.DrawToBitmap(kb, new Rectangle(0, 0, karta.Width, karta.Height));
                        kb.Save(Path.Combine(katalog, $"{i:00}-{Bez(e.Tytul)}-rozwinieta.png"), System.Drawing.Imaging.ImageFormat.Png);
                        break;
                    }
                }
                if (e.Przewijany)
                {
                    int n = 1;
                    foreach (var karta in e.Karty)
                    {
                        using var kb = new Bitmap(karta.Width, karta.Height);
                        karta.DrawToBitmap(kb, new Rectangle(0, 0, karta.Width, karta.Height));
                        kb.Save(Path.Combine(katalog, $"{i:00}-{Bez(e.Tytul)}-karta{n++}.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                i++;
            }
            // Okno kont MSSQL — laczy sie z serwerem i pokazuje jego konta oraz bazy (sam odczyt, nic nie zmienia).
            {
                using var dlg = new DialogKontaSql(k)
                { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false };
                dlg.ClientSize = new Size(dlg.ClientSize.Width, 1180);   // na zrzut okno rozciagamy, zeby bylo widac wszystkie trzy kroki
                dlg.Show();
                Application.DoEvents();
                await Task.Delay(200);
                Zapisz(dlg, Path.Combine(katalog, $"{i:00}-konta-mssql-przed-polaczeniem.png"));

                await dlg.PolaczAsync();
                Application.DoEvents();
                Zapisz(dlg, Path.Combine(katalog, $"{i:00}-konta-mssql.png"));

                // Okno modalne nad nim: zakladanie konta (tu tylko do zrzutu, bez pokazywania modalnie).
                using (var male = DialogNoweKonto.DoZrzutu("wu_r2p_platnik10_alfa_2"))
                {
                    male.StartPosition = FormStartPosition.Manual;
                    male.Location = new Point(-5000, -5000);
                    male.ShowInTaskbar = false;
                    male.Show();
                    Application.DoEvents();
                    await Task.Delay(200);
                    Zapisz(male, Path.Combine(katalog, $"{i:00}-konta-mssql-nowe-konto.png"));
                    male.Close();
                }

                // Okno z haslem i okno potwierdzenia usuniecia — tylko do zrzutu, bez wykonywania operacji.
                using (var h = DialogHasla.DoZrzutu("wu_r2p_platnik10_alfa", "7Qf2-xT9m-Kd4V-pR8s",
                           "Wpisz je w karcie instalacji, w polu „Hasło” — razem z loginem tego konta."))
                {
                    h.StartPosition = FormStartPosition.Manual;
                    h.Location = new Point(-5000, -5000);
                    h.ShowInTaskbar = false;
                    h.Show();
                    Application.DoEvents();
                    await Task.Delay(200);
                    Zapisz(h, Path.Combine(katalog, $"{i:00}-konta-mssql-haslo.png"));
                    h.Close();
                }

                // Okno testu: w trakcie pracy (kolowrotek) i z gotowym wynikiem.
                foreach (var (nazwa, wynik) in new (string, DialogPracy.Wynik?)[]
                {
                    ("czeka", null),
                    ("wynik", new DialogPracy.Wynik(Poziom.Ok, "Powodzenie")),
                    ("blad", new DialogPracy.Wynik(Poziom.Blad, "Błędne hasło użytkownika WNIOSKI_DEMO")),
                })
                {
                    using var t = DialogPracy.DoZrzutu("Test logowania do R2Płatnika",
                        "NowyR2Platnik.10_alfa · firma ALFA · konto WNIOSKI_DEMO", "Loguję do R2Płatnika…", wynik);
                    t.StartPosition = FormStartPosition.Manual;
                    t.Location = new Point(-5000, -5000);
                    t.ShowInTaskbar = false;
                    t.Show();
                    Application.DoEvents();
                    await Task.Delay(200);
                    Zapisz(t, Path.Combine(katalog, $"{i:00}-test-{nazwa}.png"));
                    t.Close();
                }

                using (var p = DialogPytania.DoZrzutu("Usunięcie konta",
                           @"Usunąć konto wu_r2p_platnik10_alfa z serwera SERWER-DEMO\OPTIMA? Tej operacji nie da się cofnąć.",
                           "Konto czyta 2 baz(y) R2Płatnika — dostęp zniknie razem z nim.", "Usuń konto", grozny: true))
                {
                    p.StartPosition = FormStartPosition.Manual;
                    p.Location = new Point(-5000, -5000);
                    p.ShowInTaskbar = false;
                    p.Show();
                    Application.DoEvents();
                    await Task.Delay(200);
                    Zapisz(p, Path.Combine(katalog, $"{i:00}-konta-mssql-usuniecie.png"));
                    p.Close();
                }

                // Okno uprawnien — pokazujemy je dla konta connectora z prawdziwymi bazami serwera.
                if (dlg.StanSerwera is { } stan && stan.Konta.FirstOrDefault(x => x.Connectorowe) is { } konto)
                {
                    using var upr = DialogUprawnien.DoZrzutu(konto, stan.Bazy, dlg.NazwaInstalacjiDlaZrzutu);
                    upr.StartPosition = FormStartPosition.Manual;
                    upr.Location = new Point(-5000, -5000);
                    upr.ShowInTaskbar = false;
                    upr.Show();
                    Application.DoEvents();
                    await Task.Delay(200);
                    Zapisz(upr, Path.Combine(katalog, $"{i:00}-konta-mssql-uprawnienia.png"));
                    upr.Close();
                }
                dlg.Close();

                static void Zapisz(Form f, string plik)
                {
                    using var bmp = new Bitmap(f.Width, f.Height);
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(plik, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            okno.Close();
        };
        Application.Run(okno);
        return 0;
    }

    static string Bez(string s) => new(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => char.IsLetterOrDigit(c) && c < 128 || c == ' ').Select(c => c == ' ' ? '-' : c).ToArray());
}
