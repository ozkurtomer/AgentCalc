using AgentCalc;
using System.Globalization;
using System.Text.Json;
using System.Collections.Concurrent;

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
                    description = "İki sayıyı toplar",
                    parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            a = new { type = "number", description = "ilk sayı" },
                            b = new { type = "number", description = "ikinci sayı" }
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
    TwoNumberTool("topla", "İki sayıyı toplar"),
    TwoNumberTool("cikar", "İki sayıyı çıkarır: a - b"),
    TwoNumberTool("carp", "İki sayıyı çarpar"),
    TwoNumberTool("bol", "İki sayıyı böler: a / b"),
    NoArgTool("simdi", "Şu anki tarih ve saati döndürür"),
    NoteTool(),
};

var pendingActions = new ConcurrentDictionary<string, (string Name, string ArgsJson)>();

app.MapGet("/agent", async (string q, IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("ollama");
    var messages = new List<object> { new { role = "user", content = q } };
    var steps = new List<object>();
    const int maxTurns = 5;

    for (int turn = 1; turn <= maxTurns; turn++)
    {
        var res = await Chat(http, messages, tools);
        var msg = res.GetProperty("message");

        if (!msg.TryGetProperty("tool_calls", out var calls))
        {
            var answer = msg.GetProperty("content").GetString() ?? "";
            steps.Add(new { turn, type = "answer", text = answer });
            return Results.Json(new { answer, steps });
        }

        messages.Add(new { role = "assistant", content = "", tool_calls = calls });

        foreach (var call in calls.EnumerateArray())
        {
            var fn = call.GetProperty("function");
            var name = fn.GetProperty("name").GetString() ?? "";
            var args = fn.GetProperty("arguments");

            if (name == "notEkle")
            {
                var id = Guid.NewGuid().ToString("N");
                pendingActions[id] = (name, args.GetRawText());
                steps.Add(new { turn, type = "pending", id, name, args = args.GetRawText() });
                return Results.Json(new { answer = "Bu işlem onayınızı bekliyor.", steps });
            }

            var result = RunTool(name, args);

            messages.Add(new { role = "tool", content = result });
            steps.Add(new { turn, type = "tool", name, args = args.GetRawText(), result });
        }
    }

    return Results.Json(new { answer = "Üst sınıra ulaşıldı.", steps });
});

app.MapGet("/onayla", (string id) =>
{
    if (!pendingActions.TryRemove(id, out var action))
        return Results.NotFound("Bekleyen işlem yok veya zaten onaylandı.");

    var args = JsonDocument.Parse(action.ArgsJson).RootElement;
    var result = RunTool(action.Name, args);
    return Results.Text(result);
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
        if (name == "simdi")
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        if (name == "notEkle")
        {
            var metin = args.GetProperty("metin").GetString() ?? "";
            File.AppendAllText("notlar.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm} {metin}{Environment.NewLine}");
            return "Not kaydedildi.";
        }

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
                a = new { type = "number", description = "ilk sayı" },
                b = new { type = "number", description = "ikinci sayı" }
            },
            required = new[] { "a", "b" }
        }
    }
};

static object NoArgTool(string name, string description) => new
{
    type = "function",
    function = new
    {
        name,
        description,
        parameters = new
        {
            type = "object",
            properties = new { },
            required = Array.Empty<string>()
        }
    }
};

static object NoteTool() => new
{
    type = "function",
    function = new
    {
        name = "notEkle",
        description = "Kullanıcı açıkça bir not kaydetmek istediğinde kullanılır. Veriyi değiştirir.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                metin = new { type = "string", description = "kaydedilecek not metni" }
            },
            required = new[] { "metin" }
        }
    }
};