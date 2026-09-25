using LivePlugin.Qualification.Harness;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>The serial collection of the tests that touch the process-wide lifecycle sink.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class QualificationLifecycleSinkGroup
{
	/// <summary>The collection name.</summary>
	public const string Name = "Qualification lifecycle sink";
}

/// <summary>
///     The Q43 lifecycle sink: the lifecycle record and captured log templates reach the runner's file one line each,
///     only when the runner configured a file, never a formatted message, and a failing write never throws.
/// </summary>
[Collection(QualificationLifecycleSinkGroup.Name)]
public sealed class QualificationLifecycleSinkTests : IDisposable
{
	private readonly string _directory = Directory.CreateTempSubdirectory("qualification-sink-").FullName;

	public void Dispose()
	{
		QualificationLifecycleSink.Configure(null);
		Directory.Delete(_directory, true);
	}

	[Fact]
	public void NothingIsWrittenWithoutAConfiguredFile()
	{
		QualificationLifecycleSink.Configure(null);

		QualificationLifecycleSink.Ledger("#1 first.enabled");

		Assert.False(QualificationLifecycleSink.IsConfigured);
		Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
	}

	[Fact]
	public void LedgerEntriesAndLogTemplatesAreAppendedOneLineEach()
	{
		string path = Path.Combine(_directory, "lifecycle.txt");
		QualificationLifecycleSink.Configure(path);
		using CapturingLoggerProvider provider = new();

		QualificationLifecycleSink.Ledger("#2 fault.disabling.threw InvalidOperationException");
		provider.CreateLogger("CheatEngine.Client.Hosting.CheatEngineClientPlugin").Log(LogLevel.Warning,
			new EventId(5, "ActivationCleanupFailed"), "cleanup of 0x7FF712345678 failed", null,
			static (_, _) => "formatted 0x7FF712345678");
		QualificationLifecycleSink.Ledger("#2 first\tdisabling\nsplit");

		string[] lines = File.ReadAllLines(path);
		Assert.Contains("ledger\t#2 fault.disabling.threw InvalidOperationException", lines);
		Assert.Contains("log\tWarning\tCheatEngine.Client.Hosting.CheatEngineClientPlugin\t5\t<no template>", lines);
		Assert.Contains("ledger\t#2 first disabling split", lines);
		Assert.DoesNotContain(lines, static line => line.Contains("0x7FF712345678", StringComparison.Ordinal));
	}

	[Fact]
	public void AFailedWriteIsCountedNeverThrown()
	{
		QualificationLifecycleSink.Configure(_directory);
		int before = QualificationLifecycleSink.FailedWrites;

		QualificationLifecycleSink.Ledger("#3 last.disabling");

		Assert.Equal(before + 1, QualificationLifecycleSink.FailedWrites);
	}

	[Fact]
	public void FieldsLoseTheirControlCharacters()
	{
		Assert.Equal("a b c d", QualificationLifecycleSink.Clean("a\tb\rc\nd"));
	}
}
