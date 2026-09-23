using System.Collections.Concurrent;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Writes the Core diagnostic events of one activation to <c>Microsoft.Extensions.Logging</c>.</summary>
/// <remarks>
///     <para>
///         Each domain logs under its own category (<c>CheatEngine.Client.Runtime</c>, <c>.Processes</c>, <c>.Memory</c>,
///         <c>.Tables</c>, <c>.Inspection</c>, <c>.Scanning</c>, <c>.Lua</c>, <c>.Lifetime</c>), so collection is chosen
///         with the standard <c>Logging:LogLevel</c> filters; no Client option controls it.
///     </para>
///     <para>
///         A logger or provider that throws, from <see cref="ILogger.IsEnabled" />, from <see cref="ILogger.Log{TState}" />
///         or while a logger is created, is contained here and never reaches a Client operation, a dispatched callback or
///         cleanup (audit A24-22). A capability refusal is logged once per capability and operation for the activation
///         that owns this sink.
///     </para>
/// </remarks>
internal sealed class LoggerCoreDiagnostics : ICoreDiagnostics
{
	/// <summary>The category of runtime snapshot and capability-gate events.</summary>
	internal const string RuntimeCategory = "CheatEngine.Client.Runtime";

	/// <summary>The category of target-selection events.</summary>
	internal const string ProcessesCategory = "CheatEngine.Client.Processes";

	/// <summary>The category of memory events.</summary>
	internal const string MemoryCategory = "CheatEngine.Client.Memory";

	/// <summary>The category of Address List events.</summary>
	internal const string TablesCategory = "CheatEngine.Client.Tables";

	/// <summary>The category of symbol events.</summary>
	internal const string InspectionCategory = "CheatEngine.Client.Inspection";

	/// <summary>The category of pattern-scan events.</summary>
	internal const string ScanningCategory = "CheatEngine.Client.Scanning";

	/// <summary>The category of Lua events.</summary>
	internal const string LuaCategory = "CheatEngine.Client.Lua";

	/// <summary>The category of Client-owned resource cleanup events.</summary>
	internal const string LifetimeCategory = "CheatEngine.Client.Lifetime";

	private readonly ConcurrentDictionary<(string Capability, string Operation), byte> _refusalsLogged = new();
	private readonly ILogger _inspection;
	private readonly ILogger _lifetime;
	private readonly ILogger _lua;
	private readonly ILogger _memory;
	private readonly ILogger _processes;
	private readonly ILogger _runtime;
	private readonly ILogger _scanning;
	private readonly ILogger _tables;

	/// <summary>Creates the per-domain loggers of one activation.</summary>
	/// <param name="loggerFactory">The logger factory of the activation's service provider.</param>
	internal LoggerCoreDiagnostics(ILoggerFactory loggerFactory)
	{
		ArgumentNullException.ThrowIfNull(loggerFactory);
		_runtime = CreateLogger(loggerFactory, RuntimeCategory);
		_processes = CreateLogger(loggerFactory, ProcessesCategory);
		_memory = CreateLogger(loggerFactory, MemoryCategory);
		_tables = CreateLogger(loggerFactory, TablesCategory);
		_inspection = CreateLogger(loggerFactory, InspectionCategory);
		_scanning = CreateLogger(loggerFactory, ScanningCategory);
		_lua = CreateLogger(loggerFactory, LuaCategory);
		_lifetime = CreateLogger(loggerFactory, LifetimeCategory);
	}

	public void RuntimeSnapshotCaptured(long activationEpoch, CheatEngineArchitecture targetArchitecture,
		int processPointerBytes, int configuredPointerBytes, bool pointerSizeMismatch)
	{
		try
		{
			ClientCoreDiagnosticsLog.RuntimeSnapshotCaptured(_runtime, activationEpoch, targetArchitecture,
				processPointerBytes, configuredPointerBytes, pointerSizeMismatch);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void CapabilityRefused(string capability, string operation, ClientCapabilityEvidenceReasonCode gate,
		ClientCapabilityEvidenceState gateState)
	{
		try
		{
			if (_refusalsLogged.TryAdd((capability, operation), 0))
			{
				ClientCoreDiagnosticsLog.CapabilityRefused(_runtime, capability, operation, gate, gateState);
			}
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void TargetSelectionAdvanced(long activationEpoch, long selectionEpoch, string operation, string reason)
	{
		try
		{
			ClientCoreDiagnosticsLog.TargetSelectionAdvanced(_processes, activationEpoch, selectionEpoch, operation,
				reason);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void PointerWidthMismatchRefused(string operation, int processPointerBytes, int configuredPointerBytes)
	{
		try
		{
			ClientCoreDiagnosticsLog.PointerWidthMismatchRefused(_memory, operation, processPointerBytes,
				configuredPointerBytes);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void MemoryBatchCompleted(string operation, int requested, int completed, string effectState)
	{
		try
		{
			ClientCoreDiagnosticsLog.MemoryBatchCompleted(_memory, operation, requested, completed, effectState);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void TableGenerationAdvanced(long activationEpoch, long tableGeneration)
	{
		try
		{
			ClientCoreDiagnosticsLog.TableGenerationAdvanced(_tables, activationEpoch, tableGeneration);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void StaleRecordIdentifierRefused(string operation, long tableGeneration)
	{
		try
		{
			ClientCoreDiagnosticsLog.StaleRecordIdentifierRefused(_tables, operation, tableGeneration);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void RecordActivationNotApplied(string operation, bool requestedState, string status)
	{
		try
		{
			ClientCoreDiagnosticsLog.RecordActivationNotApplied(_tables, operation, requestedState, status);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void SymbolRegistrationRejected(string operation, string reason)
	{
		try
		{
			ClientCoreDiagnosticsLog.SymbolRegistrationRejected(_inspection, operation, reason);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void SymbolLeaseReleased(SymbolLeaseReleaseKind kind)
	{
		try
		{
			ClientCoreDiagnosticsLog.SymbolLeaseReleased(_inspection, kind);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void PatternScanCompleted(PatternScanScope scope, int hostMatchCount, int materializedCount, bool truncated,
		long hostScanMilliseconds, long copyMilliseconds)
	{
		try
		{
			ClientCoreDiagnosticsLog.PatternScanCompleted(_scanning, scope, hostMatchCount, materializedCount, truncated,
				hostScanMilliseconds, copyMilliseconds);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void LuaOperationCompleted(string operation, string outcome, long elapsedMilliseconds, int scriptLength)
	{
		try
		{
			ClientCoreDiagnosticsLog.LuaOperationCompleted(_lua, operation, outcome, elapsedMilliseconds, scriptLength);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes a Client result (A24-22).
		}
	}

	public void CoreResourceCleanupFailed(string componentType, string exceptionType)
	{
		try
		{
			ClientCoreDiagnosticsLog.CoreResourceCleanupFailed(_lifetime, componentType, exceptionType);
		}
		catch (Exception)
		{
			// Deliberately ignored: a logging provider fault never changes the cleanup result (A24-22).
		}
	}

	/// <summary>Creates a category logger; a provider that fails here leaves that category silent instead of failing enable.</summary>
	private static ILogger CreateLogger(ILoggerFactory loggerFactory, string category)
	{
		try
		{
			return loggerFactory.CreateLogger(category);
		}
		catch (Exception)
		{
			return NullLogger.Instance;
		}
	}
}
