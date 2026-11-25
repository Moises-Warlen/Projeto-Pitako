using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pitako.HardwareAgent;
using Pitako.Modules.Printing.Application.Abstractions;
using Pitako.Modules.Weighing.Application.Abstractions;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IThermalPrinterTransport, LocalNetworkEscPosPrinterTransport>();
builder.Services.AddSingleton<IScaleTransport, LocalNetworkToledoScaleTransport>();
builder.Services.AddHostedService<HardwareAgentWorker>();

var host = builder.Build();
await host.RunAsync();
