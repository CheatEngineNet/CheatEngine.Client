using System.Reflection;
using System.Text.RegularExpressions;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Client.Extensions.DependencyInjection.Tests;

/// <summary>
///     The logging sink of the Core diagnostic events (audit ch.24): one category per domain, stable event ids, one
///     capability refusal per capability and operation, redacted fields, and no provider fault ever escaping.
/// </summary>
public sealed partial class LoggerCoreDiagnosticsTests
{
	[Fact]
	public void ThrowingLoggerProviderDoesNotEscapeDispatch()
	{
		ThrowingLoggerProvider fromIsEnabled = new(ThrowingStage.IsEnabled);
		ThrowingLoggerProvider fromLog = new(ThrowingStage.Log);
		ThrowingLoggerProvider fromCreateLogger = new(ThrowingStage.CreateLogger);

		foreach (ThrowingLoggerProvider provider in new[] { fromIsEnabled, fromLog, fromCreateLogger })
		{
			using ILoggerFactory factory = CreateFactory(provider);
			LoggerCoreDiagnostics diagnostics = new(factory);

			EmitScriptedRun(diagnostics);
		}

		Assert.Equal(ScriptedEventCount, fromIsEnabled.IsEnabledCalls);
		Assert.Equal(0, fromIsEnabled.LogCalls);
		Assert.Equal(ScriptedEventCount, fromLog.LogCalls);
		Assert.Equal(8, fromCreateLogger.CreateLoggerCalls);
		Assert.Equal(0, fromCreateLogger.LogCalls);
	}

	[Fact]
	public void EachDiagnosticEventUsesItsDomainCategoryAndStableEventId()
	{
		CapturingLoggerProvider logs = new();
		using ILoggerFactory factory = CreateFactory(logs);

		EmitScriptedRun(new LoggerCoreDiagnostics(factory));

		Assert.Equal(
			[
				new EventShape(1000, "RuntimeSnapshotCaptured", "CheatEngine.Client.Runtime", LogLevel.Debug),
				new EventShape(1001, "CapabilityRefused", "CheatEngine.Client.Runtime", LogLevel.Debug),
				new EventShape(1100, "TargetSelectionAdvanced", "CheatEngine.Client.Processes", LogLevel.Debug),
				new EventShape(1200, "PointerWidthMismatchRefused", "CheatEngine.Client.Memory", LogLevel.Information),
				new EventShape(1201, "MemoryBatchCompleted", "CheatEngine.Client.Memory", LogLevel.Debug),
				new EventShape(1300, "TableGenerationAdvanced", "CheatEngine.Client.Tables", LogLevel.Debug),
				new EventShape(1301, "StaleRecordIdentifierRefused", "CheatEngine.Client.Tables", LogLevel.Debug),
				new EventShape(1302, "RecordActivationNotApplied", "CheatEngine.Client.Tables", LogLevel.Debug),
				new EventShape(1400, "SymbolRegistrationRejected", "CheatEngine.Client.Inspection", LogLevel.Debug),
				new EventShape(1401, "SymbolLeaseReleased", "CheatEngine.Client.Inspection", LogLevel.Debug),
				new EventShape(1500, "PatternScanCompleted", "CheatEngine.Client.Scanning", LogLevel.Debug),
				new EventShape(1600, "LuaOperationCompleted", "CheatEngine.Client.Lua", LogLevel.Debug),
				new EventShape(1700, "CoreResourceCleanupFailed", "CheatEngine.Client.Lifetime", LogLevel.Warning)
			],
			logs.Entries.Select(static entry => new EventShape(entry.EventId.Id, entry.EventId.Name, entry.Category,
				entry.Level)));
		Assert.Contains(logs.Entries, static entry => entry.Message ==
													  "Memory.ReadPrimitive was refused: Cheat Engine's configured " +
													  "pointer size (4 byte(s)) differs from the target process " +
													  "width (8 byte(s)).");
	}

	[Fact]
	public void DomainCategoriesFollowTheStandardLogLevelFilters()
	{
		CapturingLoggerProvider logs = new();
		using ILoggerFactory factory = LoggerFactory.Create(logging => logging
			.SetMinimumLevel(LogLevel.Information)
			.AddFilter(LoggerCoreDiagnostics.TablesCategory, LogLevel.Debug)
			.AddFilter(LoggerCoreDiagnostics.MemoryCategory, LogLevel.None)
			.AddProvider(logs));

		EmitScriptedRun(new LoggerCoreDiagnostics(factory));

		Assert.Equal(
			[1300, 1301, 1302, 1700],
			logs.Entries.Select(static entry => entry.EventId.Id));
	}

	[Fact]
	public void CapabilityRefusalIsLoggedOncePerCapabilityAndOperation()
	{
		CapturingLoggerProvider logs = new();
		using ILoggerFactory factory = CreateFactory(logs);
		LoggerCoreDiagnostics diagnostics = new(factory);

		for (int attempt = 0; attempt < 3; attempt++)
		{
			diagnostics.CapabilityRefused("Client.Speed", "Speed.GetMultiplier",
				ClientCapabilityEvidenceReasonCode.Implementation, ClientCapabilityEvidenceState.Missing);
			diagnostics.CapabilityRefused("Client.Speed", "Speed.SetMultiplier",
				ClientCapabilityEvidenceReasonCode.Implementation, ClientCapabilityEvidenceState.Missing);
			diagnostics.CapabilityRefused("Client.UnsafeLuaExecution", "Lua.ExecuteUnsafe",
				ClientCapabilityEvidenceReasonCode.Policy, ClientCapabilityEvidenceState.Missing);
		}

		Assert.Equal(
			[
				"Capability Client.Speed refused Speed.GetMultiplier: the Implementation gate is Missing.",
				"Capability Client.Speed refused Speed.SetMultiplier: the Implementation gate is Missing.",
				"Capability Client.UnsafeLuaExecution refused Lua.ExecuteUnsafe: the Policy gate is Missing."
			],
			logs.Entries.Select(static entry => entry.Message));
		LoggerCoreDiagnostics nextActivation = new(factory);
		nextActivation.CapabilityRefused("Client.Speed", "Speed.GetMultiplier",
			ClientCapabilityEvidenceReasonCode.Implementation, ClientCapabilityEvidenceState.Missing);
		Assert.Equal(4, logs.Entries.Count);
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void DiagnosticEventsCarryNoAddressValueSymbolOrPath()
	{
		CapturingLoggerProvider logs = new();
		using ILoggerFactory factory = CreateFactory(logs);

		EmitScriptedRun(new LoggerCoreDiagnostics(factory));

		Assert.Equal(ScriptedEventCount, logs.Entries.Count);
		Assert.All(typeof(LoggerCoreDiagnostics).GetMethods(BindingFlags.Instance | BindingFlags.Public |
															 BindingFlags.DeclaredOnly)
				.SelectMany(static method => method.GetParameters()),
			static parameter => Assert.True(
				parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(int) ||
				parameter.ParameterType == typeof(long) || parameter.ParameterType == typeof(bool) ||
				parameter.ParameterType.IsEnum,
				$"{parameter.Member.Name}.{parameter.Name} has type {parameter.ParameterType.FullName}."));
		foreach (LogEntry entry in logs.Entries)
		{
			Assert.Null(entry.Exception);
			Assert.DoesNotMatch(HexAddress(), entry.Message);
			Assert.DoesNotMatch(PathLike(), entry.Message);
			Assert.DoesNotContain("player_health", entry.Message, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("readInteger", entry.Message, StringComparison.Ordinal);
			foreach (KeyValuePair<string, object?> field in entry.Fields)
			{
				Assert.True(field.Value is string or int or long or bool or Enum,
					$"Event {entry.EventId.Id} field {field.Key} has type {field.Value?.GetType().FullName}.");
			}
		}
	}

	private const int ScriptedEventCount = 13;

	/// <summary>
	///     Emits one event of each kind with the closed values Core passes (see Core's CoreDiagnosticsTests scripted run).
	/// </summary>
	private static void EmitScriptedRun(LoggerCoreDiagnostics diagnostics)
	{
		diagnostics.RuntimeSnapshotCaptured(17, CheatEngineArchitecture.X64, 8, 8, false);
		diagnostics.CapabilityRefused("Client.Hotkeys", "Hotkeys.Register",
			ClientCapabilityEvidenceReasonCode.Implementation, ClientCapabilityEvidenceState.Missing);
		diagnostics.TargetSelectionAdvanced(17, 2, "Processes.Attach", "PidChanged");
		diagnostics.PointerWidthMismatchRefused("Memory.ReadPrimitive", 8, 4);
		diagnostics.MemoryBatchCompleted("Memory.WritePrimitiveBatch", 12, 5, "Partial");
		diagnostics.TableGenerationAdvanced(17, 1);
		diagnostics.StaleRecordIdentifierRefused("Tables.SetActive", 1);
		diagnostics.RecordActivationNotApplied("Tables.SetActive", true, "RefusedByHost");
		diagnostics.SymbolRegistrationRejected("Inspection.RegisterSymbol", "AlreadyResolves");
		diagnostics.SymbolLeaseReleased(SymbolLeaseReleaseKind.Replaced);
		diagnostics.PatternScanCompleted(PatternScanScope.GlobalHostScanWithManagedFilter, 40, 10, true, 250, 3);
		diagnostics.LuaOperationCompleted("Lua.ExecuteUnsafe", "LuaError", 4, 36);
		diagnostics.CoreResourceCleanupFailed("SymbolRegistrationLease",
			"CheatEngine.Client.Results.CheatEngineOperationException");
	}

	private static ILoggerFactory CreateFactory(ILoggerProvider provider)
	{
		return LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
	}

	[GeneratedRegex(@"(?i)\b(0x)?[0-9a-f]{8,}\b")]
	private static partial Regex HexAddress();

	[GeneratedRegex(@"[A-Za-z]:\\|\\\\|/|\.ct\b|\.exe\b|\.dll\b")]
	private static partial Regex PathLike();

	private sealed record EventShape(int Id, string? Name, string Category, LogLevel Level);

	private sealed record LogEntry(
		string Category,
		EventId EventId,
		LogLevel Level,
		string Message,
		Exception? Exception,
		IReadOnlyList<KeyValuePair<string, object?>> Fields);

	private enum ThrowingStage
	{
		CreateLogger,
		IsEnabled,
		Log
	}

	private sealed class CapturingLoggerProvider : ILoggerProvider
	{
		private readonly Lock _gate = new();
		private readonly List<LogEntry> _entries = [];

		internal IReadOnlyList<LogEntry> Entries
		{
			get
			{
				lock (_gate)
				{
					return [.. _entries];
				}
			}
		}

		public ILogger CreateLogger(string categoryName)
		{
			return new CapturingLogger(this, categoryName);
		}

		public void Dispose()
		{
		}

		private void Add(LogEntry entry)
		{
			lock (_gate)
			{
				_entries.Add(entry);
			}
		}

		private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state)
				where TState : notnull
			{
				return null;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				return true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				IReadOnlyList<KeyValuePair<string, object?>> fields =
					state is IReadOnlyList<KeyValuePair<string, object?>> structured
						? [.. structured.Where(static field => field.Key != "{OriginalFormat}")]
						: [];
				owner.Add(new LogEntry(category, eventId, logLevel, formatter(state, exception), exception, fields));
			}
		}
	}

	private sealed class ThrowingLoggerProvider(ThrowingStage stage) : ILoggerProvider
	{
		private int _createLoggerCalls;
		private int _isEnabledCalls;
		private int _logCalls;

		internal int CreateLoggerCalls => Volatile.Read(ref _createLoggerCalls);

		internal int IsEnabledCalls => Volatile.Read(ref _isEnabledCalls);

		internal int LogCalls => Volatile.Read(ref _logCalls);

		public ILogger CreateLogger(string categoryName)
		{
			Interlocked.Increment(ref _createLoggerCalls);
			return stage == ThrowingStage.CreateLogger
				? throw new InvalidOperationException("The logging provider could not create a logger.")
				: new ThrowingLogger(this);
		}

		public void Dispose()
		{
		}

		private sealed class ThrowingLogger(ThrowingLoggerProvider owner) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state)
				where TState : notnull
			{
				return null;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				Interlocked.Increment(ref owner._isEnabledCalls);
				return owner.ThrowsFrom(ThrowingStage.IsEnabled)
					? throw new InvalidOperationException("The logging filter failed.")
					: true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				Interlocked.Increment(ref owner._logCalls);
				throw new InvalidOperationException("The logging provider failed.");
			}
		}

		private bool ThrowsFrom(ThrowingStage candidate)
		{
			return stage == candidate;
		}
	}
}
