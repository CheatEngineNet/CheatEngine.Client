namespace CheatEngine.Client.Results;

/// <summary>Describes what is known about the Cheat Engine side effect of an operation that failed.</summary>
/// <remarks>
///     <para>
///         A <see cref="CheatEngineFailure" /> classifies <em>why</em> an operation failed through
///         <see cref="CheatEngineFailure.Kind" />. This value independently states how far the requested Cheat Engine
///         primitive got, so a caller can decide whether a retry, a compensation, or an investigation is required. The
///         two facts are deliberately separate: for example a <see cref="CheatEngineFailureKind.Cancelled" /> failure can
///         be <see cref="NotStarted" /> (the token was observed before dispatch) or <see cref="Completed" /> (the token was
///         observed while Core copied a result that Cheat Engine had already produced).
///     </para>
///     <para>
///         A <see cref="System.Threading.CancellationToken" /> never interrupts a Cheat Engine call that has started and
///         never removes an effect that a started call produced.
///     </para>
/// </remarks>
public enum CheatEngineHostEffect
{
	/// <summary>
	///     The Client cannot state whether the requested Cheat Engine primitive ran. Treat any effect as possible.
	/// </summary>
	Unknown = 0,

	/// <summary>
	///     The requested Cheat Engine primitive was not invoked: the failure was observed during request validation,
	///     activation admission, pre-dispatch cancellation, policy evaluation, or a prerequisite lookup.
	/// </summary>
	NotStarted = 1,

	/// <summary>
	///     The requested Cheat Engine primitive was invoked, but neither its completion nor a rollback was established.
	///     Its effects may persist in Cheat Engine or in the target.
	/// </summary>
	Started = 2,

	/// <summary>
	///     The requested Cheat Engine primitive returned, and the failure happened afterwards inside the Client (result
	///     validation, copying, parsing, filtering, or cancellation observed between Client-managed steps). No Cheat
	///     Engine resource created for the call remains.
	/// </summary>
	Completed = 3,

	/// <summary>
	///     Cheat Engine work left a resource or change whose release or rollback could not be confirmed. A resource may
	///     remain allocated, or a partial change may remain visible, until Cheat Engine or the target releases it.
	/// </summary>
	CleanupUnconfirmed = 4,

	/// <summary>
	///     The requested Cheat Engine primitive ran and returned its documented negative result, which establishes that
	///     the effect did not happen (for example an allocation that returned <c>nil</c>, or a change the host refused).
	///     Nothing was applied, so nothing has to be released or rolled back.
	/// </summary>
	/// <remarks>
	///     This is distinct from <see cref="NotStarted" /> (the primitive was never invoked) and from
	///     <see cref="Unknown" /> (a refusal that does not prove the absence of a change).
	/// </remarks>
	NotApplied = 5
}
