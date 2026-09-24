using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>Where an <see cref="InstructionOperationStatus" /> was reported within one Client call.</summary>
internal enum InstructionCallPhase
{
	/// <summary>By the profile observation, before any instruction global of Cheat Engine was called.</summary>
	ProfileObservation,

	/// <summary>By the first assembler, disassembler or navigator call of the Client call.</summary>
	Operation,

	/// <summary>
	///     By a later step of the Client call (the assembly retry, the byte read or the disassembly of
	///     <c>Disassemble</c>), after an earlier instruction call of Cheat Engine returned.
	/// </summary>
	AfterEarlierCall
}

/// <summary>
///     Maps every CheatEngine.SDK 2.0.0 <see cref="InstructionOperationStatus" /> to the Client result vocabulary, without
///     reading Cheat Engine's text.
/// </summary>
/// <remarks>
///     <list type="table">
///         <listheader>
///             <term>SDK status</term>
///             <description>Client failure kind</description>
///         </listheader>
///         <item><term><c>Success</c></term><description>success, no failure</description></item>
///         <item><term><c>InvalidProfile</c></term><description><c>InvalidHostResult</c></description></item>
///         <item><term><c>AddressExceedsProfileWidth</c></term><description><c>OperationRejected</c></description></item>
///         <item><term><c>TargetNotSelected</c></term><description><c>TargetNotAttached</c></description></item>
///         <item><term><c>TargetChanged</c></term><description><c>TargetChanged</c></description></item>
///         <item>
///             <term><c>DestinationTooSmall</c> (after the one retry) and <c>OutputTooLong</c></term>
///             <description><c>ResultLimitExceeded</c></description>
///         </item>
///         <item><term><c>InstructionRejected</c></term><description><c>OperationRejected</c></description></item>
///         <item><term><c>GlobalUnavailable</c></term><description><c>CapabilityUnavailable</c></description></item>
///         <item><term><c>LuaFailure</c></term><description><c>LuaError</c></description></item>
///         <item><term><c>InvalidResult</c></term><description><c>InvalidHostResult</c></description></item>
///         <item><term><c>UnsupportedTargetBackend</c></term><description><c>Unsupported</c></description></item>
///         <item>
///             <term><c>Unknown</c> and any value this Client version does not know</term>
///             <description><c>Unknown</c>, never a success</description>
///         </item>
///     </list>
///     <para>
///         The host effect follows the phase. A status of the profile observation is
///         <see cref="CheatEngineHostEffect.NotStarted" />: no instruction global was called. A status of the first
///         operation is <see cref="CheatEngineHostEffect.NotStarted" /> for <c>GlobalUnavailable</c> and
///         <c>AddressExceedsProfileWidth</c> (both refused before the call), <see cref="CheatEngineHostEffect.NotApplied" />
///         for <c>InstructionRejected</c> (Cheat Engine's documented negative result),
///         <see cref="CheatEngineHostEffect.Completed" /> for <c>DestinationTooSmall</c> and <c>OutputTooLong</c> (Cheat
///         Engine returned; the Client bound refused the copy), and <see cref="CheatEngineHostEffect.Unknown" /> otherwise.
///         A status of a later step follows the same rule, except that a refusal before its own call is
///         <see cref="CheatEngineHostEffect.Completed" />: an earlier instruction call of the same Client call already
///         returned, so the Client call did start Cheat Engine work. <see cref="AfterEarlierCall" /> applies that rule
///         to a failed byte read. No instruction operation writes target memory. The mapping-totality tests fail when
///         the consumed SDK adds a status.
///     </para>
/// </remarks>
internal static class InstructionMapping
{
	/// <summary>Maps a status to its failure; <see langword="null" /> for <c>Success</c>.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="status">The SDK status.</param>
	/// <param name="phase">Where the status was reported.</param>
	/// <returns>The classified failure, or <see langword="null" /> when the SDK reported success.</returns>
	internal static CheatEngineFailure? ToFailure(string operation, InstructionOperationStatus status,
		InstructionCallPhase phase)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		return status == InstructionOperationStatus.Success
			? null
			: new CheatEngineFailure(ToFailureKind(status), operation, Describe(status), null,
				ToHostEffect(status, phase));
	}

	/// <summary>Returns the Client failure kind of a status other than <c>Success</c>.</summary>
	/// <param name="status">The SDK status.</param>
	/// <returns>The failure kind; <see cref="CheatEngineFailureKind.Unknown" /> for an unrecognized value.</returns>
	internal static CheatEngineFailureKind ToFailureKind(InstructionOperationStatus status)
	{
		return status switch
		{
			InstructionOperationStatus.InvalidProfile => CheatEngineFailureKind.InvalidHostResult,
			InstructionOperationStatus.AddressExceedsProfileWidth => CheatEngineFailureKind.OperationRejected,
			InstructionOperationStatus.TargetNotSelected => CheatEngineFailureKind.TargetNotAttached,
			InstructionOperationStatus.TargetChanged => CheatEngineFailureKind.TargetChanged,
			InstructionOperationStatus.DestinationTooSmall => CheatEngineFailureKind.ResultLimitExceeded,
			InstructionOperationStatus.OutputTooLong => CheatEngineFailureKind.ResultLimitExceeded,
			InstructionOperationStatus.InstructionRejected => CheatEngineFailureKind.OperationRejected,
			InstructionOperationStatus.GlobalUnavailable => CheatEngineFailureKind.CapabilityUnavailable,
			InstructionOperationStatus.LuaFailure => CheatEngineFailureKind.LuaError,
			InstructionOperationStatus.InvalidResult => CheatEngineFailureKind.InvalidHostResult,
			InstructionOperationStatus.UnsupportedTargetBackend => CheatEngineFailureKind.Unsupported,
			_ => CheatEngineFailureKind.Unknown
		};
	}

	/// <summary>Returns what a status establishes about Cheat Engine's work.</summary>
	/// <param name="status">The SDK status.</param>
	/// <param name="phase">Where the status was reported.</param>
	/// <returns>The host effect; <see cref="CheatEngineHostEffect.Unknown" /> unless the status proves more.</returns>
	internal static CheatEngineHostEffect ToHostEffect(InstructionOperationStatus status, InstructionCallPhase phase)
	{
		if (phase == InstructionCallPhase.ProfileObservation)
		{
			return CheatEngineHostEffect.NotStarted;
		}

		CheatEngineHostEffect effect = status switch
		{
			InstructionOperationStatus.GlobalUnavailable => CheatEngineHostEffect.NotStarted,
			InstructionOperationStatus.AddressExceedsProfileWidth => CheatEngineHostEffect.NotStarted,
			InstructionOperationStatus.InstructionRejected => CheatEngineHostEffect.NotApplied,
			InstructionOperationStatus.DestinationTooSmall => CheatEngineHostEffect.Completed,
			InstructionOperationStatus.OutputTooLong => CheatEngineHostEffect.Completed,
			_ => CheatEngineHostEffect.Unknown
		};
		return phase == InstructionCallPhase.AfterEarlierCall && effect == CheatEngineHostEffect.NotStarted
			? CheatEngineHostEffect.Completed
			: effect;
	}

	/// <summary>
	///     Re-states the failure of a later step of a Client call, after an earlier instruction call of Cheat Engine
	///     returned: a step refused before its own call is <see cref="CheatEngineHostEffect.Completed" />, never
	///     <see cref="CheatEngineHostEffect.NotStarted" />.
	/// </summary>
	/// <param name="failure">The failure of the later step, for example a failed byte read.</param>
	/// <returns>
	///     The same failure, with <see cref="CheatEngineHostEffect.Completed" /> in place of a not-started effect.
	/// </returns>
	internal static CheatEngineFailure AfterEarlierCall(CheatEngineFailure failure)
	{
		return failure.HostEffect == CheatEngineHostEffect.NotStarted
			? new CheatEngineFailure(failure.Kind, failure.Operation, failure.Message, failure.Exception,
				CheatEngineHostEffect.Completed)
			: failure;
	}

	/// <summary>Creates the refusal of an address wider than the observed profile, before Cheat Engine is called.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <returns><see cref="CheatEngineFailureKind.OperationRejected" /> with <see cref="CheatEngineHostEffect.NotStarted" />.</returns>
	internal static CheatEngineFailure AddressOutsideProfile(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			"The address does not fit the 32-bit instruction profile of the selected target; Cheat Engine was not called.",
			null, CheatEngineHostEffect.NotStarted);
	}

	/// <summary>Creates the failure of a previous-instruction estimate wider than the observed profile.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <returns><see cref="CheatEngineFailureKind.OperationRejected" /> with <see cref="CheatEngineHostEffect.Completed" />.</returns>
	internal static CheatEngineFailure ReturnedAddressOutsideProfile(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, operation,
			"Cheat Engine returned an address that does not fit the instruction profile of the selected target; it was " +
			"not published.", null, CheatEngineHostEffect.Completed);
	}

	/// <summary>Creates the failure of a result larger than the Client bound.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="requiredLength">The number of bytes Cheat Engine produced.</param>
	/// <param name="limit">The Client bound in bytes.</param>
	/// <returns><see cref="CheatEngineFailureKind.ResultLimitExceeded" /> with <see cref="CheatEngineHostEffect.Completed" />.</returns>
	internal static CheatEngineFailure ResultExceedsLimit(string operation, int requiredLength, int limit)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.ResultLimitExceeded, operation,
			$"The instruction takes {requiredLength} bytes, more than the configured limit of {limit} bytes; nothing was " +
			"copied.", null, CheatEngineHostEffect.Completed);
	}

	/// <summary>Creates the failure of a result CheatEngine.SDK reported as successful but outside its shape.</summary>
	/// <param name="operation">The public Client operation name.</param>
	/// <param name="message">The stable description of the violated shape.</param>
	/// <returns><see cref="CheatEngineFailureKind.InvalidHostResult" /> with <see cref="CheatEngineHostEffect.Completed" />.</returns>
	internal static CheatEngineFailure InvalidResult(string operation, string message)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, operation, message, null,
			CheatEngineHostEffect.Completed);
	}

	/// <summary>Describes a status without naming an address, an instruction or a byte.</summary>
	/// <param name="status">The SDK status.</param>
	/// <returns>A stable message.</returns>
	internal static string Describe(InstructionOperationStatus status)
	{
		return status switch
		{
			InstructionOperationStatus.InvalidProfile =>
				"Cheat Engine reported contradictory or unsupported instruction-set facts for the selected target.",
			InstructionOperationStatus.AddressExceedsProfileWidth =>
				"The address does not fit the instruction profile of the selected target.",
			InstructionOperationStatus.TargetNotSelected => "Cheat Engine has no selected target process.",
			InstructionOperationStatus.TargetChanged =>
				"Cheat Engine's selected target changed during the instruction operation; nothing was published.",
			InstructionOperationStatus.DestinationTooSmall =>
				"The assembled instruction does not fit the Client's bounded buffer; nothing was copied.",
			InstructionOperationStatus.OutputTooLong =>
				"The disassembled instruction text is longer than the configured limit; nothing was copied.",
			InstructionOperationStatus.InstructionRejected => "Cheat Engine rejected the instruction.",
			InstructionOperationStatus.GlobalUnavailable =>
				"A Cheat Engine instruction function is unavailable, so it was not called.",
			InstructionOperationStatus.LuaFailure => "A protected Cheat Engine instruction call raised a Lua error.",
			InstructionOperationStatus.InvalidResult =>
				"Cheat Engine returned an instruction result outside its documented shape.",
			InstructionOperationStatus.UnsupportedTargetBackend =>
				"The selected target is a file opened as a process, which has no instruction profile.",
			_ => "CheatEngine.SDK reported no recognized instruction outcome."
		};
	}
}
