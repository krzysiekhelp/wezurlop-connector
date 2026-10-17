using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AtrapaTenanta;

/// <summary>
/// Walidacja rekordow tak, jak zapowiedzial tenant: brak pola wymaganego albo zly typ → 422 dla calej paczki;
/// pole nieznane → ignorowane i liczone w pominietePola. Lista pol: KSZTALT-DANYCH-R2.md v0.7.
/// </summary>
public static class SchematRekordow
{
    public enum Typ { Tekst, Calkowita, Liczba, Logiczna, Data, Znacznik, Obiekt, Tablica }

    public sealed record Pole(string Nazwa, Typ Typ, bool Wymagane);

    static Pole W(string n, Typ t) => new(n, t, true);
    static Pole O(string n, Typ t) => new(n, t, false);

    public static readonly IReadOnlyDictionary<string, Pole[]> Zbiory = new Dictionary<string, Pole[]>
    {
        ["firma"] = new[] { W("nazwa", Typ.Tekst) },
        ["pracownik"] = new[]
        {
            O("osobaId", Typ.Tekst), W("nazwisko", Typ.Tekst), W("imie", Typ.Tekst), O("identyfikator", Typ.Tekst),
            W("zwolniony", Typ.Logiczna), O("dataZwolnienia", Typ.Data), W("skasowany", Typ.Logiczna), W("umowy", Typ.Obiekt),
        },
        ["zatrudnienie"] = new[]
        {
            W("pracownikId", Typ.Tekst), W("aktualne", Typ.Logiczna), W("domyslne", Typ.Logiczna), O("etat", Typ.Liczba),
            O("dataRozpoczecia", Typ.Data), O("dataZakonczenia", Typ.Data), O("kalendarzId", Typ.Tekst),
        },
        ["limit"] = new[]
        {
            W("zatrudnienieId", Typ.Tekst), W("rok", Typ.Calkowita), W("rodzaj", Typ.Tekst), W("naliczony", Typ.Logiczna),
            O("wymiarMin", Typ.Calkowita), O("zaleglyMin", Typ.Calkowita), O("dostepneNaStartMin", Typ.Calkowita),
            O("wykorzystaneMin", Typ.Calkowita), O("korektyMin", Typ.Calkowita), O("pozostaloMin", Typ.Calkowita),
            O("naZadanieWykorzystaneDni", Typ.Calkowita),
        },
        ["nieobecnosc"] = new[]
        {
            W("zatrudnienieId", Typ.Tekst), W("rodzaj", Typ.Obiekt), W("dataOd", Typ.Data), W("dataDo", Typ.Data),
            O("dni", Typ.Calkowita), O("minuty", Typ.Calkowita), O("requestId", Typ.Tekst), W("zrodloWpisu", Typ.Tekst),
            O("kontynuacja", Typ.Logiczna), O("utworzono", Typ.Znacznik), O("zmieniono", Typ.Znacznik),
        },
        ["slownik"] = new[]
        {
            W("zrodlo", Typ.Tekst), W("kod", Typ.Calkowita), W("nazwa", Typ.Tekst),
            W("tylkoOdczyt", Typ.Logiczna), W("doWniosku", Typ.Logiczna), W("nieobecnosc", Typ.Logiczna),
            O("obciazaLimit", Typ.Tekst), O("obciazaLimitZrodlo", Typ.Tekst),
        },
        ["kalendarz"] = new[]
        {
            W("kalendarzId", Typ.Tekst), W("nazwa", Typ.Tekst), W("rok", Typ.Calkowita),
            W("indywidualne", Typ.Logiczna), W("dniNierobocze", Typ.Tablica),
            O("normaUrlopowaMin", Typ.Calkowita), O("normaDobowaMin", Typ.Calkowita),
        },
    };

    public sealed record Blad(string Id, string Kod, string Pole);

    /// <summary>Zwraca bledy (puste = rekord poprawny) i liczbe pol nieznanych.</summary>
    public static (List<Blad> Bledy, int Nieznane) Sprawdz(string zbior, JsonObject r)
    {
        var bledy = new List<Blad>();
        string id = r["id"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "(brak id)";

        if (id == "(brak id)") { bledy.Add(new(id, "POLE_WYMAGANE", "id")); return (bledy, 0); }

        if (r["usuniety"] is JsonValue u && u.TryGetValue<bool>(out var us) && us)
            return (bledy, r.Count(kv => kv.Key is not ("id" or "usuniety")));

        if (r["suma"] is not JsonValue sv || !sv.TryGetValue<string>(out var suma) || !suma.StartsWith("sha256:") || suma.Length != 71)
            bledy.Add(new(id, "POLE_WYMAGANE", "suma"));

        var pola = Zbiory[zbior];
        foreach (var p in pola)
        {
            if (!r.TryGetPropertyValue(p.Nazwa, out var wart) || wart is null)
            {
                if (p.Wymagane) bledy.Add(new(id, "POLE_WYMAGANE", p.Nazwa));
                continue;
            }
            if (!ZgodnyTyp(wart, p.Typ)) bledy.Add(new(id, "ZLY_TYP", p.Nazwa));
        }

        var znane = pola.Select(p => p.Nazwa).Append("id").Append("suma").ToHashSet();
        return (bledy, r.Count(kv => !znane.Contains(kv.Key)));
    }

    static bool ZgodnyTyp(JsonNode n, Typ t)
    {
        switch (t)
        {
            case Typ.Obiekt: return n is JsonObject;
            case Typ.Tablica: return n is JsonArray;
        }
        if (n is not JsonValue v) return false;
        var k = v.GetValueKind();
        return t switch
        {
            Typ.Tekst => k == JsonValueKind.String,
            Typ.Logiczna => k is JsonValueKind.True or JsonValueKind.False,
            Typ.Calkowita => k == JsonValueKind.Number && v.TryGetValue<long>(out _) && !v.ToJsonString().Contains('.'),
            Typ.Liczba => k == JsonValueKind.Number,
            Typ.Data => k == JsonValueKind.String
                        && DateTime.TryParseExact(v.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            Typ.Znacznik => k == JsonValueKind.String && DateTimeOffset.TryParse(v.GetValue<string>(), CultureInfo.InvariantCulture, out _),
            _ => false,
        };
    }
}
