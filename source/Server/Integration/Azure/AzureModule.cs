using System;
using Autofac;
using JetBrains.Annotations;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using SharpLab.Server.Monitoring;

namespace SharpLab.Server.Azure {
    [UsedImplicitly]
    public class AzureModule : Module {
        protected override void Load(ContainerBuilder builder) {
            var instrumentationKey = Environment.GetEnvironmentVariable("SHARPLAB_TELEMETRY_KEY");
            var webAppName = Environment.GetEnvironmentVariable("SHARPLAB_WEBAPP_NAME");
            if (instrumentationKey == null || webAppName == null)
                return;

            var configuration = new TelemetryConfiguration { InstrumentationKey = instrumentationKey };
            builder.RegisterInstance(new TelemetryClient(configuration))
                   .AsSelf();

            builder.RegisterType<ApplicationInsightsMonitor>()
                   .As<IMonitor>()
                   .WithParameter("webAppName", webAppName)
                   .SingleInstance();
        }
    }
}