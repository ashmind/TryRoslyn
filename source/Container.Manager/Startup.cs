using System;
using Docker.DotNet;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpLab.Container.Manager.Azure;
using SharpLab.Container.Manager.Endpoints;
using SharpLab.Container.Manager.Internal;

namespace SharpLab.Container.Manager {
    public class Startup {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // TODO: proper DI, e.g. Autofac
            services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

            services.AddSingleton<StatusEndpoint>();

            var authorizationToken = Environment.GetEnvironmentVariable("SHARPLAB_CONTAINER_HOST_AUTHORIZATION_TOKEN")
                ?? throw new Exception("Required environment variable SHARPLAB_CONTAINER_HOST_AUTHORIZATION_TOKEN was not provided.");
            services.AddSingleton(new ExecutionEndpointSettings(authorizationToken));
            services.AddSingleton<ExecutionEndpoint>();

            services.AddSingleton(new DockerClientConfiguration().CreateClient());

            services.AddSingleton<ContainerNameFormat>();
            services.AddSingleton<ContainerPool>();

            services.AddHostedService<ContainerAllocationWorker>();
            services.AddSingleton<ContainerCleanupWorker>();
            services.AddHostedService(c => c.GetRequiredService<ContainerCleanupWorker>());

            services.AddSingleton<StdinWriter>();
            services.AddSingleton<StdoutReader>();
            services.AddSingleton<ExecutionProcessor>();
            services.AddSingleton<CrashSuspensionManager>();
            services.AddSingleton<ExecutionManager>();

            ConfigureAzureDependentServices(services);
        }

        private void ConfigureAzureDependentServices(IServiceCollection services) {
            var instrumentationKey = Environment.GetEnvironmentVariable("SHARPLAB_TELEMETRY_KEY");
            if (instrumentationKey == null) {
                Console.WriteLine("[WARN] AppInsights instrumentation key was not found.");
                return;
            }

            var configuration = new TelemetryConfiguration { InstrumentationKey = instrumentationKey };
            services.AddSingleton(new TelemetryClient(configuration));
            services.AddHostedService<ContainerCountMetricReporter>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            app.UseRouting();

            app.UseEndpoints(endpoints => {
                endpoints.MapGet("/status", app.ApplicationServices.GetRequiredService<StatusEndpoint>().ExecuteAsync);
                endpoints.MapPost("/", app.ApplicationServices.GetRequiredService<ExecutionEndpoint>().ExecuteAsync);
            });
        }
    }
}
