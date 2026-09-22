using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Maps implementation exceptions to the stable public client result vocabulary.</summary>
/// <remarks>
///     Classification uses exception types only, never exception or Lua message text: the text of a Cheat Engine or Lua
///     error depends on the host language and version.
/// </remarks>
internal static class CoreFailureFactory
{
	/// <summary>Creates the failure for a cancellation observed before any Cheat Engine work was dispatched.</summary>
	internal static CheatEngineFailure Cancelled(string operation)
	{
		return new CheatEngineFailure(
			CheatEngineFailureKind.Cancelled,
			operation,
			"The operation was cancelled before Cheat Engine work began.",
			null,
			CheatEngineHostEffect.NotStarted);
	}

	/// <summary>Maps an exception whose Cheat Engine side effect is unknown.</summary>
	internal static CheatEngineFailure FromException(string operation, Exception exception)
	{
		return FromException(operation, exception, CheatEngineHostEffect.Unknown);
	}

	/// <summary>Maps an exception and records what is known about the Cheat Engine side effect.</summary>
	internal static CheatEngineFailure FromException(string operation, Exception exception,
		CheatEngineHostEffect hostEffect)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(exception);

		string message = string.IsNullOrWhiteSpace(exception.Message)
			? $"The operation failed with {exception.GetType().Name}."
			: exception.Message;
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

	/// <summary>Classifies an exception by its type.</summary>
	/// <remarks>
	///     Internal so the consumer-contract tests can prove that every public exception type of the consumed
	///     CheatEngine.SDK version maps to a known kind (Q48).
	/// </remarks>
	internal static CheatEngineFailureKind GetKind(Exception exception)
	{
		return exception switch
		{
			CheatEngineActivationExpiredException => CheatEngineFailureKind.ActivationExpired,
			CheatEngineClientLifecycleException => CheatEngineFailureKind.InvalidState,
			EngineCapabilityUnavailableException => CheatEngineFailureKind.CapabilityUnavailable,
			EngineGlobalUnavailableException => CheatEngineFailureKind.CapabilityUnavailable,
			EngineOperationFailedException => CheatEngineFailureKind.OperationRejected,
			EngineLuaException => CheatEngineFailureKind.LuaError,
			LuaException => CheatEngineFailureKind.LuaError,
			EngineBindingException => CheatEngineFailureKind.BindingError,
			EngineMarshallingException => CheatEngineFailureKind.InvalidHostResult,
			ObjectDisposedException => CheatEngineFailureKind.InvalidState,
			ArgumentException => CheatEngineFailureKind.OperationRejected,
			InvalidOperationException => CheatEngineFailureKind.OperationRejected,
			_ => CheatEngineFailureKind.Unknown
		};
	}
}
