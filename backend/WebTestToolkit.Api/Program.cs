using System.Text.Json;
using System.Text.Json.Serialization;
using WebTestToolkit.Api.Hubs;
using WebTestToolkit.Api.Services;
using WebTestToolkit.Execution.Generation;
using WebTestToolkit.Inspector;
using WebTestToolkit.Llm;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "Frontend";

// Add services to the container.

builder.Services.AddControllers()
    // camelCase strings, not numbers, for every enum ("failed" not "1") — matches how
    // the Llm layer already serializes/parses model output, so the wire shape is
    // consistent whether JSON is coming from Groq or from this API.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// SignalR has its own JSON options, entirely separate from MVC's. Without this, the very
// same InspectorEvent goes out as {"actionType":2} over the hub and {"actionType":"type"}
// over REST, and every client has to handle both shapes.
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

builder.Services.AddSingleton<ISettingsStore, FileSettingsStore>();
builder.Services.AddScoped<IGroqSettingsProvider, ApiGroqSettingsProvider>();
builder.Services.AddWebTestToolkitLlm();

// Singleton: the sandbox owns one warm directory and serializes access to it, so
// concurrent generations queue rather than trampling each other's build output.
builder.Services.AddSingleton<BuildSandbox>();
builder.Services.AddSingleton<ReferenceBundleBuilder>();
builder.Services.AddSingleton<GeneratedProjectWriter>();
builder.Services.AddScoped<HybridTestCodeGenerator>();

// Browser sessions outlive the request that opened them; the manager is a singleton and
// closes any still-open Chrome windows when the host shuts down.
builder.Services.AddWebTestToolkitInspector();
builder.Services.AddHostedService<InspectorBroadcastService>();

// The Vite dev server runs on a different origin (localhost:5173) than the API
// (localhost:5000). SignalR needs credentials for its handshake, so the policy has to
// name the frontend origin explicitly rather than use a wildcard.
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(FrontendCorsPolicy);

app.UseAuthorization();

app.MapControllers();
app.MapHub<PingHub>("/hubs/ping");
app.MapHub<InspectHub>("/hubs/inspect");

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }))
    .WithName("Health");

app.Run();
