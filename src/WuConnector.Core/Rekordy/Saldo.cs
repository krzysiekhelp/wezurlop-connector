using WuConnector.Core.Odczyt;

namespace WuConnector.Core.Rekordy;

public sealed record WynikSalda(
    bool Naliczony,
    int? WymiarMin,
    int? ZaleglyMin,
    int? DostepneNaStartMin,
    int? WykorzystaneMin,
    int? KorektyMin,
    int? PozostaloMin,
    int? SaldoNarastajaceR2);

/// <summary>
/// Saldo urlopu z karty urlopowej — formula z USTALENIA 22.12 + 22.19. Wszystko w minutach.
///
///   pozostalo = POZOSTALO_GODZ pierwszego (po OD, X_I) wiersza limitu roku
///             + Σ LIMIT_GODZ pozostalych wierszy limitu roku        (doliczenia stazowe, urlop narastajacy)
///             − Σ IL_GODZ okresow roku
///             + Σ KOR_POZOST_GODZ okresow roku z KOR_POZOST = 1    (reczne korekty kadr, +/−)
///
/// Kolumna dniowa POZOSTALO jest niewiarygodna — nieuzywana.
/// </summary>
public static class Saldo
{
    /// <summary>Rodzaje zuzywajace pule wypoczynkowego: 0 wypoczynkowy, 5 na zadanie.</summary>
    public static readonly int[] RodzajeWypoczynkowe = { 0, 5 };
    public static readonly int[] RodzajeOpieki = { 2 };

    /// <param name="wiersze">Wszystkie wiersze KART_URL jednego zatrudnienia.</param>
    /// <param name="rodzajLimitu">R_DNIA wiersza limitu (0 wypoczynkowy, 2 opieka art. 188).</param>
    /// <param name="rodzajeZuzycia">R_DNIA okresow zuzywajacych ten limit.</param>
    public static WynikSalda Policz(IEnumerable<UrlopWiersz> wiersze, int rok, int rodzajLimitu, IReadOnlyCollection<int> rodzajeZuzycia)
    {
        var roku = wiersze.Where(w => w.Od.HasValue && w.Od.Value.Year == rok).ToList();

        var limity = roku.Where(w => w.L && w.RDnia == rodzajLimitu)
                         .OrderBy(w => w.Od).ThenBy(w => w.Id)
                         .ToList();
        var okresy = roku.Where(w => !w.L && rodzajeZuzycia.Contains(w.RDnia)).ToList();

        int wykorzystane = okresy.Sum(w => w.IlGodz ?? 0);
        int korekty = okresy.Where(w => w.KorPozost).Sum(w => w.KorPozostGodz ?? 0);
        int? narastajace = SaldoNarastajace(okresy);

        if (limity.Count == 0)
            return new WynikSalda(false, null, null, null, null, null, null, narastajace);

        var pierwszy = limity[0];
        int start = pierwszy.PozostaloGodz ?? 0;
        int doliczone = limity.Skip(1).Sum(w => w.LimitGodz ?? 0);
        int wymiar = limity.Sum(w => w.LimitGodz ?? 0);
        int dostepne = start + doliczone;
        // Zalegly = to, co ponad wymiar bylo dostepne na start pierwszego limitu.
        int zalegly = start - (pierwszy.LimitGodz ?? 0);

        return new WynikSalda(true, wymiar, zalegly, dostepne, wykorzystane, korekty,
                              dostepne - wykorzystane + korekty, narastajace);
    }

    /// <summary>
    /// Kontrola: saldo po ostatnim okresie roku, jak liczy je samo R2 (POZOSTALO_GODZ na wierszu L=0).
    /// Z ostatniego OKRESU, nie limitu — limit zalozony po urlopie z ta sama data psuje kolejnosc (22.19).
    /// </summary>
    static int? SaldoNarastajace(List<UrlopWiersz> okresy)
        => okresy.OrderBy(w => w.Od).ThenBy(w => w.Id).LastOrDefault()?.PozostaloGodz;
}
