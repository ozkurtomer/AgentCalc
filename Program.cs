using AgentCalc;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.RateLimiting;

const string SystemPrompt =
    "Sen çocuklar için bir matematik asistanısın. Sadece matematik ve hesaplama sorularını yanıtla. " +
    "Hesap yaparken mutlaka araçları kullan, sonucu kendin tahmin etme. " +
    "Başka konuları nazikçe reddet ve bir matematik sorusu sormalarını iste. Kısa, sade ve anlaşılır Türkçe kullan.";

const string Model = "gemini-2.5-flash";

var builder = WebApplication.CreateBuilder(args);

var apiKey = builder.Configuration["GEMINI_API_KEY"] ?? "";

builder.Services.AddHttpClient("llm", c =>
{
    c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/");
    c.Timeout = TimeSpan.FromMinutes(2);
    c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("agent", context =>
    {
        var ip = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                 ?? context.Connection.RemoteIpAddress?.ToString()
                 ?? "anon";

        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

var allowWrites = app.Configuration.GetValue<bool>("Agent:AllowWrites");

var tools = Calculator.Tools.Select(Calculator.Schema).ToList();
tools.Add(NoArgTool("simdi", "Şu anki tarih ve saati döndürür"));

if (allowWrites)
    tools.Add(NoteTool());

var toolArray = tools.ToArray();
var pendingActions = new ConcurrentDictionary<string, (string Name, string ArgsJson)>();

app.MapGet("/health", () => Results.Json(new { ok = !string.IsNullOrWhiteSpace(apiKey) }));

app.MapGet("/agent", async (string q, IHttpClientFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Length > 200)
        return Results.BadRequest("Lütfen 1-200 karakter arasında yazın.");

    var http = factory.CreateClient("llm");
    var messages = new List<object>
    {
        new { role = "system", content = SystemPrompt },
        new { role = "user", content = q }
    };
    var steps = new List<object>();
    const int maxTurns = 5;

    for (int turn = 1; turn <= maxTurns; turn++)
    {
        var msg = await Chat(http, messages, toolArray);

        if (!msg.TryGetProperty("tool_calls", out var calls)
            || calls.ValueKind != JsonValueKind.Array
            || calls.GetArrayLength() == 0)
        {
            var answer = msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";
            steps.Add(new { turn, type = "answer", text = answer });
            return Results.Json(new { answer, steps });
        }

        messages.Add(new { role = "assistant", content = "", tool_calls = calls });

        foreach (var call in calls.EnumerateArray())
        {
            var callId = call.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
            var fn = call.GetProperty("function");
            var name = fn.GetProperty("name").GetString() ?? "";
            var args = ParseArgs(fn.GetProperty("arguments"));

            if (name == "notEkle" && !allowWrites)
            {
                const string kapali = "Bu araç bu sunucuda kapalı.";
                messages.Add(new { role = "tool", tool_call_id = callId, content = kapali });
                steps.Add(new { turn, type = "tool", name, args = args.GetRawText(), result = kapali });
                continue;
            }

            if (name == "notEkle")
            {
                var id = Guid.NewGuid().ToString("N");
                pendingActions[id] = (name, args.GetRawText());
                steps.Add(new { turn, type = "pending", id, name, args = args.GetRawText() });
                return Results.Json(new { answer = "Bu işlem onayınızı bekliyor.", steps });
            }

            var result = RunTool(name, args);
            messages.Add(new { role = "tool", tool_call_id = callId, content = result });
            steps.Add(new { turn, type = "tool", name, args = args.GetRawText(), result });
        }
    }

    return Results.Json(new { answer = "Üst sınıra ulaşıldı.", steps });
}).RequireRateLimiting("agent");

app.MapGet("/onayla", (string id) =>
{
    if (!pendingActions.TryRemove(id, out var action))
        return Results.NotFound("Bekleyen işlem yok veya zaten onaylandı.");

    var args = JsonDocument.Parse(action.ArgsJson).RootElement;
    var result = RunTool(action.Name, args);
    return Results.Text(result);
}).RequireRateLimiting("agent");

app.Run();

static async Task<JsonElement> Chat(HttpClient http, List<object> messages, object[] tools)
{
    var res = await http.PostAsJsonAsync("chat/completions", new
    {
        model = Model,
        messages,
        tools,
        tool_choice = "auto",
        temperature = 0
    });
    res.EnsureSuccessStatusCode();
    var json = await res.Content.ReadFromJsonAsync<JsonElement>();
    return json.GetProperty("choices")[0].GetProperty("message");
}

static JsonElement ParseArgs(JsonElement arguments) =>
    arguments.ValueKind == JsonValueKind.String
        ? JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments.GetString()) ? "{}" : arguments.GetString()!).RootElement
        : arguments;

static string RunTool(string name, JsonElement args)
{
    try
    {
        if (name == "simdi")
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        if (name == "notEkle")
        {
            var metin = args.GetProperty("metin").GetString() ?? "";
            File.AppendAllText("notlar.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm} {metin}{Environment.NewLine}");
            return "Not kaydedildi.";
        }

        return Calculator.Run(name, args);
    }
    catch (Exception ex)
    {
        return $"Hata: {ex.Message}";
    }
}

static object NoArgTool(string name, string description) => new
{
    type = "function",
    function = new { name, description }
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
