using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     Logger-free sink for the bounded, redacted Core diagnostic events (audit ch.24, A11-03, A24-12 to A24-17).
/// </summary>
/// <remarks>
///     <para>
///         Core has no logging dependency: the dependency-injection package implements this sink over
///         <c>Microsoft.Extensions.Logging</c> with source-generated events and one category per domain. Every parameter is
///         a count, an epoch, a width, a stable operation name or a closed reason name: an event never carries an address,
///         a value, a symbol name, a path, a script body, an exception message or a failure object.
///     </para>
///     <para>
///         Core emits after the dispatched Cheat Engine work returned, never inside a dispatched callback, and every emit
///         goes through <see cref="GuardedCoreDiagnostics" />, so a throwing sink never changes an operation result.
///     </para>
/// </remarks>
internal interface ICoreDiagnostics
{
	/// <summary>A runtime snapshot was captured (EventId 1000).</summary>
	public void RuntimeSnapshotCaptured(long activationEpoch, CheatEngineArchitecture targetArchitecture,
		int processPointerBytes, int configuredPointerBytes, bool pointerSizeMismatch);

	/// <summary>A capability gate refused an operation (EventId 1001).</summary>
	public void CapabilityRefused(string capability, string operation, ClientCapabilityEvidenceReasonCode gate,
		ClientCapabilityEvidenceState gateState);

	/// <summary>The target-selection epoch advanced (EventId 1100).</summary>
	public void TargetSelectionAdvanced(long activationEpoch, long selectionEpoch, string operation, string reason);

	/// <summary>A pointer-typed operation was refused on a configured/process width mismatch (EventId 1200).</summary>
	public void PointerWidthMismatchRefused(string operation, int processPointerBytes, int configuredPointerBytes);

	/// <summary>A primitive batch completed, fully or partially (EventId 1201).</summary>
	public void MemoryBatchCompleted(string operation, int requested, int completed, string effectState);

	/// <summary>A trusted table load reached Cheat Engine and advanced the table generation (EventId 1300).</summary>
	public void TableGenerationAdvanced(long activationEpoch, long tableGeneration);

	/// <summary>A record identifier captured before the last trusted load was refused (EventId 1301).</summary>
	public void StaleRecordIdentifierRefused(string operation, long tableGeneration);

	/// <summary>An activation request was refused, pending or indeterminate (EventId 1302).</summary>
	public void RecordActivationNotApplied(string operation, bool requestedState, string status);

	/// <summary>A symbol registration was rejected by the collision preflight (EventId 1400).</summary>
	public void SymbolRegistrationRejected(string operation, string reason);

	/// <summary>A symbol lease release attempt ended (EventId 1401).</summary>
	public void SymbolLeaseReleased(SymbolLeaseReleaseKind kind);

	/// <summary>A pattern scan ended with metrics (EventId 1500).</summary>
	public void PatternScanCompleted(PatternScanScope scope, long hostResultCount, int materializedCount, bool truncated,
		long hostScanMilliseconds, long copyMilliseconds);

	/// <summary>A Lua operation ended (EventId 1600); the script length is non-zero for unsafe Lua only.</summary>
	public void LuaOperationCompleted(string operation, string outcome, long elapsedMilliseconds, int scriptLength);

	/// <summary>A Client-owned resource failed to release (EventId 1700).</summary>
	public void CoreResourceCleanupFailed(string componentType, string exceptionType);

	/// <summary>A Client lease release attempt ended (EventId 1701); only the kind, operation and effect are logged.</summary>
	public void LeaseReleased(string operation, LeaseReleaseKind kind, CheatEngineHostEffect hostEffect);
}

/// <summary>The sink used when no diagnostics are configured.</summary>
internal sealed class NullCoreDiagnostics : ICoreDiagnostics
{
	private NullCoreDiagnostics()
	{
	}

	internal static NullCoreDiagnostics Instance
	{
		get;
	} = new();

	public void RuntimeSnapshotCaptured(long activationEpoch, CheatEngineArchitecture targetArchitecture,
		int processPointerBytes, int configuredPointerBytes, bool pointerSizeMismatch)
	{
	}

	public void CapabilityRefused(string capability, string operation, ClientCapabilityEvidenceReasonCode gate,
		ClientCapabilityEvidenceState gateState)
	{
	}

	public void TargetSelectionAdvanced(long activationEpoch, long selectionEpoch, string operation, string reason)
	{
	}

	public void PointerWidthMismatchRefused(string operation, int processPointerBytes, int configuredPointerBytes)
	{
	}

	public void MemoryBatchCompleted(string operation, int requested, int completed, string effectState)
	{
	}

	public void TableGenerationAdvanced(long activationEpoch, long tableGeneration)
	{
	}

	public void StaleRecordIdentifierRefused(string operation, long tableGeneration)
	{
	}

	public void RecordActivationNotApplied(string operation, bool requestedState, string status)
	{
	}

	public void SymbolRegistrationRejected(string operation, string reason)
	{
	}

	public void SymbolLeaseReleased(SymbolLeaseReleaseKind kind)
	{
	}

	public void PatternScanCompleted(PatternScanScope scope, long hostResultCount, int materializedCount, bool truncated,
		long hostScanMilliseconds, long copyMilliseconds)
	{
	}

	public void LuaOperationCompleted(string operation, string outcome, long elapsedMilliseconds, int scriptLength)
	{
	}

	public void CoreResourceCleanupFailed(string componentType, string exceptionType)
	{
	}

	public void LeaseReleased(string operation, LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
	{
	}
}

/// <summary>Contains every sink failure so diagnostics can never change a Client operation or cleanup result (A24-22).</summary>
internal sealed class GuardedCoreDiagnostics(ICoreDiagnostics inner) : ICoreDiagnostics
{
	private readonly ICoreDiagnostics _inner = inner ?? throw new ArgumentNullException(nameof(inner));

	/// <summary>Returns the null sink for <see langword="null" />, or a guarded sink that swallows every sink failure.</summary>
	internal static ICoreDiagnostics Wrap(ICoreDiagnostics? diagnostics)
	{
		return diagnostics switch
		{
			null => NullCoreDiagnostics.Instance,
			NullCoreDiagnostics or GuardedCoreDiagnostics => diagnostics,
			_ => new GuardedCoreDiagnostics(diagnostics)
		};
	}

	public void RuntimeSnapshotCaptured(long activationEpoch, CheatEngineArchitecture targetArchitecture,
		int processPointerBytes, int configuredPointerBytes, bool pointerSizeMismatch)
	{
		try
		{
			_inner.RuntimeSnapshotCaptured(activationEpoch, targetArchitecture, processPointerBytes,
				configuredPointerBytes, pointerSizeMismatch);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void CapabilityRefused(string capability, string operation, ClientCapabilityEvidenceReasonCode gate,
		ClientCapabilityEvidenceState gateState)
	{
		try
		{
			_inner.CapabilityRefused(capability, operation, gate, gateState);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void TargetSelectionAdvanced(long activationEpoch, long selectionEpoch, string operation, string reason)
	{
		try
		{
			_inner.TargetSelectionAdvanced(activationEpoch, selectionEpoch, operation, reason);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void PointerWidthMismatchRefused(string operation, int processPointerBytes, int configuredPointerBytes)
	{
		try
		{
			_inner.PointerWidthMismatchRefused(operation, processPointerBytes, configuredPointerBytes);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void MemoryBatchCompleted(string operation, int requested, int completed, string effectState)
	{
		try
		{
			_inner.MemoryBatchCompleted(operation, requested, completed, effectState);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void TableGenerationAdvanced(long activationEpoch, long tableGeneration)
	{
		try
		{
			_inner.TableGenerationAdvanced(activationEpoch, tableGeneration);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void StaleRecordIdentifierRefused(string operation, long tableGeneration)
	{
		try
		{
			_inner.StaleRecordIdentifierRefused(operation, tableGeneration);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void RecordActivationNotApplied(string operation, bool requestedState, string status)
	{
		try
		{
			_inner.RecordActivationNotApplied(operation, requestedState, status);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void SymbolRegistrationRejected(string operation, string reason)
	{
		try
		{
			_inner.SymbolRegistrationRejected(operation, reason);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void SymbolLeaseReleased(SymbolLeaseReleaseKind kind)
	{
		try
		{
			_inner.SymbolLeaseReleased(kind);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void PatternScanCompleted(PatternScanScope scope, long hostResultCount, int materializedCount, bool truncated,
		long hostScanMilliseconds, long copyMilliseconds)
	{
		try
		{
			_inner.PatternScanCompleted(scope, hostResultCount, materializedCount, truncated, hostScanMilliseconds,
				copyMilliseconds);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void LuaOperationCompleted(string operation, string outcome, long elapsedMilliseconds, int scriptLength)
	{
		try
		{
			_inner.LuaOperationCompleted(operation, outcome, elapsedMilliseconds, scriptLength);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the operation result.
		}
	}

	public void CoreResourceCleanupFailed(string componentType, string exceptionType)
	{
		try
		{
			_inner.CoreResourceCleanupFailed(componentType, exceptionType);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the cleanup result.
		}
	}

	public void LeaseReleased(string operation, LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
	{
		try
		{
			_inner.LeaseReleased(operation, kind, hostEffect);
		}
		catch (Exception)
		{
			// Deliberately ignored: diagnostics must never change the release outcome.
		}
	}
}
