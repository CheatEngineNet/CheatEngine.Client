using LivePlugin.Qualification.Harness;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     The Q46 log sink of the harness keeps what a log reader may see (category, event id, level, message template) and
///     never the formatted message; it counts every event whose formatted text or exception carries scenario data.
/// </summary>
public sealed partial class CapturingLoggerProviderTests
{
	[Fact]
	public void CapturedEventsKeepTemplatesButNeverFormattedMessages()
	{
		using CapturingLoggerProvider provider = new();
		ILogger logger = provider.CreateLogger("CheatEngine.Client.Hosting.CheatEngineClientPlugin");

		ActivationEnabled(logger, 7);
		CleanupFailed(logger, "scope");

		IReadOnlyList<CapturedLogEvent> events = provider.Events();
		Assert.Equal(2, events.Count);
		Assert.Equal(new CapturedLogEvent("CheatEngine.Client.Hosting.CheatEngineClientPlugin", 1, "ActivationEnabled",
			LogLevel.Information, "Client activation {Epoch} enabled"), events[0]);
		Assert.Equal("Cleanup of {Stage} failed", events[1].Template);
		Assert.Equal(42, events[1].EventId);
		Assert.Equal(LogLevel.Warning, events[1].Level);
		Assert.DoesNotContain(events, static captured => captured.Template.Contains("scope", StringComparison.Ordinal));
		Assert.Equal(0, provider.SensitiveHits);
	}

	[Fact]
	public void SensitiveHitsCountAddressesAndDeclaredValues()
	{
		using CapturingLoggerProvider provider = new();
		ILogger logger = provider.CreateLogger("Qualification");
		provider.DeclareSensitive("DE AD BE EF CA FE");
		provider.DeclareSensitive("0x10");

		ScanFinished(logger, "de ad be ef ca fe");
		Wrote(logger, "0x7FF712345678");
		ScriptApplied(logger, CapturingLoggerProvider.ScriptMarker + "_patch");
		OperationFailed(logger, new InvalidOperationException("failed at 0x10"), "Memory.Write");
		NothingSensitive(logger, 3);

		Assert.Equal(4, provider.SensitiveHits);
		Assert.Equal(5, provider.Events().Count);
		Assert.All(provider.Events(), static captured =>
			Assert.DoesNotContain("0x7FF712345678", captured.Template, StringComparison.Ordinal));
	}

	[LoggerMessage(EventId = 1, EventName = "ActivationEnabled", Level = LogLevel.Information,
		Message = "Client activation {Epoch} enabled")]
	private static partial void ActivationEnabled(ILogger logger, long epoch);

	[LoggerMessage(EventId = 42, EventName = "Cleanup", Level = LogLevel.Warning, Message = "Cleanup of {Stage} failed")]
	private static partial void CleanupFailed(ILogger logger, string stage);

	[LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Scan of {Pattern} finished")]
	private static partial void ScanFinished(ILogger logger, string pattern);

	[LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Wrote {Address}")]
	private static partial void Wrote(ILogger logger, string address);

	[LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Script {Name} applied")]
	private static partial void ScriptApplied(ILogger logger, string name);

	[LoggerMessage(EventId = 103, Level = LogLevel.Error, Message = "Operation {Name} failed")]
	private static partial void OperationFailed(ILogger logger, Exception exception, string name);

	[LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "Nothing sensitive in {Count} items")]
	private static partial void NothingSensitive(ILogger logger, int count);
}
