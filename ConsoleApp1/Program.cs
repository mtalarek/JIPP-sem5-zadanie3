using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Http;
using System.Text.Json.Serialization;

var builder = Host.CreateApplicationBuilder();

builder.Services.AddOptions<OpenMeteoApiClientOptions>()
    .BindConfiguration("HttpClients:OpenMeteoApiClient")
    .ValidateOnStart();

builder.Services.AddHttpClient<OpenMeteoApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OpenMeteoApiClientOptions>>();
    client.BaseAddress = new Uri(options.Value.BaseAddress);
});

var app = builder.Build();

var client = app.Services.GetRequiredService<OpenMeteoApiClient>();

var forecast = await client.GetForecastAsync();

Console.WriteLine("=== CURRENT WEATHER ===");
Console.WriteLine($"Temperature: {forecast.Current.Temperature2m} °C");
Console.WriteLine($"Wind speed : {forecast.Current.WindSpeed10m} km/h");

Console.WriteLine();
Console.WriteLine("=== HOURLY (first 5 hours) ===");

for (int i = 0; i < 5; i++)
{
    Console.WriteLine(
        $"{forecast.Hourly.Time[i]} | " +
        $"Temp: {forecast.Hourly.Temperature2m[i]} °C | " +
        $"Humidity: {forecast.Hourly.RelativeHumidity2m[i]} % | " +
        $"Wind: {forecast.Hourly.WindSpeed10m[i]} km/h"
    );
}

public class OpenMeteoApiClient(HttpClient httpClient, ILogger<OpenMeteoApiClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ForecastResponse> GetForecastAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync(
            "v1/forecast" +
            "?latitude=52.52" +
            "&longitude=13.41" +
            "&current=temperature_2m,wind_speed_10m" +
            "&hourly=temperature_2m,relative_humidity_2m,wind_speed_10m",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to get weather forecast");
            throw new HttpRequestException("Open-Meteo API error");
        }

        logger.LogInformation("Successfully retrieved weather forecast");

        return (await response.Content.ReadFromJsonAsync<ForecastResponse>(
                   JsonOptions, cancellationToken))!;
    }
}

public class ForecastResponse
{
    [JsonPropertyName("current")]
    public required CurrentWeather Current { get; set; }

    [JsonPropertyName("hourly")]
    public required HourlyWeather Hourly { get; set; }
}

public class CurrentWeather
{
    [JsonPropertyName("temperature_2m")]
    public required double Temperature2m { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public required double WindSpeed10m { get; set; }
}

public class HourlyWeather
{
    [JsonPropertyName("time")]
    public required string[] Time { get; set; }

    [JsonPropertyName("temperature_2m")]
    public required double[] Temperature2m { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public required int[] RelativeHumidity2m { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public required double[] WindSpeed10m { get; set; }
}

public class OpenMeteoApiClientOptions
{
    public required string BaseAddress { get; set; }
    public required double Latitude { get; set; }
    public required double Longitude { get; set; }
}