// BuildOutputSeverity — classifies individual lines from build output.
// Parsed from MSBuild/dotnet build output patterns.
// Example: if (entry.Severity == BuildOutputSeverity.Error) FailBuild();

namespace TheWatch.Shared.Enums;

public enum BuildOutputSeverity
{
    Trace = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Fatal = 4
}
