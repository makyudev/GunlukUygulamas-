using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using GunlukUygulamasi.Models;

namespace GunlukUygulamasi.Services;

public sealed class WeatherService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Location KayseriLocation = new("Kayseri", 38.7312, 35.4787);

    public async Task<WeatherSnapshot?> GetWeatherAsync(
        string? manualCity,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var location = string.IsNullOrWhiteSpace(manualCity)
                ? await GetLocationFromIpAsync(cancellationToken)
                : await GetLocationByCityAsync(manualCity.Trim(), cancellationToken);
            if (location is null)
            {
                return null;
            }

            var latitude = location.Latitude.ToString(CultureInfo.InvariantCulture);
            var longitude = location.Longitude.ToString(CultureInfo.InvariantCulture);
            var weatherUri = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}" +
                             $"&longitude={longitude}&current=temperature_2m,weather_code&timezone=auto";
            using var weatherResponse = await HttpClient.GetAsync(weatherUri, cancellationToken);
            weatherResponse.EnsureSuccessStatusCode();
            using var weatherDocument = JsonDocument.Parse(
                await weatherResponse.Content.ReadAsStreamAsync(cancellationToken));

            if (!weatherDocument.RootElement.TryGetProperty("current", out var current) ||
                !current.TryGetProperty("temperature_2m", out var temperatureElement) ||
                !current.TryGetProperty("weather_code", out var codeElement) ||
                !temperatureElement.TryGetDouble(out var temperature) ||
                !codeElement.TryGetInt32(out var code))
            {
                return null;
            }

            return new WeatherSnapshot(location.City, temperature, code, GetWeatherDescription(code));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    private static async Task<Location?> GetLocationFromIpAsync(CancellationToken cancellationToken)
    {
        try
        {
            var location = await GetLocationFromIpWhoIsAsync(cancellationToken);
            if (location is not null)
            {
                return location;
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            Trace.TraceWarning("ipwho.is konum sorgusu başarısız oldu: {0}", exception.Message);
        }

        try
        {
            using var response = await HttpClient.GetAsync("http://ip-api.com/json/", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() == "success" &&
                root.TryGetProperty("city", out var city) &&
                root.TryGetProperty("lat", out var latitude) &&
                root.TryGetProperty("lon", out var longitude) &&
                !string.IsNullOrWhiteSpace(city.GetString()) &&
                latitude.TryGetDouble(out var latitudeValue) &&
                longitude.TryGetDouble(out var longitudeValue))
            {
                return new Location(city.GetString()!, latitudeValue, longitudeValue);
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            Trace.TraceWarning("ip-api.com konum sorgusu başarısız oldu: {0}", exception.Message);
        }

        return KayseriLocation;
    }

    private static async Task<Location?> GetLocationFromIpWhoIsAsync(CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync("https://ipwho.is/", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        if ((root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False) ||
            !root.TryGetProperty("city", out var city) ||
            !root.TryGetProperty("latitude", out var latitude) ||
            !root.TryGetProperty("longitude", out var longitude) ||
            string.IsNullOrWhiteSpace(city.GetString()) ||
            !latitude.TryGetDouble(out var latitudeValue) ||
            !longitude.TryGetDouble(out var longitudeValue))
        {
            return null;
        }

        return new Location(city.GetString()!, latitudeValue, longitudeValue);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KisiselGunlugum/1.0");
        return client;
    }

    private static async Task<Location?> GetLocationByCityAsync(string cityName, CancellationToken cancellationToken)
    {
        if (cityName.Contains("kayseri", StringComparison.OrdinalIgnoreCase))
        {
            return KayseriLocation;
        }

        var uri = "https://geocoding-api.open-meteo.com/v1/search?name=" +
                  Uri.EscapeDataString(cityName) + "&count=5&language=tr&format=json";
        try
        {
            using var response = await HttpClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array ||
                results.GetArrayLength() == 0)
            {
                return KayseriLocation;
            }

            var result = results[0];
            foreach (var candidate in results.EnumerateArray())
            {
                if (candidate.TryGetProperty("country_code", out var countryCode) &&
                    countryCode.ValueKind == JsonValueKind.String &&
                    string.Equals(countryCode.GetString(), "TR", StringComparison.OrdinalIgnoreCase))
                {
                    result = candidate;
                    break;
                }
            }

            if (TryReadGeocodingResult(result, out var location))
            {
                return location;
            }

            return KayseriLocation;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            Trace.TraceWarning("Open-Meteo şehir araması başarısız oldu: {0}", exception.Message);
            return KayseriLocation;
        }
    }

    private static bool TryReadGeocodingResult(JsonElement result, out Location location)
    {
        location = KayseriLocation;
        if (!result.TryGetProperty("name", out var name) ||
            name.ValueKind != JsonValueKind.String ||
            !result.TryGetProperty("latitude", out var latitude) ||
            !result.TryGetProperty("longitude", out var longitude) ||
            string.IsNullOrWhiteSpace(name.GetString()) ||
            !latitude.TryGetDouble(out var latitudeValue) ||
            !longitude.TryGetDouble(out var longitudeValue))
        {
            return false;
        }

        location = new Location(name.GetString()!, latitudeValue, longitudeValue);
        return true;
    }

    private static string GetWeatherDescription(int code) => code switch
    {
        0 => "Açık",
        1 => "Çoğunlukla Açık",
        2 => "Parçalı Bulutlu",
        3 => "Kapalı",
        45 or 48 => "Sisli",
        51 or 53 or 55 => "Çiseleme",
        56 or 57 => "Dondurucu Çiseleme",
        61 or 63 or 65 => "Yağmurlu",
        66 or 67 => "Dondurucu Yağmur",
        71 or 73 or 75 or 77 => "Karlı",
        80 or 81 or 82 => "Sağanak Yağışlı",
        85 or 86 => "Kar Sağanağı",
        95 => "Gök Gürültülü Fırtına",
        96 or 99 => "Dolu Fırtınası",
        _ => "Hava Durumu"
    };

    private sealed record Location(string City, double Latitude, double Longitude);
}

public sealed record WeatherSnapshot(string City, double TemperatureC, int WeatherCode, string Description)
{
    public string Badge => $"{City} · {TemperatureC:0.#}°C · {Description}";
    public string TemperatureLabel => $"{TemperatureC:0.#}°C";
}
