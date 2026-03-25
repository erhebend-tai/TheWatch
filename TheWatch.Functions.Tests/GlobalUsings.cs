// GlobalUsings.cs - shared imports for TheWatch.Functions.Tests
// WAL: xUnit + NullLogger + System.Text.Json for all function tests.
//
// Example usage in test files:
//   var logger = NullLoggerFactory.Instance.CreateLogger<MyFunction>();
//   var json = JsonSerializer.Serialize(new MyMessage(...));
//   await new MyFunction(logger).Run(json);

global using Xunit;
global using System.Text.Json;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using TheWatch.Functions.Functions;
global using TheWatch.Shared.Domain.Ports;
global using TheWatch.Shared.Domain.Messages;
global using TheWatch.Shared.Enums;
