using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WuConnector.Contracts
{
    public static class WorkerJson
    {
        /// <summary>
        /// Prefiks linii z wynikiem. Wszystko inne na stdout usluga ignoruje —
        /// na wypadek, gdyby biblioteka R2 cos wypisala.
        /// </summary>
        public const string Znacznik = "WU-WYNIK:";

        public static readonly JsonSerializerOptions Opcje = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };

        public static string Serializuj<T>(T obj) => JsonSerializer.Serialize(obj, Opcje);

        public static T Deserializuj<T>(string json) => JsonSerializer.Deserialize<T>(json, Opcje);

        /// <summary>Wyszukuje linie wyniku w pelnym wyjsciu procesu. Null, gdy jej nie ma.</summary>
        public static WynikWorkera OdczytajWynik(string stdout)
        {
            if (string.IsNullOrEmpty(stdout)) return null;
            foreach (var linia in stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                if (linia.StartsWith(Znacznik, StringComparison.Ordinal))
                    return Deserializuj<WynikWorkera>(linia.Substring(Znacznik.Length));
            return null;
        }
    }
}
