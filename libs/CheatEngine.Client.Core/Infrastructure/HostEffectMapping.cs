using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Objects;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Maps the effect state that CheatEngine.SDK reports for an effectful operation to the Client vocabulary.</summary>
/// <remarks>
///     <para>
///         Every value of <see cref="EngineEffectState" /> has one Client counterpart: <c>NotStarted</c> stays
///         <see cref="CheatEngineHostEffect.NotStarted" />, the documented negative result <c>NotApplied</c> stays
///         <see cref="CheatEngineHostEffect.NotApplied" />, a confirmed <c>Applied</c> effect is
///         <see cref="CheatEngineHostEffect.Completed" /> (the primitive ran to completion), and <c>Unknown</c> stays
///         <see cref="CheatEngineHostEffect.Unknown" />.
///     </para>
///     <para>
///         A value this Client version does not know is <see cref="CheatEngineHostEffect.Unknown" />: an unrecognized
///         effect never reads as an established one. The mapping-totality tests fail when the consumed SDK adds a value.
///     </para>
/// </remarks>
internal static class HostEffectMapping
{
	/// <summary>Returns the Client host effect for an SDK effect state.</summary>
	/// <param name="state">The effect state reported by CheatEngine.SDK.</param>
	/// <returns>The Client host effect; <see cref="CheatEngineHostEffect.Unknown" /> for an unrecognized value.</returns>
	internal static CheatEngineHostEffect FromSdk(EngineEffectState state)
	{
		return state switch
		{
			EngineEffectState.NotStarted => CheatEngineHostEffect.NotStarted,
			EngineEffectState.NotApplied => CheatEngineHostEffect.NotApplied,
			EngineEffectState.Applied => CheatEngineHostEffect.Completed,
			EngineEffectState.Unknown => CheatEngineHostEffect.Unknown,
			_ => CheatEngineHostEffect.Unknown
		};
	}
}
