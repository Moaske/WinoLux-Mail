using Wino.Core.Domain.Interfaces;

namespace Wino.Platform.Linux;

/// <summary>
/// The Linux application configuration. Crash reporting stays off: the shared configuration carries upstream Wino
/// Mail's Sentry project, which an unofficial fork must not report to, and an empty DSN disables the Sentry SDK.
/// </summary>
public sealed class LinuxApplicationConfiguration : IApplicationConfiguration
{
    public bool AllowLegacyDataMigration => false;
    public string ApplicationDisplayName => "WinoLux Mail";
    public required string ApplicationDataFolderPath { get; set; }
    public required string PublisherSharedFolderPath { get; set; }
    public required string ApplicationTempFolderPath { get; set; }
    public string SentryDNS => string.Empty;
}
