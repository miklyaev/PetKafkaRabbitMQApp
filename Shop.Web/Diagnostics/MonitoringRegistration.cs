using System.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.Contracts;
using Shop.Observability;

namespace Shop.Web.Diagnostics;

public sealed class DiagnosticsHub : Hub;

public static class MonitoringRegistration
{
    public static bool AddWebBrokerMonitoring(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection("BrokerMonitoring").Get<BrokerMonitoringOptions>() ?? new();
        var registered = services.AddBrokerDiagnostics(configuration, environment, "Shop.Web");
        var enabled = registered && environment.IsDevelopment() && options.Enabled;
        services.AddSingleton(options);
        if (!enabled) return false;
        services.AddSingleton(configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new());
        services.AddSingleton(configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new());
        services.AddSingleton<DiagnosticJournal>();
        services.Replace(ServiceDescriptor.Singleton<IDiagnosticSink>(p => p.GetRequiredService<DiagnosticJournal>()));
        services.AddHttpClient("broker-monitoring", client => client.Timeout = TimeSpan.FromSeconds(2))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        services.AddSingleton<RabbitMonitoring>();
        services.AddSingleton<KafkaMonitoring>();
        services.AddHostedService<MonitoringPoller>();
        services.AddHostedService<DiagnosticNotifier>();
        return true;
    }

    public static IApplicationBuilder UseBrokerMonitoringGuard(this IApplicationBuilder app, bool enabled) => app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isIngestion = path.StartsWithSegments("/internal/diagnostics");
        var isMonitoring = isIngestion || path.StartsWithSegments("/api/diagnostics")
            || path.StartsWithSegments("/hubs/diagnostics") || path.StartsWithSegments("/brokers");
        if (!isMonitoring) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!enabled || (isIngestion && context.Request.Headers.ContainsKey("Origin"))
            || (!isIngestion && !IsLocalRequest(context)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    });

    public static bool IsLocalRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote?.IsIPv4MappedToIPv6 == true) remote = remote.MapToIPv4();
        if (remote is null || !IPAddress.IsLoopback(remote)) return false;
        // Validate Host as well as the real remote address to mitigate DNS rebinding.
        var host = context.Request.Host.Host.Trim('[', ']');
        if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && (!IPAddress.TryParse(host, out var address) || !IPAddress.IsLoopback(address))) return false;
        if (context.Request.Headers.TryGetValue("Origin", out var origin))
        {
            if (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
                || BrokerMonitoringOptions.LoopbackUrl(origin.ToString()) is null) return false;
            var sameOrigin = string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase);
            var allowed = context.RequestServices.GetService<BrokerMonitoringOptions>()?.AllowedOrigins ?? [];
            if (!sameOrigin && !allowed.Any(value => BrokerMonitoringOptions.LoopbackUrl(value) is { } configured
                && string.Equals(configured.GetLeftPart(UriPartial.Authority), uri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))) return false;
        }
        return true;
    }
}

internal sealed class MonitoringPoller(RabbitMonitoring rabbit, KafkaMonitoring kafka) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        Poll(rabbit.PollAsync, stoppingToken), Poll(kafka.PollAsync, stoppingToken));

    private static async Task Poll(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        do
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { await action(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }
}

internal sealed class DiagnosticNotifier(DiagnosticJournal journal, IHubContext<DiagnosticsHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var revision = journal.Revision;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var current = journal.Revision;
            if (current == revision) continue;
            revision = current;
            await hub.Clients.All.SendAsync("DiagnosticsChanged", stoppingToken);
        }
    }
}
