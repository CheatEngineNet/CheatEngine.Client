using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Maps implementation exceptions to the stable public client result vocabulary.</summary>
/// <remarks>
///     <para>
///         Classification uses exception types and the SDK's own failure categories only, never exception or Lua message
///         text: the text of a Cheat Engine or Lua error depends on the host language and version.
///     </para>
///     <para>
///         Every <see cref="EngineException" /> is classified by its <see cref="EngineException.Kind" />, exhaustively
///         (<see cref="FromEngineFailureKind" />), and a <see cref="MemoryScanException" /> by its
///         <see cref="MemoryScanException.FailureKind" /> (<see cref="FromMemoryScanFailureKind" />). A category this
///         Client version does not know is <see cref="CheatEngineFailureKind.Unknown" />, and the SDK mapping contract
///         tests fail until it is mapped.
///     </para>
/// </remarks>
internal static class CoreFailureFactory
{
	/// <summary>Creates the failure for a cancellation observed before any Cheat Engine work was dispatched.</summary>
	internal static CheatEngineFailure Cancelled(string operation)
	{
		return CancellationMapping.BeforeNativeCall(operation);
	}

	/// <summary>Maps an exception whose Cheat Engine side effect is unknown.</summary>
	internal static CheatEngineFailure FromException(string operation, Exception exception)
	{
		return FromException(operation, exception, CheatEngineHostEffect.Unknown);
	}

	/// <summary>Maps an exception and records what is known about the Cheat Engine side effect.</summary>
	/// <remarks>
	///     A failed ownership handoff (<see cref="EngineResourceHandoffException" />,
	///     <see cref="SymbolRegistrationHandoffException" />, <see cref="SymbolListRegistrationHandoffException" />)
	///     reports an effect Cheat Engine already accepted and that the SDK's single compensation may not have removed, so
	///     an otherwise unknown effect is recorded as <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />. A known
	///     effect passed by the caller is kept.
	/// </remarks>
	internal static CheatEngineFailure FromException(string operation, Exception exception,
		CheatEngineHostEffect hostEffect)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(exception);

		string message = string.IsNullOrWhiteSpace(exception.Message)
			? $"The operation failed with {exception.GetType().Name}."
			: exception.Message;
		if (hostEffect == CheatEngineHostEffect.Unknown && IsOwnershipHandoffFailure(exception))
		{
			hostEffect = CheatEngineHostEffect.CleanupUnconfirmed;
		}

		return new CheatEngineFailure(GetKind(exception), operation, message, exception, hostEffect);
	}

	internal static CheatEngineFailure Lifecycle(string operation, string message, Exception? exception = null)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, operation, message, exception);
	}

	/// <summary>Returns the same failure with a more precise host effect.</summary>
	internal static CheatEngineFailure WithHostEffect(CheatEngineFailure failure, CheatEngineHostEffect hostEffect)
	{
		return failure.HostEffect == hostEffect
			? failure
			: new CheatEngineFailure(failure.Kind, failure.Operation, failure.Message, failure.Exception, hostEffect);
	}

	/// <summary>Classifies an exception by its type and, for SDK exceptions, by the SDK's failure category.</summary>
	/// <remarks>
	///     Internal so the consumer-contract tests can prove that every public exception type of the consumed
	///     CheatEngine.SDK version maps to a known kind (Q48). <see cref="MemoryScanStateException" /> and
	///     <see cref="MemoryScanException" /> derive from <see cref="InvalidOperationException" />, so they precede that
	///     arm.
	/// </remarks>
	internal static CheatEngineFailureKind GetKind(Exception exception)
	{
		return exception switch
		{
			CheatEngineActivationExpiredException => CheatEngineFailureKind.ActivationExpired,
			CheatEngineClientLifecycleException => CheatEngineFailureKind.InvalidState,
			EngineException engine => FromEngineFailureKind(engine.Kind),
			LuaException => CheatEngineFailureKind.LuaError,
			// The session is busy (a Cheat Engine call is still running) or not in a state that accepts the call.
			MemoryScanStateException => CheatEngineFailureKind.InvalidState,
			MemoryScanException scan => FromMemoryScanFailureKind(scan.FailureKind),
			ObjectDisposedException => CheatEngineFailureKind.InvalidState,
			ArgumentException => CheatEngineFailureKind.OperationRejected,
			InvalidOperationException => CheatEngineFailureKind.OperationRejected,
			_ => CheatEngineFailureKind.Unknown
		};
	}

	/// <summary>Maps an SDK engine failure category to the Client kind; exhaustive over the consumed SDK.</summary>
	/// <param name="kind">The category reported by <see cref="EngineException.Kind" />.</param>
	/// <returns>
	///     The Client kind; <see cref="CheatEngineFailureKind.Unknown" /> for a category this Client does not know.
	/// </returns>
	internal static CheatEngineFailureKind FromEngineFailureKind(EngineFailureKind kind)
	{
		return kind switch
		{
			EngineFailureKind.ExpectedOperationFailure => CheatEngineFailureKind.OperationRejected,
			EngineFailureKind.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			EngineFailureKind.CapabilityUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			EngineFailureKind.ProtectedLuaFailure => CheatEngineFailureKind.LuaError,
			EngineFailureKind.BindingFailure => CheatEngineFailureKind.BindingError,
			EngineFailureKind.MarshallingFailure => CheatEngineFailureKind.InvalidHostResult,
			EngineFailureKind.TargetIdentityUnavailable => CheatEngineFailureKind.TargetIdentityUnavailable,
			EngineFailureKind.TargetIdentityMismatch => CheatEngineFailureKind.TargetChanged,
			_ => CheatEngineFailureKind.Unknown
		};
	}

	/// <summary>Maps an SDK memory-scan failure category to the Client kind; exhaustive over the consumed SDK.</summary>
	/// <param name="kind">The category reported by <see cref="MemoryScanException.FailureKind" />.</param>
	/// <returns>
	///     The Client kind; <see cref="CheatEngineFailureKind.Unknown" /> for a category this Client does not know.
	/// </returns>
	internal static CheatEngineFailureKind FromMemoryScanFailureKind(MemoryScanFailureKind kind)
	{
		return kind switch
		{
			MemoryScanFailureKind.MissingCapability => CheatEngineFailureKind.CapabilityUnavailable,
			MemoryScanFailureKind.LuaError => CheatEngineFailureKind.LuaError,
			MemoryScanFailureKind.UnexpectedResult => CheatEngineFailureKind.InvalidHostResult,
			MemoryScanFailureKind.RuntimeInvalidated => CheatEngineFailureKind.RuntimeChanged,
			MemoryScanFailureKind.TargetIdentityUnavailable => CheatEngineFailureKind.TargetIdentityUnavailable,
			MemoryScanFailureKind.TargetIdentityMismatch => CheatEngineFailureKind.TargetChanged,
			_ => CheatEngineFailureKind.Unknown
		};
	}

	/// <summary>Whether the exception reports a failed ownership handoff after Cheat Engine accepted the effect.</summary>
	private static bool IsOwnershipHandoffFailure(Exception exception)
	{
		return exception is EngineResourceHandoffException or SymbolRegistrationHandoffException
			or SymbolListRegistrationHandoffException;
	}
}
