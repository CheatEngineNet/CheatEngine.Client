using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>States what a cancellation observed by Core means for the Cheat Engine side effect.</summary>
/// <remarks>
///     <para>
///         A <see cref="CancellationToken" /> never interrupts a Cheat Engine call. Core observes it at two kinds of
///         point only, and the point alone decides the reported effect:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 before the native call: <see cref="CheatEngineFailureKind.Cancelled" /> with
///                 <see cref="CheatEngineHostEffect.NotStarted" />, because nothing was invoked;
///             </description>
///         </item>
///         <item>
///             <description>
///                 after the native call returned: <see cref="CheatEngineFailureKind.Cancelled" /> with
///                 <see cref="CheatEngineHostEffect.Completed" />, because Cheat Engine finished its work. The caller
///                 discards what it copied and publishes nothing: no partial result, no prefix, no owner.
///             </description>
///         </item>
///     </list>
/// </remarks>
internal static class CancellationMapping
{
	/// <summary>The default message of a cancellation observed before the native call.</summary>
	internal const string BeforeNativeCallMessage = "The operation was cancelled before Cheat Engine work began.";

	/// <summary>The default message of a cancellation observed after the native call returned.</summary>
	internal const string AfterNativeCallMessage =
		"The operation was cancelled after Cheat Engine completed its work; no result was published.";

	/// <summary>Creates the failure of a cancellation observed before any Cheat Engine call of the operation.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="message">The diagnostic message.</param>
	/// <returns>
	///     A <see cref="CheatEngineFailureKind.Cancelled" /> failure whose effect is
	///     <see cref="CheatEngineHostEffect.NotStarted" />.
	/// </returns>
	internal static CheatEngineFailure BeforeNativeCall(string operation, string message = BeforeNativeCallMessage)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.Cancelled, operation, message, null,
			CheatEngineHostEffect.NotStarted);
	}

	/// <summary>Creates the failure of a cancellation observed after the operation's Cheat Engine call returned.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="message">The diagnostic message.</param>
	/// <returns>
	///     A <see cref="CheatEngineFailureKind.Cancelled" /> failure whose effect is
	///     <see cref="CheatEngineHostEffect.Completed" />.
	/// </returns>
	/// <remarks>The caller publishes nothing it copied from the completed call.</remarks>
	internal static CheatEngineFailure AfterNativeCall(string operation, string message = AfterNativeCallMessage)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.Cancelled, operation, message, null,
			CheatEngineHostEffect.Completed);
	}
}
