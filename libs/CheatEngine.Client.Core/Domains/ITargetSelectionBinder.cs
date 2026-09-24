using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Internal boundary that keys a new target-bound owner to the selection epoch of the process incarnation that
///     CheatEngine.SDK bound it to. <see cref="ProcessClient" /> implements it, since it owns the observed selection.
/// </summary>
/// <remarks>
///     The target-selection epoch advances only when the Client observes Cheat Engine's selection. A process selected in
///     Cheat Engine's own window since the last observation is not reflected in the epoch yet: an owner registered under
///     the current epoch would then be keyed to the earlier process, and the next observation would release it while its
///     own target is still selected. The incarnation of the new owner is therefore recorded first, as an observation of
///     the selection, and the owner is registered under the epoch that this observation leaves.
/// </remarks>
internal interface ITargetSelectionBinder
{
	/// <summary>
	///     Records the process incarnation of a new owner as an observation of Cheat Engine's selection. The epoch advances,
	///     and the earlier selection's leases are released, when the incarnation names another process, another
	///     incarnation of the same identifier, or another backend than the Client last observed.
	/// </summary>
	/// <param name="incarnation">The incarnation that CheatEngine.SDK bound the new owner to.</param>
	/// <param name="operation">The public Client operation that created the owner.</param>
	/// <returns>The epoch to register the owner under, and the reason of the advance to report, if any.</returns>
	/// <remarks>Runs on Cheat Engine's main thread, inside the dispatched callback that created the owner.</remarks>
	public TargetSelectionBinding BindOwner(TargetProcessIncarnation incarnation, string operation);

	/// <summary>Reports the selection advance of a binding (EventId 1100), after the dispatched callback returned.</summary>
	/// <param name="binding">The binding.</param>
	/// <param name="operation">The public Client operation that created the owner.</param>
	public void ReportBinding(TargetSelectionBinding binding, string operation);
}

/// <summary>The selection epoch a new owner belongs to, and the reason of the advance its binding made.</summary>
/// <param name="SelectionEpoch">The target-selection epoch to register the owner under.</param>
/// <param name="AdvanceReason">The closed reason name of the advance, or <see langword="null" /> without one.</param>
internal readonly record struct TargetSelectionBinding(long SelectionEpoch, string? AdvanceReason);
