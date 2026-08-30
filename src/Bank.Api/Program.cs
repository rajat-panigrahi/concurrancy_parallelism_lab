using Bank.Api.Features.Accounts.OpenAccount;
using Bank.Api.Features.Lab;
using Bank.Api.Features.Lab.StartRun;
using Bank.Api.Features.Fraud.RunFraudChecks;
using Bank.Api.Features.Interest.CalculateInterest;
using Bank.Api.Features.Lab.Streaming;
using Bank.Api.Features.Transfers.DeadlockTransfer;
using Bank.Api.Features.Transfers.IdempotentTransfer;
using Bank.Api.Features.Withdrawals.DistributedLockWithdraw;
using Bank.Api.Features.Withdrawals.LockWithdraw;
using Bank.Api.Features.Withdrawals.NaiveWithdraw;
using Bank.Api.Features.Withdrawals.OptimisticWithdraw;
using Bank.Api.Features.Withdrawals.PessimisticWithdraw;
using Bank.Api.Shared.Contention;
using Bank.Api.Shared.Endpoints;
using Bank.Api.Shared.Interleaving;
using Bank.Api.Shared.Locking;
using Bank.Api.Shared.Persistence;
using Bank.Api.Shared.Withdrawals;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Enums as names, not numbers. A timeline that says "LostUpdate" teaches; one that
// says 10 requires a lookup table, and the UI would have to hardcode it.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// A factory, not just a scoped DbContext. DbContext is NOT thread-safe: it holds a
// change tracker and a single connection, and issuing two concurrent operations on one
// instance throws "A second operation was started on this context".
//
// The lab runs N actors at once against one account, so each actor needs its own
// context — which is also what really happens in production, where each request gets
// its own scope. Sharing one would replace the race we want with an EF exception.
builder.Services.AddDbContextFactory<BankDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("BankDb")));

builder.Services.AddScoped<BankDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<BankDbContext>>().CreateDbContext());

builder.Services.AddSignalR();

// One store for the whole process. That is the point: the in-process slices share
// mutable state, which is exactly what stops them scaling past one instance.
builder.Services.AddSingleton<InMemoryAccountStore>();

// Singletons because a run's timeline, its barriers and its locks must be the same
// objects for every actor in that run. Registering any of these as scoped would give
// each request its own copy and quietly delete the contention we are demonstrating.
builder.Services.AddSingleton<ContentionRecorder>();
builder.Services.AddSingleton<IContentionRecorder>(sp => sp.GetRequiredService<ContentionRecorder>());
builder.Services.AddSingleton<LabInterleaveGate>();
builder.Services.AddSingleton<IInterleaveGate>(sp => sp.GetRequiredService<LabInterleaveGate>());
builder.Services.AddSingleton<LabRunStore>();

// Swap this one registration for a Postgres advisory lock and the `lock` strategy
// survives scale-out. One line is the difference. See ADR-0012.
builder.Services.AddSingleton<IAccountLock, InProcessAccountLock>();
builder.Services.AddSingleton<PostgresAdvisoryAccountLock>();

// Each concurrency lesson registers itself as a strategy the lab can run.
builder.Services.AddSingleton<IWithdrawStrategy, NaiveWithdrawHandler>();
builder.Services.AddSingleton<IWithdrawStrategy, LockWithdrawHandler>();
builder.Services.AddSingleton<IWithdrawStrategy, OptimisticWithdrawHandler>();
builder.Services.AddSingleton<IWithdrawStrategy, PessimisticWithdrawHandler>();
builder.Services.AddSingleton<IWithdrawStrategy, DistributedLockWithdrawHandler>();

builder.Services.AddSingleton<StartRunHandler>();
builder.Services.AddSingleton<CalculateInterestHandler>();
builder.Services.AddSingleton<FraudCheckService>();
builder.Services.AddSingleton<RunFraudChecksHandler>();
builder.Services.AddSingleton<DeadlockTransferHandler>();
builder.Services.AddSingleton<DeadlockDemoHandler>();
builder.Services.AddSingleton<IdempotentTransferHandler>();
builder.Services.AddSingleton<IdempotencyDemoHandler>();
builder.Services.AddScoped<OpenAccountHandler>();
builder.Services.AddHostedService<ContentionBroadcaster>();

// Both spellings of the dev host. `localhost` and `127.0.0.1` are DIFFERENT origins to
// a browser, so allowing only one produces a CORS failure that looks like the API being
// down. AllowCredentials is required for the SignalR websocket handshake, and it forbids
// a wildcard origin — so the list has to be explicit.
builder.Services.AddCors(options => options.AddPolicy("lab-ui", policy => policy
    .WithOrigins(
        "http://localhost:4200",
        "http://127.0.0.1:4200",
        "http://localhost:5080",
        "http://127.0.0.1:5080")
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
app.MapHub<ContentionHub>("/hubs/contention");

app.Run();

/// <summary>Exposed so the integration tests can drive the real app via WebApplicationFactory.</summary>
public partial class Program;
