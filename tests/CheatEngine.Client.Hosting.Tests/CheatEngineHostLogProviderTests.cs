using CheatEngine.SDK.Hosting.Diagnostics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.Hosting.Tests;

/// <summary>The serial collection of the tests that replace CheatEngine.SDK's process-wide host log sink and level.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostLogSerialGroup
{
	/// <summary>The collection name.</summary>
	public const string Name = "CheatEngine.SDK host log";
}

/// <summary>
///     The opt-in host log provider writes through <see cref="HostLog.Write" /> into a fake sink: message templates only by
///     default (Q46), the formatted message on request, the four host levels, and <see cref="HostLog.IsEnabled" />.
/// </summary>
[Collection(HostLogSerialGroup.Name)]
public sealed partial class CheatEngineHostLogProviderTests : IDisposable
{
	private const string Category = "CheatEngine.Client.Hosting.Tests.HostLog";
	private const string AddressMarker = "0x7FFC7A0A0000";
	private const string PathMarker = "C:\\Users\\player\\secret.ct";

	private readonly HostLogLevel _previousLevel;
	private readonly IHostLogSink _previousSink;
	private readonly RecordingSink _sink = new();

	public CheatEngineHostLogProviderTests()
	{
		_previousSink = HostLog.Sink;
		_previousLevel = HostLog.MinimumLevel;
		HostLog.Sink = _sink;
		HostLog.MinimumLevel = HostLogLevel.Trace;
	}

	public void Dispose()
	{
		HostLog.Sink = _previousSink;
		HostLog.MinimumLevel = _previousLevel;
	}

	[Fact]
	public void AddCheatEngineHostLogRegistersOneProviderWithTheOptionsOfTheFirstCall()
	{
		ServiceCollection services = new();

		services.AddLogging(logging => logging
			.AddCheatEngineHostLog()
			.AddCheatEngineHostLog(static options => options.IncludeFormattedMessages = true));

		ServiceDescriptor descriptor = Assert.Single(services,
			static descriptor => descriptor.ServiceType == typeof(ILoggerProvider));
		CheatEngineHostLogProvider provider = Assert.IsType<CheatEngineHostLogProvider>(descriptor.ImplementationInstance);
		Assert.False(provider.IncludeFormattedMessages);
	}

	[Fact]
	public void AddCheatEngineHostLogRejectsMissingArguments()
	{
		ServiceCollection services = new();
		ILoggingBuilder? logging = null;
		services.AddLogging(builder => logging = builder);

		Assert.Throws<ArgumentNullException>(static () => ((ILoggingBuilder) null!).AddCheatEngineHostLog());
		Assert.Throws<ArgumentNullException>(() => logging!.AddCheatEngineHostLog(null!));
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void EntriesCarryTheMessageTemplateAndNeverTheArgumentValues()
	{
		using ILoggerFactory factory = CreateFactory(static _ =>
		{
		});
		ILogger logger = factory.CreateLogger(Category);

		LogRead(logger, AddressMarker, PathMarker);

		HostLogEntry entry = Assert.Single(_sink.Entries);
		Assert.Equal(HostLogLevel.Information, entry.Level);
		Assert.Equal(Category + "[4601]: Read {Address} from {Path}.", entry.Message);
		Assert.Null(entry.Exception);
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void ExceptionsAreReducedToTheirTypeNameByDefault()
	{
		using ILoggerFactory factory = CreateFactory(static _ =>
		{
		});
		ILogger logger = factory.CreateLogger(Category);

		LogFailure(logger, new InvalidOperationException("failed at " + AddressMarker), 3);

		HostLogEntry entry = Assert.Single(_sink.Entries);
		Assert.Equal(HostLogLevel.Error, entry.Level);
		Assert.Equal(Category + "[4602]: Operation failed after {Attempts} attempt(s). " +
					 "(System.InvalidOperationException)", entry.Message);
		Assert.Null(entry.Exception);
		Assert.DoesNotContain(AddressMarker, entry.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void IncludeFormattedMessagesWritesTheFormattedMessageAndTheException()
	{
		using ILoggerFactory factory = CreateFactory(static options => options.IncludeFormattedMessages = true);
		ILogger logger = factory.CreateLogger(Category);
		InvalidOperationException failure = new("failed");

		LogRead(logger, AddressMarker, PathMarker);
		LogFailure(logger, failure, 3);

		Assert.Collection(
			_sink.Entries,
			read => Assert.Equal($"{Category}[4601]: Read {AddressMarker} from {PathMarker}.", read.Message),
			failed =>
			{
				Assert.Equal($"{Category}[4602]: Operation failed after 3 attempt(s).", failed.Message);
				Assert.Same(failure, failed.Exception);
			});
	}

	[Fact]
	public void StateWithoutATemplateWritesItsCategoryAndEventOnly()
	{
		using ILoggerFactory factory = CreateFactory(static _ =>
		{
		});
		ILogger logger = factory.CreateLogger(Category);

		logger.Log(LogLevel.Warning, new EventId(7), "raw state at " + AddressMarker, null,
			static (state, _) => state);

		HostLogEntry entry = Assert.Single(_sink.Entries);
		Assert.Equal(HostLogLevel.Warning, entry.Level);
		Assert.Equal(Category + "[7]", entry.Message);
	}

	[Theory]
	[InlineData(LogLevel.Trace, HostLogLevel.Trace)]
	[InlineData(LogLevel.Debug, HostLogLevel.Trace)]
	[InlineData(LogLevel.Information, HostLogLevel.Information)]
	[InlineData(LogLevel.Warning, HostLogLevel.Warning)]
	[InlineData(LogLevel.Error, HostLogLevel.Error)]
	[InlineData(LogLevel.Critical, HostLogLevel.Error)]
	public void EveryLogLevelMapsToAHostLogLevel(LogLevel logLevel, HostLogLevel expected)
	{
		ILogger logger = new CheatEngineHostLogProvider(false).CreateLogger(Category);

		logger.Log(logLevel, new EventId(1), "state", null, static (state, _) => state);

		Assert.True(logger.IsEnabled(logLevel));
		Assert.Equal(expected, Assert.Single(_sink.Entries).Level);
	}

	[Fact]
	public void NoneAndUndefinedLevelsAreNeverWritten()
	{
		ILogger logger = new CheatEngineHostLogProvider(false).CreateLogger(Category);

		logger.Log(LogLevel.None, new EventId(1), "state", null, static (state, _) => state);
		logger.Log((LogLevel) 42, new EventId(1), "state", null, static (state, _) => state);

		Assert.False(logger.IsEnabled(LogLevel.None));
		Assert.False(logger.IsEnabled((LogLevel) 42));
		Assert.Empty(_sink.Entries);
	}

	[Fact]
	public void TheHostLogMinimumLevelDecidesWhatIsWritten()
	{
		HostLog.MinimumLevel = HostLogLevel.Warning;
		ILogger logger = new CheatEngineHostLogProvider(false).CreateLogger(Category);

		logger.Log(LogLevel.Information, new EventId(1), "information", null, static (state, _) => state);
		logger.Log(LogLevel.Warning, new EventId(2), "warning", null, static (state, _) => state);

		Assert.False(logger.IsEnabled(LogLevel.Information));
		Assert.True(logger.IsEnabled(LogLevel.Warning));
		Assert.Equal(Category + "[2]", Assert.Single(_sink.Entries).Message);
	}

	private static ILoggerFactory CreateFactory(Action<CheatEngineHostLogOptions> configure)
	{
		return LoggerFactory.Create(logging => logging
			.SetMinimumLevel(LogLevel.Trace)
			.AddCheatEngineHostLog(configure));
	}

	[LoggerMessage(4601, LogLevel.Information, "Read {Address} from {Path}.")]
	private static partial void LogRead(ILogger logger, string address, string path);

	[LoggerMessage(4602, LogLevel.Error, "Operation failed after {Attempts} attempt(s).")]
	private static partial void LogFailure(ILogger logger, Exception exception, int attempts);

	private sealed record HostLogEntry(HostLogLevel Level, string Message, Exception? Exception);

	/// <summary>Records the entries of this test's category; entries written by other code are ignored.</summary>
	private sealed class RecordingSink : IHostLogSink
	{
		private readonly List<HostLogEntry> _entries = [];
		private readonly Lock _gate = new();

		internal IReadOnlyList<HostLogEntry> Entries
		{
			get
			{
				lock (_gate)
				{
					return [.. _entries];
				}
			}
		}

		public void Write(HostLogLevel level, string message, Exception? exception)
		{
			if (!message.StartsWith(Category, StringComparison.Ordinal))
			{
				return;
			}

			lock (_gate)
			{
				_entries.Add(new HostLogEntry(level, message, exception));
			}
		}
	}
}
