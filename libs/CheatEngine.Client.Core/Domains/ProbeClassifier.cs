using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Classifies one read-only host probe into capability evidence, by exception type only.</summary>
/// <remarks>
///     <para>
///         A probe never throws an SDK exception to its caller: the SDK <c>Engine*Exception</c> types map to their
///         evidence state, and <see cref="LuaException" /> maps to <see cref="ProbeResult{T}.Faulted" /> with
///         <see cref="IndistinguishableLuaFailureReason" />. A throwing-form generated binding of CheatEngine.SDK 1.0.0
///         raises <see cref="LuaException" /> for an undefined global, a raised Lua error and an unexpected result type
///         alike, and the Client can neither parse the message (error text depends on the host) nor touch the Lua stack
///         (ADR-01) to separate them.
///     </para>
///     <para>
///         Lifecycle and consumer exceptions are deliberately not caught here (for example the
///         <see cref="InvalidOperationException" /> of a detached SDK runtime): the calling domain returns them through the
///         <see cref="CheatEngine.Client.Core.Infrastructure.SdkBoundary" /> rules or lets a Client lifecycle exception propagate.
///     </para>
/// </remarks>
internal static class ProbeClassifier
{
	/// <summary>The evidence reason of a <see cref="LuaException" /> raised by a generated CheatEngine.SDK binding.</summary>
	internal const string IndistinguishableLuaFailureReason =
		"The Cheat Engine runtime probe failed with an undefined global, a raised Lua error or an unexpected result " +
		"type (indistinguishable with CheatEngine.SDK 1.0.0 generated bindings).";

	/// <summary>Runs one probe and classifies its value or its SDK exception.</summary>
	internal static ProbeResult<T> Probe<T>(Func<T> probe)
	{
		ArgumentNullException.ThrowIfNull(probe);
		try
		{
			return ProbeResult<T>.Available(probe());
		}
		catch (EngineGlobalUnavailableException)
		{
			return ProbeResult<T>.MissingGlobal();
		}
		catch (EngineCapabilityUnavailableException)
		{
			return ProbeResult<T>.MissingCapability();
		}
		catch (EngineMarshallingException)
		{
			return ProbeResult<T>.Malformed("The Cheat Engine runtime probe returned a malformed result.");
		}
		catch (EngineException exception)
		{
			return ProbeResult<T>.Faulted(
				$"The Cheat Engine runtime probe failed with {exception.GetType().Name}.");
		}
		catch (LuaException)
		{
			return ProbeResult<T>.Faulted(IndistinguishableLuaFailureReason);
		}
	}
}
