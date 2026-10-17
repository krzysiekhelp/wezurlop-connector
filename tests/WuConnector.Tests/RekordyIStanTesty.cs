using System.Text.Json.Nodes;
using WuConnector.Core.Odczyt;
using WuConnector.Core.Rekordy;
using WuConnector.Core.Stan;

namespace WuConnector.Tests;

public class RekordTesty
{
    static Rekord R(string id, string wartosc) =>
        new(Zbiory.Nieobecnosc, id, new[] { new KeyValuePair<string, JsonNode?>("pole", wartosc) });

    [Fact]
    public void Ta_sama_tresc_ta_sama_suma()
    {
        Assert.Equal(R("urlop:1", "a").Suma, R("urlop:1", "a").Suma);
        Assert.NotEqual(R("urlop:1", "a").Suma, R("urlop:1", "b").Suma);
        Assert.StartsWith("sha256:", R("urlop:1", "a").Suma);
        Assert.Equal(7 + 64, R("urlop:1", "a").Suma.Length);
    }

    [Fact]
    public void Tresc_zaczyna_sie_od_id_i_sumy_i_zachowuje_polskie_znaki()
    {
        var json = R("urlop:1", "żądanie").Json();
        Assert.StartsWith("{\"id\":\"urlop:1\",\"suma\":\"sha256:", json);
        Assert.Contains("żądanie", json);
    }
}

public class KalendarzTesty
{
    [Fact]
    public void Dekoduje_blob_little_endian()
    {
        // Styczen 2026: 1.01 swieto (9999), 2.01 roboczy (480), 3.01 wolny (0), reszta robocza.
        var sty = new byte[62];
        void Dzien(int d, int v) { sty[2 * d - 2] = (byte)(v & 0xFF); sty[2 * d - 1] = (byte)(v >> 8); }
        for (int d = 1; d <= 31; d++) Dzien(d, 480);
        Dzien(1, 9999); Dzien(3, 0);
        var mies = new byte[]?[12]; mies[0] = sty;

        var dni = KalendarzR2.DniNierobocze(new LataKalWiersz(2, 2026, mies));

        Assert.Equal(new[] { new DateTime(2026, 1, 1), new DateTime(2026, 1, 3) }, dni);
    }

    [Fact]
    public void Luty_nie_czyta_poza_liczbe_dni_miesiaca()
    {
        var lut = new byte[62];   // 31 "dni" zer — ale luty 2026 ma 28
        var mies = new byte[]?[12]; mies[1] = lut;
        Assert.Equal(28, KalendarzR2.DniNierobocze(new LataKalWiersz(2, 2026, mies)).Count);
    }
}

public class MagazynStanuTesty : IDisposable
{
    readonly string _plik = Path.Combine(Path.GetTempPath(), $"wu-test-{Guid.NewGuid():N}.db");
    readonly MagazynStanu _m;

    public MagazynStanuTesty() { _m = new MagazynStanu(_plik); }

    public void Dispose()
    {
        _m.Dispose();
        foreach (var f in new[] { _plik, _plik + "-wal", _plik + "-shm" }) if (File.Exists(f)) File.Delete(f);
    }

    static Rekord R(string zbior, string id, string v = "x") =>
        new(zbior, id, new[] { new KeyValuePair<string, JsonNode?>("v", v) });

    static List<Rekord> Wiele(string zbior, int ile) => Enumerable.Range(1, ile).Select(i => R(zbior, $"urlop:{i}")).ToList();

    [Fact]
    public void Pierwszy_odczyt_wszystko_nowe_po_potwierdzeniu_nic()
    {
        var rek = Wiele(Zbiory.Nieobecnosc, 3);
        var r1 = _m.Porownaj("1", Zbiory.Nieobecnosc, rek);
        Assert.Equal(3, r1.Nowe);
        Assert.Equal(3, r1.DoWyslania.Count);

        _m.Potwierdz("1", Zbiory.Nieobecnosc, r1.DoWyslania, r1.Usuniete);
        var r2 = _m.Porownaj("1", Zbiory.Nieobecnosc, rek);
        Assert.Empty(r2.DoWyslania);
        Assert.Equal(3, r2.BezZmian);
    }

    [Fact]
    public void Bez_potwierdzenia_stan_sie_nie_zmienia()   // paczka zerwana — wysylamy ponownie
    {
        var rek = Wiele(Zbiory.Nieobecnosc, 2);
        _m.Porownaj("1", Zbiory.Nieobecnosc, rek);
        Assert.Equal(2, _m.Porownaj("1", Zbiory.Nieobecnosc, rek).Nowe);
    }

    [Fact]
    public void Zmiana_i_usuniecie()
    {
        var rek = Wiele(Zbiory.Nieobecnosc, 3);
        _m.Potwierdz("1", Zbiory.Nieobecnosc, rek, Array.Empty<string>());

        var teraz = new List<Rekord> { rek[0], R(Zbiory.Nieobecnosc, "urlop:2", "inna tresc") };   // 2 zmieniony, 3 usuniety
        var r = _m.Porownaj("1", Zbiory.Nieobecnosc, teraz);

        Assert.Equal(1, r.Zmienione);
        Assert.Equal(new[] { "urlop:3" }, r.Usuniete);
        Assert.False(r.UsunieciaWstrzymane);
    }

    [Fact]
    public void Bezpiecznik_wstrzymuje_masowe_usuniecia_nieobecnosci()
    {
        var rek = Wiele(Zbiory.Nieobecnosc, 100);
        _m.Potwierdz("1", Zbiory.Nieobecnosc, rek, Array.Empty<string>());

        var r = _m.Porownaj("1", Zbiory.Nieobecnosc, new List<Rekord>());   // np. zla baza — pusty odczyt

        Assert.True(r.UsunieciaWstrzymane);
        Assert.Empty(r.Usuniete);
        Assert.Equal(100, r.UsunieciaWykryte);
    }

    static Rekord Rg(string zbior, string id, string grupa, DateTime? koniec = null) =>
        new(zbior, id, new[] { new KeyValuePair<string, JsonNode?>("v", "x") }, grupa, koniec);

    [Fact]
    public void Bezpiecznik_nie_blokuje_limitow_po_zmianie_roku()
    {
        // 60 limitow za 2025 i 60 za 2026; w styczniu 2027 okno zaczyna sie 1.01.2026 — rok 2025 wypada.
        var rek = Enumerable.Range(1, 60).Select(i => Rg(Zbiory.Limit, $"{i}:2025:wypoczynkowy", $"{i}", new DateTime(2025, 12, 31)))
            .Concat(Enumerable.Range(1, 60).Select(i => Rg(Zbiory.Limit, $"{i}:2026:wypoczynkowy", $"{i}", new DateTime(2026, 12, 31))))
            .ToList();
        _m.Potwierdz("1", Zbiory.Limit, rek, Array.Empty<string>());
        var zakres = new ZakresPorownania(Enumerable.Range(1, 60).Select(i => $"{i}").ToHashSet(), new DateTime(2026, 1, 1));

        var r = _m.Porownaj("1", Zbiory.Limit, rek.Skip(60).ToList(), zakres: zakres);

        Assert.False(r.UsunieciaWstrzymane);
        Assert.Equal(60, r.Usuniete.Count);
        Assert.Equal(0, r.UsunieciaNiewyjasnione);
    }

    [Fact]
    public void Zwolnienie_pracownika_z_wieloma_nieobecnosciami_nie_uruchamia_bezpiecznika()
    {
        // Pracownik 7 ma 40 nieobecnosci (wiecej niz prog 20); pozostali 10 × 10.
        var rek = Enumerable.Range(1, 40).Select(i => Rg(Zbiory.Nieobecnosc, $"urlop:7{i:000}", "7"))
            .Concat(Enumerable.Range(1, 100).Select(i => Rg(Zbiory.Nieobecnosc, $"urlop:{i}", $"{i % 10}")))
            .ToList();
        _m.Potwierdz("1", Zbiory.Nieobecnosc, rek, Array.Empty<string>());
        var zakres = new ZakresPorownania(Enumerable.Range(0, 10).Where(i => i != 7).Select(i => $"{i}").ToHashSet(), new DateTime(2025, 1, 1));

        var r = _m.Porownaj("1", Zbiory.Nieobecnosc, rek.Skip(40).Where(x => x.Grupa != "7").ToList(), zakres: zakres);

        Assert.False(r.UsunieciaWstrzymane);
        Assert.Equal(50, r.Usuniete.Count);           // 40 + 10 z grupy 7 w drugiej puli
        Assert.Equal(0, r.UsunieciaNiewyjasnione);
    }

    [Fact]
    public void Gwaltowny_spadek_wstrzymuje_nawet_wyjasnione_usuniecia()
    {
        // Zla baza: znikaja wszyscy pracownicy — kazde usuniecie „wyjasnione", ale to nie jest zwolnienie 36 osob naraz.
        var rek = Enumerable.Range(1, 36).Select(i => Rg(Zbiory.Pracownik, $"{i}", $"{i}")).ToList();
        _m.Potwierdz("1", Zbiory.Pracownik, rek, Array.Empty<string>());

        var r = _m.Porownaj("1", Zbiory.Pracownik, new List<Rekord>(),
                            zakres: new ZakresPorownania(new HashSet<string>(), new DateTime(2025, 1, 1)));

        Assert.True(r.UsunieciaWstrzymane);
        Assert.Empty(r.Usuniete);
    }

    [Fact]
    public void Niewyjasnione_usuniecia_nieobecnosci_pracownika_w_zakresie_wstrzymane()
    {
        var rek = Enumerable.Range(1, 100).Select(i => Rg(Zbiory.Nieobecnosc, $"urlop:{i}", "1", new DateTime(2026, 6, 1))).ToList();
        _m.Potwierdz("1", Zbiory.Nieobecnosc, rek, Array.Empty<string>());
        var zakres = new ZakresPorownania(new HashSet<string> { "1" }, new DateTime(2025, 1, 1));

        var r = _m.Porownaj("1", Zbiory.Nieobecnosc, rek.Take(70).ToList(), zakres: zakres);   // 30 urlopow znika u pracujacej osoby

        Assert.True(r.UsunieciaWstrzymane);
        Assert.Equal(30, r.UsunieciaNiewyjasnione);
    }

    [Fact]
    public void Tryb_pelny_wysyla_takze_niezmienione()
    {
        var rek = Wiele(Zbiory.Pracownik, 5);
        _m.Potwierdz("1", Zbiory.Pracownik, rek, Array.Empty<string>());
        Assert.Equal(5, _m.Porownaj("1", Zbiory.Pracownik, rek, trybPelny: true).DoWyslania.Count);
    }

    [Fact]
    public void Firmy_sa_rozdzielone()
    {
        _m.Potwierdz("111", Zbiory.Pracownik, Wiele(Zbiory.Pracownik, 5), Array.Empty<string>());
        Assert.Equal(5, _m.Porownaj("222", Zbiory.Pracownik, Wiele(Zbiory.Pracownik, 5)).Nowe);
    }

    [Fact]
    public void Kursor_zapamietany()
    {
        Assert.Null(_m.Kursor("1", Zbiory.Limit));
        _m.UstawKursor("1", Zbiory.Limit, "c-875");
        Assert.Equal("c-875", _m.Kursor("1", Zbiory.Limit));
    }
}

public class BudowniczyTesty
{
    static readonly byte[] Sekret = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    static readonly DateTime Dzis = new(2026, 9, 19);

    static SuroweDane Dane(IReadOnlyList<UrlopWiersz>? urlopy = null, IReadOnlyList<ZasilekWiersz>? zasilki = null) => new(
        new FirmaWiersz("1234567890", "FIRMA"),
        new[] { new PracownikWiersz(207, "KOWALSKI", "JAN", null, false, null, false, true, false, false, "90010112345") },
        new[] { new ZatrudnienieWiersz(207, 207, true, true, 2, 3, 4, new DateTime(2020, 1, 1), null) },
        urlopy ?? Array.Empty<UrlopWiersz>(),
        Array.Empty<KadroweWiersz>(),
        zasilki ?? Array.Empty<ZasilekWiersz>(),
        new[] { new SlownikWiersz("urlop", 5, "urlop wyp.na żądanie"), new SlownikWiersz("urlop", 6, "ekwiwalent") },
        Array.Empty<KalendarzWiersz>(), Array.Empty<LataKalWiersz>(), DateTime.Now);

    static JsonObject Jeden(WynikBudowy w, string zbior) => Assert.Single(w.Zbiory[zbior]).Tresc;

    [Fact]
    public void Aktualna_umowa_o_prace_bez_limitu_daje_naliczony_false()
    {
        var w = BudowniczyRekordow.Zbuduj(Dane(), new UstawieniaBudowy(6, Sekret, Dzis));
        var lim = Jeden(w, Zbiory.Limit);
        Assert.Equal("207:2026:wypoczynkowy", (string)lim["id"]!);
        Assert.False((bool)lim["naliczony"]!);
        Assert.Null(lim["pozostaloMin"]);
        Assert.True(lim.ContainsKey("pozostaloMin"));   // klucz obecny, wartosc null
    }

    static WynikBudowy Zbuduj(SuroweDane d) => BudowniczyRekordow.Zbuduj(d, new UstawieniaBudowy(6, Sekret, Dzis));

    [Fact]
    public void Zwolniony_nie_wychodzi_wcale()
    {
        var d = Dane();
        d = d with { Pracownicy = new[] { d.Pracownicy[0] with { Zwolniony = true, DataZwolnienia = new DateTime(2020, 12, 31) } } };
        var w = Zbuduj(d);
        Assert.Empty(w.Zbiory[Zbiory.Pracownik]);
        Assert.Empty(w.Zbiory[Zbiory.Zatrudnienie]);
        Assert.Empty(w.Zbiory[Zbiory.Limit]);
        Assert.Empty(w.GrupyWZakresie);
    }

    [Fact]
    public void Na_wypowiedzeniu_z_data_w_przyszlosci_nadal_w_zakresie()
    {
        var d = Dane();
        d = d with { Pracownicy = new[] { d.Pracownicy[0] with { Zwolniony = true, DataZwolnienia = new DateTime(2026, 10, 31) } } };
        var p = Jeden(Zbuduj(d), Zbiory.Pracownik);
        Assert.True((bool)p["zwolniony"]!);
        Assert.Equal("2026-10-31", (string)p["dataZwolnienia"]!);
    }

    [Fact]
    public void Ostatni_dzien_pracy_jeszcze_w_zakresie_nastepny_juz_nie()
    {
        var p = new PracownikWiersz(1, "A", "B", null, true, new DateTime(2026, 9, 19), false, true, false, false, null);
        Assert.True(BudowniczyRekordow.WZakresie(p, new DateTime(2026, 9, 19)));
        Assert.False(BudowniczyRekordow.WZakresie(p, new DateTime(2026, 9, 20)));
    }

    [Fact]
    public void Zleceniobiorca_poza_zakresem()
    {
        var d = Dane();
        d = d with { Pracownicy = new[] { d.Pracownicy[0] with { UmowaOPrace = false, UmowaZlecenie = true } } };
        Assert.Empty(Zbuduj(d).Zbiory[Zbiory.Pracownik]);
    }

    [Fact]
    public void Historyczne_zatrudnienie_pracujacej_osoby_nie_wychodzi()
    {
        var d = Dane();
        d = d with { Zatrudnienia = d.Zatrudnienia.Append(new ZatrudnienieWiersz(150, 207, false, false, null, null, null, null, null)).ToList() };
        Assert.Equal("207", Assert.Single(Zbuduj(d).Zbiory[Zbiory.Zatrudnienie]).Id);
    }

    [Fact]
    public void Nieobecnosci_sprzed_okna_nie_wychodza()
    {
        var u = new[]
        {
            new UrlopWiersz(1, 207, false, 0, new(2024, 12, 30), new(2024, 12, 31), 2, 960, null, null, false, null, null, 3, null, null),
            new UrlopWiersz(2, 207, false, 0, new(2024, 12, 30), new(2025, 1, 2), 3, 1440, null, null, false, null, null, 3, null, null),
            new UrlopWiersz(3, 207, false, 0, new(2025, 7, 1), new(2025, 7, 2), 2, 960, null, null, false, null, null, 3, null, null),
        };
        var ids = Zbuduj(Dane(u)).Zbiory[Zbiory.Nieobecnosc].Select(r => r.Id).ToList();
        Assert.Equal(new[] { "urlop:2", "urlop:3" }, ids);   // okno od 1.01.2025; urlop 2 konczy sie juz w oknie
    }

    [Fact]
    public void Etat_dziesietny_i_identyfikatory_jako_string()
    {
        var z = Jeden(BudowniczyRekordow.Zbuduj(Dane(), new UstawieniaBudowy(6, Sekret, Dzis)), Zbiory.Zatrudnienie);
        Assert.Equal(0.75, (double)z["etat"]!);
        Assert.Equal("207", (string)z["pracownikId"]!);
        Assert.Equal("2", (string)z["kalendarzId"]!);
    }

    [Fact]
    public void OsobaId_to_HMAC_a_PESEL_nie_wychodzi()
    {
        var w = BudowniczyRekordow.Zbuduj(Dane(), new UstawieniaBudowy(6, Sekret, Dzis));
        string json = w.Zbiory[Zbiory.Pracownik][0].Json();
        Assert.DoesNotContain("90010112345", json);
        Assert.Contains(BudowniczyRekordow.OsobaId(Sekret, "90010112345"), json);
    }

    [Fact]
    public void Wpis_connectora_rozpoznany_po_X_F7_a_requestId_z_OPIS()
    {
        var u = new[]
        {
            new UrlopWiersz(12330, 207, false, 0, new(2026, 10, 29), new(2026, 10, 31), 2, 960, null, null, false, null, "wu-01J9ZD11M2A8", 6, null, null),
            new UrlopWiersz(12331, 207, false, 0, new(2026, 12, 1), new(2026, 12, 1), 1, 480, null, null, false, null, "URLOP-KADR-1", 3, null, null),
        };
        var n = BudowniczyRekordow.Zbuduj(Dane(u), new UstawieniaBudowy(6, Sekret, Dzis)).Zbiory[Zbiory.Nieobecnosc];

        Assert.Equal("connector", (string)n[0].Tresc["zrodloWpisu"]!);
        Assert.Equal("wu-01J9ZD11M2A8", (string)n[0].Tresc["requestId"]!);
        Assert.Equal("kadry", (string)n[1].Tresc["zrodloWpisu"]!);
        Assert.Null(n[1].Tresc["requestId"]);
    }

    [Fact]
    public void Zasilek_bez_minut_z_kontynuacja()
    {
        var z = new[] { new ZasilekWiersz(512, 207, 0, new(2026, 9, 14), new(2026, 9, 18), 5, true, null, null) };
        var n = Jeden(BudowniczyRekordow.Zbuduj(Dane(zasilki: z), new UstawieniaBudowy(6, Sekret, Dzis)), Zbiory.Nieobecnosc);
        Assert.Equal("zasilek:512", (string)n["id"]!);
        Assert.Null(n["minuty"]);
        Assert.True((bool)n["kontynuacja"]!);
    }

    [Fact]
    public void Slownik_flagi()
    {
        var s = BudowniczyRekordow.Zbuduj(Dane(), new UstawieniaBudowy(6, Sekret, Dzis)).Zbiory[Zbiory.Slownik];
        Assert.True((bool)s[0].Tresc["doWniosku"]!);      // na zadanie
        Assert.False((bool)s[1].Tresc["nieobecnosc"]!);   // ekwiwalent
    }

    [Fact]
    public void Za_krotki_sekret_odrzucony()
        => Assert.Throws<ArgumentException>(() => BudowniczyRekordow.Zbuduj(Dane(), new UstawieniaBudowy(6, new byte[4], Dzis)));
}

