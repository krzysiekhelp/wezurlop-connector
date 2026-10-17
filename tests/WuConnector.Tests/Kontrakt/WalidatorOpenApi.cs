using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WuConnector.Tests.Kontrakt;

/// <summary>
/// Minimalny walidator schematow OpenAPI 3.0 (podzbior uzywany w openapi-sync-v1.json tenanta):
/// $ref, type, required, nullable, properties, additionalProperties, items, maxItems, minLength, maxLength,
/// pattern, enum, format date/date-time, anyOf, allOf. Dodatkowo zglasza pola spoza "properties"
/// — tenant je ignoruje i liczy jako pominietePola, a my chcemy tam zero.
/// </summary>
public sealed class WalidatorOpenApi
{
    readonly JsonObject _schematy;

    public WalidatorOpenApi(string sciezka)
        => _schematy = JsonNode.Parse(File.ReadAllText(sciezka))!["components"]!["schemas"]!.AsObject();

    public static WalidatorOpenApi Tenanta() => new(Path.Combine(AppContext.BaseDirectory, "Kontrakt", "openapi-sync-v1.json"));

    /// <summary>Bledy walidacji wartosci wzgledem schematu o danej nazwie (pusta lista = zgodna).</summary>
    public List<string> Sprawdz(string schemat, JsonNode? wartosc)
    {
        var bledy = new List<string>();
        Sprawdz(_schematy[schemat]!.AsObject(), wartosc, "$", bledy);
        return bledy;
    }

    JsonObject Rozwin(JsonObject s)
        => s["$ref"] is JsonValue r ? Rozwin(_schematy[((string)r!).Split('/')[^1]]!.AsObject()) : s;

    void Sprawdz(JsonObject s, JsonNode? v, string sciezka, List<string> bledy)
    {
        s = Rozwin(s);
        if (v is null)
        {
            if (s["nullable"]?.GetValue<bool>() != true && s["allOf"] is null) bledy.Add($"{sciezka}: null niedozwolony");
            return;
        }
        if (s["allOf"] is JsonArray all)
        {
            foreach (var a in all) Sprawdz(a!.AsObject(), v, sciezka, bledy);
            return;
        }
        if (s["anyOf"] is JsonArray any)
        {
            var proby = any.Select(a => { var b = new List<string>(); Sprawdz(a!.AsObject(), v, sciezka, b); return b; }).ToList();
            if (proby.All(b => b.Count > 0)) bledy.AddRange(proby.OrderBy(b => b.Count).First());
            return;
        }

        string? typ = (string?)s["type"];
        switch (typ)
        {
            case "object":
                if (v is not JsonObject o) { bledy.Add($"{sciezka}: oczekiwano obiektu"); return; }
                var wlasnosci = s["properties"]?.AsObject();
                foreach (var w in s["required"]?.AsArray() ?? new JsonArray())
                    if (!o.ContainsKey((string)w!) || o[(string)w!] is null) bledy.Add($"{sciezka}.{w}: pole wymagane");
                foreach (var (k, x) in o)
                {
                    if (wlasnosci?[k] is JsonObject ps) Sprawdz(ps, x, $"{sciezka}.{k}", bledy);
                    else if (wlasnosci is not null) bledy.Add($"{sciezka}.{k}: pole spoza schematu (pominietePola)");
                }
                break;
            case "array":
                if (v is not JsonArray arr) { bledy.Add($"{sciezka}: oczekiwano tablicy"); return; }
                if (s["maxItems"] is JsonValue mi && arr.Count > (int)mi) bledy.Add($"{sciezka}: ponad {mi} elementow");
                if (s["items"] is JsonObject it)
                    for (int i = 0; i < arr.Count; i++) Sprawdz(it, arr[i], $"{sciezka}[{i}]", bledy);
                break;
            case "string":
                if (v is not JsonValue sv || !sv.TryGetValue(out string? t)) { bledy.Add($"{sciezka}: oczekiwano tekstu"); return; }
                if (s["maxLength"] is JsonValue ml && t.Length > (int)ml) bledy.Add($"{sciezka}: dłuższe niż {ml}");
                if (s["minLength"] is JsonValue mn && t.Length < (int)mn) bledy.Add($"{sciezka}: krótsze niż {mn}");
                if (s["pattern"] is JsonValue p && !Regex.IsMatch(t, (string)p!)) bledy.Add($"{sciezka}: niezgodne ze wzorcem {p}");
                if ((string?)s["format"] == "date" && !DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    bledy.Add($"{sciezka}: nie jest datą YYYY-MM-DD ({t})");
                if ((string?)s["format"] == "date-time" && !Regex.IsMatch(t, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$"))
                    bledy.Add($"{sciezka}: nie jest ISO 8601 ze strefą ({t})");
                if (s["enum"] is JsonArray e && !e.Any(x => (string?)x == t)) bledy.Add($"{sciezka}: wartość {t} spoza listy");
                break;
            case "integer":
                if (v is not JsonValue iv || !iv.TryGetValue(out long l)) { bledy.Add($"{sciezka}: oczekiwano liczby całkowitej"); return; }
                if (s["enum"] is JsonArray ie && !ie.Any(x => (long)x! == l)) bledy.Add($"{sciezka}: wartość {l} spoza listy");
                break;
            case "number":
                if (v is not JsonValue nv || !nv.TryGetValue(out double _)) bledy.Add($"{sciezka}: oczekiwano liczby");
                break;
            case "boolean":
                if (v is not JsonValue bv || !bv.TryGetValue(out bool _)) bledy.Add($"{sciezka}: oczekiwano true/false");
                break;
        }
    }
}
