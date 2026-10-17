using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AtrapaTenanta;

/// <summary>
/// Atrapa API tenanta wg API-TENANTA-v1.md i PRZYKLADY-API-TENANTA.md (ksztalt wiazacy).
/// Zachowuje sie jak zapowiedzial tenant, lacznie z bledami: 401, 403, 409, 413, 422, 429/503.
/// Wejscia /_atrapa/* (bez klucza) sluza testom: dodawanie zadan, awarie, podglad stanu.
/// </summary>
public static class Serwer
{
    public const int MaksRekordow = 500;
    public const int MaksBajtow = 2 * 1024 * 1024;
    public const int MaksRozpakowanych = 8 * 1024 * 1024;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    static readonly string[] ZbioryProtokolu = { "firma", "pracownik", "zatrudnienie", "limit", "nieobecnosc", "slownik", "kalendarz" };

    public static WebApplication Zbuduj(string[] args, StanAtrapy stan)
    {
        var b = WebApplication.CreateBuilder(args);
        b.Logging.SetMinimumLevel(LogLevel.Warning);
        var app = b.Build();
        Mapuj(app, stan);
        return app;
    }

    public static void Mapuj(WebApplication app, StanAtrapy s)
    {
        app.MapPost("/api/v1/sync/start", (HttpContext c) => Obsluz(c, s, true, Start));
        app.MapPost("/api/v1/sync/dane", (HttpContext c) => Obsluz(c, s, true, Dane));
        app.MapPost("/api/v1/sync/przeglad", (HttpContext c) => Obsluz(c, s, true, Przeglad));
        app.MapGet("/api/v1/sync/zadania", (HttpContext c) => Obsluz(c, s, false, Zadania));
        app.MapGet("/api/v1/sync/stan", (HttpContext c) => Obsluz(c, s, false, Stan));
        app.MapPost("/api/v1/sync/wyniki", (HttpContext c) => Obsluz(c, s, true, Wyniki));
        app.MapPost("/api/v1/sync/koniec", (HttpContext c) => Obsluz(c, s, true, Koniec));
        app.MapPost("/api/v1/sync/zgloszenie-firmy", (HttpContext c) => Obsluz(c, s, true, Zgloszenie));
        MapujSterowanie(app, s);
    }

    // ================================================================== potok wspolny

    sealed record Odp(int Status, JsonNode Tresc, int? RetryAfter = null);

    delegate Odp Obsluga(StanAtrapy s, JsonObject? cialo, HttpContext c);

    static async Task Obsluz(HttpContext c, StanAtrapy s, bool post, Obsluga obsluga)
    {
        Odp odp;
        string? kluczIdem = post ? c.Request.Headers["X-Idempotency-Key"].FirstOrDefault() : null;

        (int Status, int? RetryAfter)? awaria = null;
        lock (s.Zamek)
        {
            s.Dziennik.Add($"{c.Request.Method} {c.Request.Path}");
            if (s.Awarie.TryDequeue(out var aw)) awaria = aw;
            else if (s.LimitNaMinute > 0 && !Miesci(s.Zadania, s.LimitNaMinute, TimeSpan.FromMinutes(1))) awaria = (429, 30);
            else if (s.LimitZgloszenNaGodzine > 0 && c.Request.Path.Value!.EndsWith("zgloszenie-firmy")
                     && !Miesci(s.Zgloszeniaczas, s.LimitZgloszenNaGodzine, TimeSpan.FromHours(1))) awaria = (429, 600);
        }
        if (awaria is { } a)
        {
            await Zapisz(c, new Odp(a.Status, Blad(a.Status == 429 ? "ZA_SZYBKO" : a.Status == 503 ? "PRZERWA_TECHNICZNA" : "AWARIA", "Awaria wstrzyknieta"), a.RetryAfter));
            return;
        }

        // uwierzytelnienie: Authorization: Bearer, a gdy go brak — X-Api-Key
        string? auth = c.Request.Headers.Authorization.FirstOrDefault();
        string? klucz = auth is not null && auth.StartsWith("Bearer ") ? auth[7..] : c.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (klucz != s.Klucz) { await Zapisz(c, new Odp(401, Blad("KLUCZ_NIEPRAWIDLOWY", "Nieprawidlowy klucz instalacji"))); return; }

        // Tura 7: naglowek informacyjny — brak = wersja 1; 409 tylko przy wartosci niezgodnej.
        string? protokol = c.Request.Headers["X-Protokol"].FirstOrDefault();
        if (protokol is not null && protokol != "1")
        {
            var b = Blad("WERSJA_PROTOKOLU", "Nieobslugiwana wersja");
            b["blad"]!["obslugiwane"] = new JsonArray(1);
            await Zapisz(c, new Odp(409, b));
            return;
        }

        JsonObject? cialo = null;
        if (post)
        {
            var (tresc, blad) = await CzytajCialo(c);
            if (blad is not null) { await Zapisz(c, blad); return; }
            try { cialo = JsonNode.Parse(tresc!) as JsonObject; }
            catch (JsonException) { await Zapisz(c, new Odp(400, Blad("JSON_NIEPOPRAWNY", "Nieczytelny JSON"))); return; }
            if (cialo is null) { await Zapisz(c, new Odp(400, Blad("JSON_NIEPOPRAWNY", "Oczekiwano obiektu"))); return; }
        }

        lock (s.Zamek)
        {
            if (kluczIdem is not null && s.Idempotencja.TryGetValue(kluczIdem, out var zap))
                odp = new Odp(zap.Status, JsonNode.Parse(zap.Tresc)!);
            else
            {
                odp = obsluga(s, cialo, c);
                if (kluczIdem is not null && odp.Status < 500 && odp.Status != 429)
                    s.Idempotencja[kluczIdem] = (odp.Status, odp.Tresc.ToJsonString(Json));
            }
        }
        await Zapisz(c, odp);
    }

    /// <summary>Okno przesuwne limitu (tura 7: 120 zadan/min na klucz, 10 zgloszen firmy/h).</summary>
    static bool Miesci(Queue<DateTime> kolejka, int limit, TimeSpan okno)
    {
        var teraz = DateTime.UtcNow;
        while (kolejka.Count > 0 && teraz - kolejka.Peek() >= okno) kolejka.Dequeue();
        if (kolejka.Count >= limit) return false;
        kolejka.Enqueue(teraz);
        return true;
    }

    static async Task<(string?, Odp?)> CzytajCialo(HttpContext c)
    {
        using var ms = new MemoryStream();
        Stream wej = c.Request.Body;
        bool gzip = string.Equals(c.Request.Headers.ContentEncoding.FirstOrDefault(), "gzip", StringComparison.OrdinalIgnoreCase);
        if (gzip) wej = new GZipStream(wej, CompressionMode.Decompress);

        var bufor = new byte[81920];
        int n;
        while ((n = await wej.ReadAsync(bufor)) > 0)
        {
            ms.Write(bufor, 0, n);
            if (ms.Length > MaksRozpakowanych) return (null, new Odp(413, Maks("Tresc po rozpakowaniu przekracza 8 MB")));
        }
        if (ms.Length > MaksBajtow) return (null, new Odp(413, Maks("Tresc przekracza 2 MB")));
        return (Encoding.UTF8.GetString(ms.ToArray()), null);
    }

    static JsonObject Maks(string k)
    {
        var b = Blad("PACZKA_ZA_DUZA", k);
        b["blad"]!["maks"] = new JsonObject { ["rekordy"] = MaksRekordow, ["bajty"] = MaksBajtow };
        return b;
    }

    static async Task Zapisz(HttpContext c, Odp o)
    {
        c.Response.StatusCode = o.Status;
        if (o.RetryAfter is int ra) c.Response.Headers.RetryAfter = ra.ToString();
        c.Response.ContentType = "application/json; charset=utf-8";
        await c.Response.WriteAsync(o.Tresc.ToJsonString(Json));
    }

    static JsonObject Blad(string kod, string komunikat) => new()
    {
        ["blad"] = new JsonObject { ["kod"] = kod, ["komunikat"] = komunikat, ["zdarzenieId"] = Guid.NewGuid().ToString("N")[..8] },
    };

    /// <summary>NIP i przebieg z ciala albo zapytania; 403 dla firmy spoza klucza, 409 dla obcego przebiegu.</summary>
    static Odp? Kontekst(StanAtrapy s, string? nip, string? przebiegId, bool wymagajPrzebiegu, out StanFirmy f, out Przebieg? p)
    {
        f = null!; p = null;
        if (string.IsNullOrEmpty(nip)) return new Odp(400, Blad("POLE_WYMAGANE", "nip"));
        // Tura 8: NIP z bledna suma kontrolna → 422 (nigdy 403 — literowka nie moze trafic do zgloszen).
        if (!WuConnector.Core.Konfiguracja.Nip.CzyPoprawny(nip)) return new Odp(422, Blad("NIP_NIEPOPRAWNY", "Numer NIP jest niepoprawny"));
        if (!s.DozwoloneNip.Contains(nip)) return new Odp(403, Blad("FIRMA_NIEZATWIERDZONA", "Firma nie zostala zatwierdzona przez administratora"));
        f = s.Firma(nip);
        if (!wymagajPrzebiegu) return null;
        if (przebiegId is null || !f.Przebiegi.TryGetValue(przebiegId, out p) || p.Zamkniety)
            return new Odp(409, Blad("PRZEBIEG_NIEZNANY", "Nieznany albo zamkniety przebieg"));
        return null;
    }

    static string? Str(JsonObject? o, string k) => o?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : null;

    // ================================================================== operacje

    /// <summary>
    /// GET /sync/stan (zmiana protokolu z 20.09.2026): stan klucza i firmy bez otwierania przebiegu.
    /// `nip` opcjonalny — bez niego sprawdzamy sam klucz.
    /// </summary>
    static Odp Stan(StanAtrapy s, JsonObject? _, HttpContext c)
    {
        string? nip = c.Request.Query["nip"];
        JsonObject? firma = null;
        string? ostatni = null;

        if (!string.IsNullOrEmpty(nip))
        {
            if (!WuConnector.Core.Konfiguracja.Nip.CzyPoprawny(nip))
                return new Odp(422, Blad("NIP_NIEPOPRAWNY", "Numer NIP jest niepoprawny"));
            string stanFirmy = s.DozwoloneNip.Contains(nip) ? "ok"
                             : s.OdrzuconeNip.Contains(nip) ? "firma_poza_zakresem"
                             : s.Zgloszenia.ContainsKey(nip) ? "firma_zgloszona"
                             : "firma_nieznana";
            firma = new JsonObject { ["stan"] = stanFirmy };
            if (stanFirmy == "ok" && s.Firma(nip).OstatniPrzebiegZamkniety is DateTimeOffset t)
                ostatni = t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        return new Odp(200, new JsonObject
        {
            ["protokol"] = 1,
            ["klucz"] = "aktywny",
            ["firma"] = firma,
            ["ostatniPrzebieg"] = ostatni,
        });
    }

    static Odp Start(StanAtrapy s, JsonObject? b, HttpContext c)
    {
        if (Kontekst(s, Str(b, "nip"), null, false, out var f, out _) is Odp blad) return blad;
        f.Starty.Add((JsonObject)b!.DeepClone());

        // Bezpiecznik z 20.09.2026: pierwszy odcisk zapamietujemy cicho, inny zatrzymuje strumien tej firmy.
        if (Str(b, "odciskTozsamosci") is string odcisk)
        {
            if (f.OdciskTozsamosci is null) f.OdciskTozsamosci = odcisk;
            else if (f.OdciskTozsamosci != odcisk)
            {
                var t = Blad("TOZSAMOSC_ZMIENIONA", "Odcisk tozsamosci inny niz zapamietany dla tej firmy");
                ((JsonObject)t["blad"]!)["odcisk"] = odcisk;
                return new Odp(403, t);
            }
        }

        var kursoryConn = b["kursory"] as JsonObject;
        var zbiory = new JsonObject();
        foreach (var z in ZbioryProtokolu)
        {
            f.Kursory.TryGetValue(z, out var ks);
            string? kc = Str(kursoryConn, z);
            bool pelny = ks is null || kc != ks || f.WymusPelny.Contains(z);
            zbiory[z] = new JsonObject { ["kursor"] = ks, ["tryb"] = pelny ? "pelny" : "zmiany" };
        }

        var id = Guid.NewGuid().ToString();
        f.Przebiegi[id] = new Przebieg { Id = id };
        return new Odp(200, new JsonObject
        {
            ["przebiegId"] = id,
            ["zbiory"] = zbiory,
            ["przegladKontrolny"] = f.PrzegladKontrolny,
            ["rozmiarPaczki"] = MaksRekordow,
            ["wycofane"] = new JsonArray(f.Wycofane.Select(w => (JsonNode?)w).ToArray()),
        });
    }

    static Odp Dane(StanAtrapy s, JsonObject? b, HttpContext c)
    {
        if (Kontekst(s, Str(b, "nip"), Str(b, "przebiegId"), true, out var f, out _) is Odp blad) return blad;
        string? zbior = Str(b, "zbior");
        if (zbior is null || !SchematRekordow.Zbiory.ContainsKey(zbior)) return new Odp(422, Blad("ZBIOR_NIEZNANY", zbior ?? "(brak)"));
        if (b!["rekordy"] is not JsonArray rek) return new Odp(422, Blad("POLE_WYMAGANE", "rekordy"));
        if (rek.Count > MaksRekordow) return new Odp(413, Maks($"Paczka ma {rek.Count} rekordow"));

        var bledy = new List<SchematRekordow.Blad>();
        int nieznane = 0;
        foreach (var n in rek)
        {
            if (n is not JsonObject r) { bledy.Add(new("(nie obiekt)", "ZLY_TYP", "rekord")); continue; }
            var (bl, nz) = SchematRekordow.Sprawdz(zbior, r);
            bledy.AddRange(bl);
            nieznane += nz;
        }
        if (bledy.Count > 0)
        {
            var o = Blad("REKORDY_NIEPOPRAWNE", "Paczka zawiera rekordy niezgodne ze schematem");
            o["blad"]!["pozycje"] = new JsonArray(bledy.Select(x => (JsonNode?)new JsonObject
            {
                ["id"] = x.Id, ["kod"] = x.Kod, ["pole"] = x.Pole,
            }).ToArray());
            return new Odp(422, o);
        }

        // Paczka = transakcja: dopiero po pelnej walidacji
        var d = f.Zbior(zbior);
        foreach (JsonObject r in rek.Cast<JsonObject>())
        {
            string id = Str(r, "id")!;
            if (r["usuniety"] is JsonValue u && u.GetValue<bool>()) d.Remove(id);
            else d[id] = (JsonObject)r.DeepClone();
        }
        f.PominietePola += nieznane;
        return new Odp(200, new JsonObject { ["przyjeto"] = rek.Count, ["kursor"] = f.NowyKursor(zbior), ["pominietePola"] = nieznane });
    }

    static Odp Przeglad(StanAtrapy s, JsonObject? b, HttpContext c)
    {
        if (Kontekst(s, Str(b, "nip"), Str(b, "przebiegId"), true, out var f, out var p) is Odp blad) return blad;
        string? zbior = Str(b, "zbior");
        if (zbior is null || !SchematRekordow.Zbiory.ContainsKey(zbior)) return new Odp(422, Blad("ZBIOR_NIEZNANY", zbior ?? "(brak)"));
        if (b!["pozycje"] is not JsonArray poz) return new Odp(422, Blad("POLE_WYMAGANE", "pozycje"));
        if (poz.Count > MaksRekordow) return new Odp(413, Maks($"Paczka ma {poz.Count} pozycji"));

        if (!p!.PrzegladId.TryGetValue(zbior, out var widziane)) p.PrzegladId[zbior] = widziane = new();
        var d = f.Zbior(zbior);
        foreach (JsonObject x in poz.Cast<JsonObject>())
        {
            string id = Str(x, "id")!;
            widziane.Add(id);
            if (!d.TryGetValue(id, out var mam) || Str(mam, "suma") != Str(x, "suma")) p.Rozjazd = true;
        }

        // Usuniecia domykamy WYLACZNIE po paczce "ostatnia" tego przebiegu.
        if (b["ostatnia"] is JsonValue o && o.GetValue<bool>())
            foreach (var id in d.Keys.Where(k => !widziane.Contains(k)).ToList()) { d.Remove(id); p.Rozjazd = true; }

        return new Odp(200, new JsonObject { ["przyjeto"] = poz.Count, ["kursor"] = f.NowyKursor(zbior) });
    }

    static Odp Zadania(StanAtrapy s, JsonObject? cialo, HttpContext c)
    {
        var q = c.Request.Query;
        if (Kontekst(s, q["nip"], q["przebiegId"], true, out var f, out _) is Odp blad) return blad;
        int limit = int.TryParse(q["limit"], out var l) ? Math.Clamp(l, 1, 100) : 20;

        var teraz = DateTime.UtcNow;
        var wydane = f.Zadania
            .Where(z => !f.Wycofane.Contains(z.RequestId))
            .Where(z => z.Stan == StanZadania.Oczekuje || (z.Stan == StanZadania.Wydane && z.WydaneDo < teraz))
            .Take(limit).ToList();
        foreach (var z in wydane) { z.Stan = StanZadania.Wydane; z.WydaneDo = teraz + s.Rezerwacja; z.Wydan++; }

        return new Odp(200, new JsonObject
        {
            ["zadania"] = new JsonArray(wydane.Select(z => (JsonNode?)z.Tresc.DeepClone()).ToArray()),
        });
    }

    static Odp Wyniki(StanAtrapy s, JsonObject? b, HttpContext c)
    {
        if (Kontekst(s, Str(b, "nip"), Str(b, "przebiegId"), true, out var f, out _) is Odp blad) return blad;
        if (b!["wyniki"] is not JsonArray wyn) return new Odp(422, Blad("POLE_WYMAGANE", "wyniki"));

        foreach (JsonObject w in wyn.Cast<JsonObject>())
        {
            var z = f.Zadania.FirstOrDefault(x => x.RequestId == Str(w, "requestId"));
            if (z is null) continue;
            z.Wynikow++;
            if (z.Stan == StanZadania.Zamkniete) continue;   // powtorzony wynik nie zmienia stanu
            z.Wynik = (JsonObject)w.DeepClone();
            if (Str(w, "status") == "bladPrzejsciowy") z.Stan = StanZadania.Oczekuje;
            else { z.Stan = StanZadania.Zamkniete; f.Wycofane.Remove(z.RequestId); }
        }
        return new Odp(200, new JsonObject { ["przyjeto"] = wyn.Count });
    }

    static Odp Koniec(StanAtrapy s, JsonObject? b, HttpContext c)
    {
        if (Kontekst(s, Str(b, "nip"), Str(b, "przebiegId"), true, out var f, out var p) is Odp blad) return blad;
        f.Konce.Add((JsonObject)b!.DeepClone());
        p!.Zamkniety = true;
        f.OstatniPrzebiegZamkniety = DateTimeOffset.UtcNow;
        bool rozjazd = p.Rozjazd && p.PrzegladId.Count > 0;
        f.WymusPelny.Clear();
        if (rozjazd) foreach (var z in p.PrzegladId.Keys) f.WymusPelny.Add(z);
        return new Odp(200, new JsonObject { ["nastepnyInterwal"] = s.NastepnyInterwal, ["pelnyPrzegladWymagany"] = rozjazd });
    }

    static Odp Zgloszenie(StanAtrapy s, JsonObject? b, HttpContext c)
    {
        string? nip = Str(b, "nip");
        if (nip is null) return new Odp(400, Blad("POLE_WYMAGANE", "nip"));
        if (!WuConnector.Core.Konfiguracja.Nip.CzyPoprawny(nip)) return new Odp(422, Blad("NIP_NIEPOPRAWNY", "Numer NIP jest niepoprawny"));
        // Tura 8: trzy stany zgloszenia.
        if (s.DozwoloneNip.Contains(nip)) return new Odp(200, new JsonObject { ["status"] = "zatwierdzona" });
        if (s.OdrzuconeNip.Contains(nip)) return new Odp(200, new JsonObject { ["status"] = "odrzucona" });
        s.Zgloszenia[nip] = Str(b, "nazwa") ?? "";
        return new Odp(202, new JsonObject { ["status"] = "oczekuje" });
    }

    // ================================================================== sterowanie testami

    static void MapujSterowanie(WebApplication app, StanAtrapy s)
    {
        app.MapPost("/_atrapa/zadanie", (JsonObject b) =>
        {
            lock (s.Zamek)
            {
                var z = (JsonObject)b["zadanie"]!.DeepClone();
                s.Firma(Str(b, "nip")!).Zadania.Add(new Zadanie { RequestId = Str(z, "requestId")!, Tresc = z });
            }
            return Results.Ok();
        });
        app.MapPost("/_atrapa/wycofaj", (JsonObject b) => { lock (s.Zamek) s.Firma(Str(b, "nip")!).Wycofane.Add(Str(b, "requestId")!); return Results.Ok(); });
        app.MapPost("/_atrapa/awaria", (JsonObject b) =>
        {
            lock (s.Zamek)
                for (int i = 0; i < ((int?)b["ile"] ?? 1); i++) s.Awarie.Enqueue(((int)b["status"]!, (int?)b["retryAfter"]));
            return Results.Ok();
        });
        app.MapPost("/_atrapa/odrzuc", (JsonObject b) => { lock (s.Zamek) { s.OdrzuconeNip.Add(Str(b, "nip")!); s.Zgloszenia.Remove(Str(b, "nip")!); } return Results.Ok(); });
        app.MapPost("/_atrapa/zatwierdz", (JsonObject b) => { lock (s.Zamek) { s.DozwoloneNip.Add(Str(b, "nip")!); s.Zgloszenia.Remove(Str(b, "nip")!); } return Results.Ok(); });
        app.MapPost("/_atrapa/przeglad", (JsonObject b) => { lock (s.Zamek) s.Firma(Str(b, "nip")!).PrzegladKontrolny = (bool)b["wlacz"]!; return Results.Ok(); });
        app.MapPost("/_atrapa/pelny", (JsonObject b) => { lock (s.Zamek) foreach (var z in ZbioryProtokolu) s.Firma(Str(b, "nip")!).WymusPelny.Add(z); return Results.Ok(); });
        app.MapPost("/_atrapa/ustawienia", (JsonObject b) =>
        {
            lock (s.Zamek)
            {
                if (b["interwal"] is JsonValue i) s.NastepnyInterwal = i.GetValue<int>();
                if (b["rezerwacjaSekund"] is JsonValue r) s.Rezerwacja = TimeSpan.FromSeconds(r.GetValue<int>());
                if (b["limitNaMinute"] is JsonValue lm) s.LimitNaMinute = lm.GetValue<int>();
                if (b["limitZgloszenNaGodzine"] is JsonValue lz) s.LimitZgloszenNaGodzine = lz.GetValue<int>();
            }
            return Results.Ok();
        });
        app.MapGet("/_atrapa/stan", (string nip) =>
        {
            lock (s.Zamek)
            {
                var f = s.Firma(nip);
                return Results.Json(new
                {
                    rekordy = f.Rekordy.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                    kursory = f.Kursory,
                    zadania = f.Zadania.Select(z => new { z.RequestId, stan = z.Stan.ToString(), z.Wydan, z.Wynikow, wynik = z.Wynik?.ToJsonString() }),
                    wycofane = f.Wycofane,
                    starty = f.Starty.Count,
                    konce = f.Konce.Count,
                    ostatniStart = f.Starty.LastOrDefault()?.ToJsonString(),
                    ostatniKoniec = f.Konce.LastOrDefault()?.ToJsonString(),
                    pominietePola = f.PominietePola,
                    zgloszenia = s.Zgloszenia,
                    dziennik = s.Dziennik.TakeLast(50),
                });
            }
        });
        app.MapGet("/_atrapa/rekord", (string nip, string zbior, string id) =>
        {
            lock (s.Zamek)
                return s.Firma(nip).Zbior(zbior).TryGetValue(id, out var r) ? Results.Text(r.ToJsonString(), "application/json") : Results.NotFound();
        });
    }
}

