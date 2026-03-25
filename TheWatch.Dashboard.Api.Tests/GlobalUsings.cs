// GlobalUsings.cs — shared using directives for all test files in this project.
// WAL: Keep test infrastructure imports centralized here to reduce per-file boilerplate.
//
// Example — a test file only needs:
//   namespace TheWatch.Dashboard.Api.Tests;
//   public class MyTests { [Fact] public void It_Works() { ... } }

global using Xunit;
global using NSubstitute;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.AspNetCore.SignalR;
global using TheWatch.Dashboard.Api.Services;
global using TheWatch.Dashboard.Api.Hubs;
global using TheWatch.Shared.Domain.Ports;
global using TheWatch.Shared.Domain.Models;
global using TheWatch.Shared.Enums;
