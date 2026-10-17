using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WuConnector.Core.Konfiguracja;

/// <summary>
/// Jedna sekcja konfiguracji przygotowana przez okno do zapisu. W odroznieniu od domkniecia (lambdy) da sie ja
/// zapisac do pliku i przekazac drugiemu procesowi — oknu z podniesionymi uprawnieniami, ktore samo czyta
/// aktualna konfiguracje i nanosi na nia wylacznie te sekcje.
///
/// Sekrety (klucz, hasla, sekret osobaId) sa tu juz zaszyfrowane DPAPI zakresu maszyny — takie same jak w pliku
/// konfiguracji, wiec przekazanie nie odslania ich bardziej niz sam plik.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ZmianaSekcji
{
    public enum Rodzaje { Polaczenie, Harmonogram, Instalacja, Sekret }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required Rodzaje Rodzaj { get; set; }

    // --- Polaczenie
    public string? AdresSerwera { get; set; }
    public string? KluczApi { get; set; }

    // --- Harmonogram
    public Harmonogram? Harmonogram { get; set; }

    // --- Instalacja: rozpoznawana po katalogu BIN; Instalacja == null oznacza usuniecie z konfiguracji.
    public string? KatalogBin { get; set; }
    public PlikKonfiguracji.InstalacjaWPliku? Instalacja { get; set; }

    // --- Sekret osobaId (przenosiny maszyny)
    public string? SekretOsoby { get; set; }

    public static ZmianaSekcji DlaPolaczenia(string adres, string kluczZaszyfrowany) => new()
    { Rodzaj = Rodzaje.Polaczenie, AdresSerwera = adres, KluczApi = kluczZaszyfrowany };

    public static ZmianaSekcji DlaHarmonogramu(Harmonogram h) => new()
    { Rodzaj = Rodzaje.Harmonogram, Harmonogram = h };

    public static ZmianaSekcji DlaInstalacji(string katalogBin, PlikKonfiguracji.InstalacjaWPliku? instalacja) => new()
    { Rodzaj = Rodzaje.Instalacja, KatalogBin = katalogBin, Instalacja = instalacja };

    public static ZmianaSekcji DlaSekretu(string sekretZaszyfrowany) => new()
    { Rodzaj = Rodzaje.Sekret, SekretOsoby = sekretZaszyfrowany };

    /// <summary>Nanosi sekcje na konfiguracje wczytana z dysku. Reszta pliku zostaje nietknieta.</summary>
    public void Zastosuj(PlikKonfiguracji p)
    {
        switch (Rodzaj)
        {
            case Rodzaje.Polaczenie:
                p.AdresSerwera = AdresSerwera ?? "";
                p.KluczApi = KluczApi ?? "";
                break;

            case Rodzaje.Harmonogram:
                p.Harmonogram = Harmonogram;
                break;

            case Rodzaje.Instalacja:
                int i = p.Instalacje.FindIndex(x => x.KatalogBin.Equals(KatalogBin, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) { if (Instalacja is null) p.Instalacje.RemoveAt(i); else p.Instalacje[i] = Instalacja; }
                else if (Instalacja is not null) p.Instalacje.Add(Instalacja);
                break;

            case Rodzaje.Sekret:
                p.SekretOsoby = SekretOsoby ?? throw new InvalidOperationException("Zmiana sekretu bez sekretu.");
                break;
        }
    }

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public void Zapisz(string sciezka)
        => File.WriteAllText(sciezka, JsonSerializer.Serialize(this, Json), new UTF8Encoding(false));

    public static ZmianaSekcji Wczytaj(string sciezka)
        => JsonSerializer.Deserialize<ZmianaSekcji>(File.ReadAllText(sciezka, Encoding.UTF8), Json)
           ?? throw new InvalidDataException("Pusty plik zmiany sekcji.");
}
