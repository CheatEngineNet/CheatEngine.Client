using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     Carries a classified Lua admission refusal out of Client-internal SDK work that has no failure channel of its own.
/// </summary>
/// <remarks>
///     <para>
///         It never leaves Core: <see cref="SdkBoundary" /> translates it like any SDK fault, and
///         <see cref="CoreFailureFactory" /> reports the carried <see cref="CheatEngineFailure.Kind" />
///         (<see cref="CheatEngineFailureKind.ActivationExpired" />, <see cref="CheatEngineFailureKind.InvalidState" /> or
///         <see cref="CheatEngineFailureKind.RuntimeChanged" />), so a refused admission is never reported as a
///         rejection.
///     </para>
///     <para>
///         No Core path raises it any more: the Address List mutations, whose parent-chain read was its only producer,
///         are CheatEngine.SDK commands that acquire their own admission. <see cref="CoreFailureFactory" /> still
///         classifies it, which is why the type remains.
///     </para>
/// </remarks>
internal sealed class LuaAdmissionRefusedException : Exception
{
	/// <summary>Creates the carrier of a classified admission refusal.</summary>
	/// <param name="failure">The refusal classified by <see cref="LuaAdmission" />.</param>
	internal LuaAdmissionRefusedException(CheatEngineFailure failure)
		: base(failure.Message)
	{
		Failure = failure;
	}

	/// <summary>Gets the classified refusal.</summary>
	internal CheatEngineFailure Failure
	{
		get;
	}
}
