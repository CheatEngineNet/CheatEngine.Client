using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains;

/// <summary>A copied CheatEngine.SDK <c>TargetSelectionObservation</c>: what identifies the selected target.</summary>
/// <remarks>
///     The SDK type has an internal constructor, so the port copies it into this value and the Processes domain is
///     testable without a host. Only a local process yields an <see cref="Incarnation" />: its PID and the creation time
///     the SDK observed, read in the same Lua operation as the backend fact.
/// </remarks>
/// <param name="Status">The factual observation category.</param>
/// <param name="Backend">The backend the SDK established for the selection.</param>
/// <param name="SelectedProcessId">The selected PID when Cheat Engine reported one.</param>
/// <param name="Incarnation">The local process incarnation, only for a qualified local selection.</param>
internal readonly record struct TargetSelectionFacts(
	TargetSelectionObservationStatus Status,
	TargetBackend Backend,
	int? SelectedProcessId,
	TargetProcessIncarnation? Incarnation);

/// <summary>A copied CheatEngine.SDK <c>TargetIdentityCheck</c>: a known incarnation compared with the selection.</summary>
/// <param name="Kind">The factual validation category.</param>
/// <param name="Observed">The selection observation the SDK compared.</param>
internal readonly record struct TargetIdentityFacts(TargetIdentityCheckKind Kind, TargetSelectionFacts Observed);
