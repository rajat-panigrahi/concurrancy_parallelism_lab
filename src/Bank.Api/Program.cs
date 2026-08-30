using Bank.Api.Shared.Endpoints;
using Bank.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<BankDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BankDb")));

// One store for the whole process. That is the point: the in-process slices share
// mutable state, which is exactly what stops them scaling past one instance.
builder.Services.AddSingleton<InMemoryAccountStore>();

builder.Services.AddCors(options => options.AddPolicy("lab-ui", policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("lab-ui");

// Every instance reports which one it is, so the multi-replica demo can show you
// which process served each request.
var instanceId = Environment.GetEnvironmentVariable("INSTANCE_ID")
                 ?? Environment.MachineName;

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    instanceId,
    processId = Environment.ProcessId,
    processorCount = Environment.ProcessorCount,
}))
.WithTags("Health");

app.MapSliceEndpoints();

app.Run();

/// <summary>Exposed so the integration tests can drive the real app via WebApplicationFactory.</summary>
public partial class Program;
