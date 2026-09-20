using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Maps implementation exceptions to the stable public client result vocabulary.</summary>
internal static class CoreFailureFactory
{
	internal static CheatEngineFailure Cancelled(string operation)
	{
		return new CheatEngineFailure(
			CheatEngineFailureKind.Cancelled,
			operation,
			"The operation was cancelled before Cheat Engine work began.");
	}

	internal static CheatEngineFailure FromException(string operation, Exception exception)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(exception);

		return new CheatEngineFailure(GetKind(exception), operation, exception.Message, exception);
	}

	internal static CheatEngineFailure Lifecycle(string operation, string message, Exception? exception = null)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, operation, message, exception);
	}

	private static CheatEngineFailureKind GetKind(Exception exception)
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
