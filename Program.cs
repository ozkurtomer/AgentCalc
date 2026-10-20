using AgentCalc;
using System.Globalization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("ollama", c =>
{
    c.BaseAddress = new Uri("http://localhost:11434");
    c.Timeout = TimeSpan.FromMinutes(2);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/ask", async (string q, IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("ollama");

    var body = new
    {
        model = "qwen2.5:7b",
        messages = new[] { new { role = "user", content = q } },
        stream = false
    };

    var res = await http.PostAsJsonAsync("/api/chat", body);
    res.EnsureSuccessStatusCode();

    var json = await res.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("message").GetProperty("content").GetString();
});

app.MapGet("/tool-test", async (string q, IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("ollama");

    var body = new
    {
        model = "qwen2.5:7b",
        messages = new[] { new { role = "user", content = q } },
        tools = new[]
        {
            new
            {
                type = "function",
                function = new
                {
                    name = "topla",
                    description = "Ýki sayýyý toplar",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            a = new { type = "number", description = "ilk sayý" },
                            b = new { type = "number", description = "ikinci sayý" }
                        },
                        required = new[] { "a", "b" }
                    }
                }
            }
        },
        stream = false
    };

    var res = await http.PostAsJsonAsync("/api/chat", body);
    res.EnsureSuccessStatusCode();
    return await res.Content.ReadAsStringAsync();
});

var tools = new object[]
{
    TwoNumberTool("topla", "Ýki sayýyý toplar"),
    TwoNumberTool("cikar", "Ýki sayýyý çýkarýr: a - b"),
    TwoNumberTool("carp", "Ýki sayýyý çarpar"),
    TwoNumberTool("bol", "Ýki sayýyý böler: a / b")
};

app.MapGet("/agent", async (string q, IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("ollama");
    var messages = new List<object> { new { role = "user", content = q } };

    const int maxTurns = 5;

    for (int turn = 1; turn <= maxTurns; turn++)
    {
        var res = await Chat(http, messages, tools);
        var msg = res.GetProperty("message");

        if (!msg.TryGetProperty("tool_calls", out var calls))
            return msg.GetProperty("content").GetString();

        messages.Add(new { role = "assistant", content = "", tool_calls = calls });

        foreach (var call in calls.EnumerateArray())
        {
            var fn = call.GetProperty("function");
            var name = fn.GetProperty("name").GetString() ?? "";
            var args = fn.GetProperty("arguments");

            string result = RunTool(name, args);
            messages.Add(new { role = "tool", content = result });
        }
    }

    return "Üst sýnýra ulaþýldý, iþlem tamamlanamadý.";
});

app.Run();

static async Task<JsonElement> Chat(HttpClient http, List<object> messages, object[] tools)
{
    var res = await http.PostAsJsonAsync("/api/chat", new
    {
        model = "qwen2.5:7b",
        messages,
        tools,
        stream = false
    });
    res.EnsureSuccessStatusCode();
    return await res.Content.ReadFromJsonAsync<JsonElement>();
}

static string RunTool(string name, JsonElement args)
{
    try
    {
        double a = args.GetProperty("a").GetDouble();
        double b = args.GetProperty("b").GetDouble();

        return name switch
        {
            "topla" => Calculator.Topla(a, b).ToString(CultureInfo.InvariantCulture),
            "cikar" => Calculator.Cikar(a, b).ToString(CultureInfo.InvariantCulture),
            "carp" => Calculator.Carp(a, b).ToString(CultureInfo.InvariantCulture),
            "bol" => Calculator.Bol(a, b).ToString(CultureInfo.InvariantCulture),
            _ => $"Bilinmeyen araç: {name}"
        };
    }
    catch (Exception ex)
    {
        return $"Hata: {ex.Message}";
    }
}

static object TwoNumberTool(string name, string description) => new
{
    type = "function",
    function = new
    {
        name,
        description,
        parameters = new
        {
            type = "object",
            properties = new
            {
                a = new { type = "number", description = "ilk sayý" },
                b = new { type = "number", description = "ikinci sayý" }
            },
            required = new[] { "a", "b" }
        }
    }
};

app.Run();