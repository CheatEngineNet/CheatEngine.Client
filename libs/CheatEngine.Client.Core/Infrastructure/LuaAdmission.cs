using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     The single point where Core asks CheatEngine.SDK to admit a Lua operation, and classifies a refusal from the SDK's
///     factual <see cref="LuaAdmissionStatus" /> instead of an exception message.
/// </summary>
/// <remarks>
///     <para>
///         Only <see cref="LuaAdmissionStatus.Admitted" /> is a success. Every refusal is reported with
///         <see cref="CheatEngineHostEffect.NotStarted" />, because the SDK decides admission before the host's state
///         provider or any Lua call runs:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <c>Detached</c> and <c>TransitionInProgress</c>:
///                 <see cref="CheatEngineFailureKind.ActivationExpired" /> (the plugin is disabled, or its lifecycle
///                 transition is draining);
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>ExternalStateReset</c>: <see cref="CheatEngineFailureKind.RuntimeChanged" /> (Cheat Engine replaced
///                 its Lua state outside the plugin's control);
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>ThreadNotAdmitted</c> and <c>NoStateForThread</c>:
///                 <see cref="CheatEngineFailureKind.InvalidState" /> (a Client bug: Core only runs Lua work on Cheat
///                 Engine's main thread, ADR-07);
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>Unknown</c> and any value this Client version does not know:
///                 <see cref="CheatEngineFailureKind.InvalidState" />, never a success.
///             </description>
///         </item>
///     </list>
///     <para>
///         A refusal is never <see cref="CheatEngineFailureKind.OperationRejected" />: nothing reached Cheat Engine.
///     </para>
///     <para>
///         It classifies only the admissions Core asks for itself, such as unsafe Lua execution. A CheatEngine.SDK
///         command that acquires its own admission, such as an <c>AddressListMutations</c> command or a
///         <c>CheatTableFiles</c> call, raises a plain <see cref="InvalidOperationException" /> when it is refused:
///         <see cref="SdkBoundary" /> reports it as <see cref="CheatEngineFailureKind.OperationRejected" /> with an
///         unknown effect, unless the activation ended (<see cref="CheatEngineActivationExpiredException" />) or the SDK
///         detected an external Lua state reset (<see cref="CheatEngineFailureKind.RuntimeChanged" />).
///     </para>
/// </remarks>
internal static class LuaAdmission
{
	private const string DetachedMessage =
		"CheatEngine.SDK refused the Lua operation (Detached): the plugin is not enabled, so the Client activation has " +
		"ended.";

	private const string TransitionMessage =
		"CheatEngine.SDK refused the Lua operation (TransitionInProgress): a plugin lifecycle transition is draining, so " +
		"the Client activation is ending.";

	private const string ExternalResetMessage =
		"CheatEngine.SDK refused the Lua operation (ExternalStateReset): Cheat Engine replaced its Lua state outside the " +
		"plugin's control. Disable and re-enable the plugin to recover.";

	private const string ThreadNotAdmittedMessage =
		"Client bug: called off the main thread. CheatEngine.SDK refused the Lua operation (ThreadNotAdmitted).";

	private const string NoStateForThreadMessage =
		"Client bug: called off the main thread. Cheat Engine provided no Lua state for the calling thread " +
		"(NoStateForThread).";

	private const string UnknownMessage =
		"CheatEngine.SDK reported no recognized Lua admission outcome; the operation was not started.";

	/// <summary>Asks CheatEngine.SDK to admit a Lua operation on the calling thread.</summary>
	/// <param name="operation">The public Client operation name used in the failure.</param>
	/// <param name="admitted">The admitted operation on success; dispose it before returning to Cheat Engine.</param>
	/// <param name="failure">The classified refusal when the operation was not admitted.</param>
	/// <returns><see langword="true" /> only when the SDK reported <see cref="LuaAdmissionStatus.Admitted" />.</returns>
	internal static bool TryAcquire(string operation, out LuaRuntimeOperation admitted, out CheatEngineFailure failure)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);

		// The SDK hands out an admitted operation only with Admitted, the only status classified as a success; for every
		// other status the operation is the default value, which owns no admission.
		return TryClassify(LuaRuntime.TryAcquireOperationWithOutcome(out admitted), operation, out failure);
	}

	/// <summary>Classifies an SDK admission outcome; the seam that the mapping-totality tests exercise.</summary>
	/// <param name="status">The factual outcome reported by CheatEngine.SDK.</param>
	/// <param name="operation">The public Client operation name used in the failure.</param>
	/// <param name="failure">The classified refusal; the default value on success.</param>
	/// <returns><see langword="true" /> only for <see cref="LuaAdmissionStatus.Admitted" />.</returns>
	internal static bool TryClassify(LuaAdmissionStatus status, string operation, out CheatEngineFailure failure)
	{
		failure = status switch
		{
			LuaAdmissionStatus.Admitted => default,
			LuaAdmissionStatus.Detached => Refused(CheatEngineFailureKind.ActivationExpired, operation, DetachedMessage),
			LuaAdmissionStatus.TransitionInProgress => Refused(CheatEngineFailureKind.ActivationExpired, operation,
				TransitionMessage),
			LuaAdmissionStatus.ExternalStateReset => Refused(CheatEngineFailureKind.RuntimeChanged, operation,
				ExternalResetMessage),
			LuaAdmissionStatus.ThreadNotAdmitted => Refused(CheatEngineFailureKind.InvalidState, operation,
				ThreadNotAdmittedMessage),
			LuaAdmissionStatus.NoStateForThread => Refused(CheatEngineFailureKind.InvalidState, operation,
				NoStateForThreadMessage),
			LuaAdmissionStatus.Unknown => Refused(CheatEngineFailureKind.InvalidState, operation, UnknownMessage),
			_ => Refused(CheatEngineFailureKind.InvalidState, operation, UnknownMessage)
		};
		return status == LuaAdmissionStatus.Admitted;
	}

	private static CheatEngineFailure Refused(CheatEngineFailureKind kind, string operation, string message)
	{
		return new CheatEngineFailure(kind, operation, message, null, CheatEngineHostEffect.NotStarted);
	}
}
