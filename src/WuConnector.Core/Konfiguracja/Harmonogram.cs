using System.Globalization;

namespace WuConnector.Core.Konfiguracja;

/// <summary>
/// Harmonogram synchronizacji (okno: Polaczenie → Harmonogram). Jeden cykl = odczyt R2 + wymiana z wezurlop
/// (zmiany kadrowe w jedna strone, zaakceptowane urlopy w druga).
///
///   w godzinach pracy   — co <see cref="OdstepMinut"/> (minimum 5 min, decyzja uzytkownika 19.09.2026),
///   poza nimi           — co <see cref="OdstepPozaMinut"/> albo wcale (<see cref="WstrzymajPoza"/>);
///                         connector budzi sie wtedy sam na poczatku najblizszego okna pracy.
/// Serwer moze odstep wydluzyc (nastepnyInterwal, Retry-After), nigdy skrocic ponizej harmonogramu.
/// </summary>
public sealed class Harmonogram
{
    public const int MinimalnyOdstepMinut = 5;

    public int OdstepMinut { get; set; } = 5;
    public string GodzinaOd { get; set; } = "06:00";
    public string GodzinaDo { get; set; } = "20:00";

    /// <summary>Dni pracy: 1 = poniedzialek … 7 = niedziela.</summary>
    public List<int> Dni { get; set; } = new() { 1, 2, 3, 4, 5 };

    public int OdstepPozaMinut { get; set; } = 60;
    public bool WstrzymajPoza { get; set; }

    public static Harmonogram Domyslny() => new();

    /// <summary>Po ilu czasie nastepny cykl, liczac od chwili <paramref name="teraz"/>.</summary>
    public TimeSpan Odstep(DateTime teraz)
    {
        var w = TimeSpan.FromMinutes(Math.Max(MinimalnyOdstepMinut, OdstepMinut));
        if (WOknie(teraz)) return w;

        var start = NastepnyStartOkna(teraz);
        var doStartu = start is DateTime s ? s - teraz : TimeSpan.MaxValue;
        if (WstrzymajPoza)
            return start is null ? TimeSpan.FromHours(1) : doStartu;   // brak dni pracy — sprawdzamy co godzine, czy to sie zmienilo
        var poza = TimeSpan.FromMinutes(Math.Max(MinimalnyOdstepMinut, OdstepPozaMinut));
        return doStartu < poza ? doStartu : poza;                        // nie przesypiamy poczatku dnia pracy
    }

    public bool WOknie(DateTime t)
    {
        if (!Dni.Contains(Dzien(t))) return false;
        var (od, @do) = (Godzina(GodzinaOd), Godzina(GodzinaDo));
        var g = t.TimeOfDay;
        return od <= @do ? g >= od && g < @do : g >= od || g < @do;     // okno przez polnoc, np. 22:00–06:00
    }

    public DateTime? NastepnyStartOkna(DateTime teraz)
    {
        var od = Godzina(GodzinaOd);
        for (int i = 0; i <= 7; i++)
        {
            var d = teraz.Date.AddDays(i);
            var start = d + od;
            if (start > teraz && Dni.Contains(Dzien(d))) return start;
        }
        return null;
    }

    /// <summary>Czytelny opis do okna i dziennika, np. "Pn–Pt 06:00–20:00 co 5 min, poza tym co 60 min".</summary>
    public string Opis()
    {
        string[] n = { "", "Pn", "Wt", "Śr", "Cz", "Pt", "So", "Nd" };
        var dni = Dni.OrderBy(x => x).ToList();
        string d = dni.Count == 7 ? "codziennie"
                 : dni.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) ? "Pn–Pt"
                 : string.Join(", ", dni.Select(x => n[x]));
        string poza = WstrzymajPoza ? "poza tym wstrzymana" : $"poza tym co {Math.Max(MinimalnyOdstepMinut, OdstepPozaMinut)} min";
        return $"{d} {GodzinaOd}–{GodzinaDo} co {Math.Max(MinimalnyOdstepMinut, OdstepMinut)} min, {poza}";
    }

    public string? Sprawdz()
    {
        if (OdstepMinut < MinimalnyOdstepMinut) return $"Odstęp w godzinach pracy nie może być krótszy niż {MinimalnyOdstepMinut} minut.";
        if (!WstrzymajPoza && OdstepPozaMinut < MinimalnyOdstepMinut) return $"Odstęp poza godzinami pracy nie może być krótszy niż {MinimalnyOdstepMinut} minut.";
        if (!TimeSpan.TryParseExact(GodzinaOd, @"hh\:mm", CultureInfo.InvariantCulture, out _)
            || !TimeSpan.TryParseExact(GodzinaDo, @"hh\:mm", CultureInfo.InvariantCulture, out _)) return "Godziny pracy w formacie GG:MM.";
        if (Dni.Count == 0 && WstrzymajPoza) return "Brak dni pracy i wstrzymanie poza godzinami — connector nigdy by się nie zsynchronizował.";
        return null;
    }

    static int Dzien(DateTime t) => t.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)t.DayOfWeek;

    static TimeSpan Godzina(string s)
        => TimeSpan.TryParseExact(s, @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.Zero;
}
