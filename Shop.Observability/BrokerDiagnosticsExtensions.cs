using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Shop.Observability;

/// <summary>Opt-in Development-only broker instrumentation and independent HTTP delivery.</summary>
public static class BrokerDiagnosticsExtensions
{
    /// <summary>
    /// Registers a no-op sink unless Development and BrokerMonitoring:Enabled are both true.
    /// Returns whether callers should register broker observers. Missing IngestionKey disables only
    /// outbound delivery, not local collection. Replace IDiagnosticSink after this call for a local journal;
    /// the exporter then neither creates nor drains an outbound buffer. Supply secrets through external configuration.
    /// </summary>
    public static bool AddBrokerDiagnostics(this IServiceCollection services, IConfiguration configuration,
        IHostEnvironment environment, string serviceName)
    {
        var enabled = environment.IsDevelopment() && configuration.GetValue<bool>("BrokerMonitoring:Enabled");
        if (!enabled)
        {
            services.AddSingleton<IDiagnosticSink, NullDiagnosticSink>();
            return false;
        }

        services.AddSingleton(new DiagnosticIdentity(serviceName, Guid.NewGuid().ToString("N")));
        services.AddSingleton<IDiagnosticSink>(_ => new BoundedDiagnosticSink());
        services.AddSingleton<BrokerDiagnosticObserver>();
        services.AddHostedService<DiagnosticExporter>();
        return true;
    }

    /// <summary>
    /// Connects send/publish/consume/receive observers via the MassTransit 8.5.11 container hooks.
    /// Call only when AddBrokerDiagnostics returned true. The Kafka rider inherits these host observers:
    /// produce uses ISendObserver and KafkaSendContext, without a second explicit Produce wrapper.
    /// </summary>
    public static void AddBrokerDiagnosticObservers(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddSendObserver(provider => provider.GetRequiredService<BrokerDiagnosticObserver>());
        configurator.AddPublishObserver(provider => provider.GetRequiredService<BrokerDiagnosticObserver>());
        configurator.AddConsumeObserver(provider => provider.GetRequiredService<BrokerDiagnosticObserver>());
        configurator.AddReceiveObserver(provider => provider.GetRequiredService<BrokerDiagnosticObserver>());
    }
}
