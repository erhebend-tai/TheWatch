using TheWatch.WorkerServices;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHostedService<TheWatch.WorkerServices.Workers.Worker>();

var host = builder.Build();
host.Run();
