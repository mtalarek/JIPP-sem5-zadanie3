//using System.Net.Http.Json;
//using System.Text.Json;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Options;
//using Microsoft.Extensions.Http;
//using System.Text.Json.Serialization;

//var builder = Host.CreateApplicationBuilder();

//builder.Services.AddOptions<OpenMeteoApiClientOptions>()
//    .BindConfiguration("HttpClients:OpenMeteoApiClient")
//    .ValidateOnStart();

//builder.Services.AddHttpClient<OpenMeteoApiClient>((sp, client) =>
//{
//    var options = sp.GetRequiredService<IOptions<OpenMeteoApiClientOptions>>();
//    client.BaseAddress = new Uri(options.Value.BaseAddress);
//});

//var app = builder.Build();

//var client = app.Services.GetRequiredService<OpenMeteoApiClient>();

//var forecast = await client.GetForecastAsync();

//Console.WriteLine("=== CURRENT WEATHER ===");
//Console.WriteLine($"Temperature: {forecast.Current.Temperature2m} °C");
//Console.WriteLine($"Wind speed : {forecast.Current.WindSpeed10m} km/h");

//Console.WriteLine();
//Console.WriteLine("=== HOURLY (first 5 hours) ===");

//for (int i = 0; i < 5; i++)
//{
//    Console.WriteLine(
//        $"{forecast.Hourly.Time[i]} | " +
//        $"Temp: {forecast.Hourly.Temperature2m[i]} °C | " +
//        $"Humidity: {forecast.Hourly.RelativeHumidity2m[i]} % | " +
//        $"Wind: {forecast.Hourly.WindSpeed10m[i]} km/h"
//    );
//}

//public class OpenMeteoApiClient(HttpClient httpClient, ILogger<OpenMeteoApiClient> logger)
//{
//    private static readonly JsonSerializerOptions JsonOptions = new()
//    {
//        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
//    };

//    public async Task<ForecastResponse> GetForecastAsync(
//        CancellationToken cancellationToken = default)
//    {
//        var response = await httpClient.GetAsync(
//            "v1/forecast" +
//            "?latitude=52.52" +
//            "&longitude=13.41" +
//            "&current=temperature_2m,wind_speed_10m" +
//            "&hourly=temperature_2m,relative_humidity_2m,wind_speed_10m",
//            cancellationToken);

//        if (!response.IsSuccessStatusCode)
//        {
//            logger.LogWarning("Failed to get weather forecast");
//            throw new HttpRequestException("Open-Meteo API error");
//        }

//        logger.LogInformation("Successfully retrieved weather forecast");

//        return (await response.Content.ReadFromJsonAsync<ForecastResponse>(
//                   JsonOptions, cancellationToken))!;
//    }
//}

//public class ForecastResponse
//{
//    [JsonPropertyName("current")]
//    public required CurrentWeather Current { get; set; }

//    [JsonPropertyName("hourly")]
//    public required HourlyWeather Hourly { get; set; }
//}

//public class CurrentWeather
//{
//    [JsonPropertyName("temperature_2m")]
//    public required double Temperature2m { get; set; }

//    [JsonPropertyName("wind_speed_10m")]
//    public required double WindSpeed10m { get; set; }
//}

//public class HourlyWeather
//{
//    [JsonPropertyName("time")]
//    public required string[] Time { get; set; }

//    [JsonPropertyName("temperature_2m")]
//    public required double[] Temperature2m { get; set; }

//    [JsonPropertyName("relative_humidity_2m")]
//    public required int[] RelativeHumidity2m { get; set; }

//    [JsonPropertyName("wind_speed_10m")]
//    public required double[] WindSpeed10m { get; set; }
//}

//public class OpenMeteoApiClientOptions
//{
//    public required string BaseAddress { get; set; }
//    public required double Latitude { get; set; }
//    public required double Longitude { get; set; }
//}


using Zadanie3.Helpers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;
using Spectre.Console;
using Spectre.Console.Json;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zadanie3.Helpers;

// ===========================================================
// HOST + DI
// ===========================================================

var builder = Host.CreateApplicationBuilder();

builder.Services.AddChatClient(sp =>
{
    IChatClient client = new OllamaApiClient(
        "http://localhost:11434",
        "gpt-oss:120b-cloud");

    return client.AsBuilder()
        .UseFunctionInvocation()
        .UseLogging()
        .Build(sp);
});

builder.Services.AddHttpClient<OpenMeteoApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.open-meteo.com/");
});

var app = builder.Build();

var chatClient = app.Services.GetRequiredService<IChatClient>();
var weatherClient = app.Services.GetRequiredService<OpenMeteoApiClient>();

// ===========================================================
// AI TOOLS
// ===========================================================

var weatherTool = AiFunctions.CreateWeatherTool(weatherClient);

AIFunction[] functions =
[
    AIFunctionFactory.Create(weatherTool,
        "GetCurrentWeatherForCity",
        "Gets current weather for a city")
];

var chatOptions = new ChatOptions
{
    Tools = [.. functions]
};

// Debug – jak AI widzi funkcje
AnsiConsole.Write(new JsonText(JsonSerializer.Serialize(
    functions.Select(f => new
    {
        f.Name,
        f.Description,
        f.JsonSchema,
        f.ReturnJsonSchema
    }),
    new JsonSerializerOptions { WriteIndented = true }
)));

// ===========================================================
// CHAT HISTORY
// ===========================================================

List<ChatMessage> chatHistory =
[
    new(ChatRole.System,
        """
        You MUST use an available tool whenever the user asks for real-world,
        current, or factual data such as weather.
        Never answer such questions without calling a tool.
        """)
];

// ===========================================================
// CHAT LOOP
// ===========================================================

while (await AnsiConsole.ConfirmAsync("Continue?"))
{
    var prompt = await AnsiConsole.AskAsync<string>("User:");

    chatHistory.Add(new(ChatRole.User, prompt));

    List<ChatResponseUpdate> updates = [];

    var reasoningSb = new StringBuilder();
    var contentSb = new StringBuilder();

    AnsiConsole.Clear();

    await AnsiConsole.Live(new Text(""))
        .AutoClear(false)
        .StartAsync(async ctx =>
        {
            ctx.UpdateTarget(chatHistory.AsRenderable());
            ctx.Refresh();

            await foreach (var update in chatClient.GetStreamingResponseAsync(
                               chatHistory, chatOptions))
            {
                foreach (var content in update.Contents)
                {
                    if (content is TextReasoningContent reasoning)
                        reasoningSb.Append(reasoning.Text);
                    else if (content is TextContent text)
                        contentSb.Append(text.Text);
                }

                ctx.UpdateTarget(new Rows(
                    chatHistory.AsRenderable(),
                    reasoningSb.AsRenderable(),
                    new ChatMessage(ChatRole.Assistant, contentSb.ToString()).AsRenderable()));

                ctx.Refresh();
                updates.Add(update);
            }
        });

    chatHistory.AddMessages(updates);

    AnsiConsole.Clear();
    AnsiConsole.Write(chatHistory.AsRenderable());
}

// ===========================================================
// FINAL JSON DUMP
// ===========================================================

AnsiConsole.Write(new Rows(
    chatHistory.Select(m => new JsonText(m.Serialize()))));

// ===========================================================
// API CLIENT
// ===========================================================

public class OpenMeteoApiClient(HttpClient httpClient)
{
    public async Task<ForecastResponse> GetForecastAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var url = $"v1/forecast?latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
                  "&current_weather=true";

        var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Open-Meteo API error: {response.StatusCode}");

        return (await response.Content.ReadFromJsonAsync<ForecastResponse>(
                   new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
                   cancellationToken))!;
    }
}

// ===========================================================
// AI FUNCTIONS (TOOLS)
// ===========================================================

public static class AiFunctions
{
    public static string Reverse(string input)
        => new string(input.Reverse().ToArray());

    public static Func<string, Task<string>> CreateWeatherTool(OpenMeteoApiClient client)
    {
        return async city =>
        {
            (double latitude, double longitude)? coordinates = city.ToLower() switch
            {
                "warsaw" or "warszawa" => (52.2297, 21.0122),
                "berlin" => (52.52, 13.41),
                "paris" => (48.8566, 2.3522),
                _ => null
            };

            if (coordinates is null)
                return $"Sorry, I don't have coordinates for {city}.";

            var forecast = await client.GetForecastAsync(coordinates.Value.latitude, coordinates.Value.longitude);

            return $"Current weather in {city}: {forecast.Current.Temperature2m} °C, wind {forecast.Current.WindSpeed10m} km/h";
        };
    }
}

// ===========================================================
// DTOs
// ===========================================================

public class ForecastResponse
{
    [JsonPropertyName("current_weather")]
    public required CurrentWeather Current { get; set; }
}

public class CurrentWeather
{
    [JsonPropertyName("temperature")]
    public double Temperature2m { get; set; }

    [JsonPropertyName("windspeed")]
    public double WindSpeed10m { get; set; }
}