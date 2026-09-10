using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KsitalTelemetryHub.Service.Worker;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService() // Включает режим системной службы Windows при запуске через Service Control Manager
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();
    });

var host = builder.Build();
await host.RunAsync();