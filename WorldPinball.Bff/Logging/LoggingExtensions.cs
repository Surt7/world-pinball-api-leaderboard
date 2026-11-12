using Serilog;
using Serilog.Events;

namespace WorldPinball.Bff.Logging;

public static class LoggingExtensions
{
    public static WebApplicationBuilder AddBffLogging(this WebApplicationBuilder builder)
    {
        var seqServerUrl = builder.Configuration["Seq:ServerUrl"];

        builder.Services.AddSerilog((services, configuration) =>
        {
            configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "WorldPinball.Bff")
                .WriteTo.Console();

            if (!string.IsNullOrWhiteSpace(seqServerUrl))
            {
                configuration.WriteTo.Seq(seqServerUrl);
            }

            var otelEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            if (!builder.Environment.IsEnvironment("Testing") && !string.IsNullOrWhiteSpace(otelEndpoint))
            {
                configuration.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = otelEndpoint;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = "WorldPinball.Bff",
                        ["service.instance.id"] = Environment.MachineName
                    };
                });
            }
        });

        return builder;
    }
}
