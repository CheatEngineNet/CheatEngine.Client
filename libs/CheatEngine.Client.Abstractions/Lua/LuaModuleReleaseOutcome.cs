using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>Copied, handle-free result of one release of a Lua module registration.</summary>
/// <remarks>
///     <para>
///         A module generated from <see cref="CheatEngineLuaModuleAttribute" /> releases its globals through its
///         CheatEngine.SDK 2.0.0 registration lease and copies what the SDK observed: the kind of the release, in the Client
///         lease vocabulary, and its counts and failed global names. The SDK writes a global only while it still holds the
///         value the registration installed (primitive identity, no <c>__eq</c> metamethod), so a global that a third party
///         replaced, or that is already <c>nil</c>, is counted in <see cref="ReplacementCount" /> and left untouched.
///     </para>
///     <para>
///         The kinds a generated module reports are <see cref="LeaseReleaseKind.Released" />,
///         <see cref="LeaseReleaseKind.PartiallyReleased" /> (at least one protected read or write failed; the failed
///         globals are in <see cref="FailedExports" /> and are not retried),
///         <see cref="LeaseReleaseKind.RefusedRuntimeChanged" /> (the registration belongs to an earlier Lua attachment or
///         state, or CheatEngine.SDK refused the Lua admission with <c>Detached</c> or <c>ExternalStateReset</c>: the
///         registration is consumed, nothing was written, and its globals may remain as functions that raise an error),
///         <see cref="LeaseReleaseKind.AlreadyReleased" /> (the module owned no registration),
///         <see cref="LeaseReleaseKind.CleanupUnavailable" /> (CheatEngine.SDK refused the Lua admission for any other
///         reason; the module keeps its registration for a later attempt) and
///         <see cref="LeaseReleaseKind.CleanupUnconfirmed" /> (CheatEngine.SDK consumed the registration but reported a
///         release outside its documented shape; it is never retried). A manual <see cref="ILuaModule" /> reports its own
///         release with the factories of this type.
///     </para>
///     <para>
///         The outcome is immutable and holds copied names and counts only: never a Lua state, reference, or native handle.
///     </para>
/// </remarks>
public sealed class LuaModuleReleaseOutcome
{
	private LuaModuleReleaseOutcome(string moduleName, LeaseReleaseKind kind, int removedCount, int restoredCount,
		int replacementCount, int remainingCount, ImmutableArray<string> failedExports)
	{
		ModuleName = moduleName;
		Kind = kind;
		RemovedCount = removedCount;
		RestoredCount = restoredCount;
		ReplacementCount = replacementCount;
		RemainingCount = remainingCount;
		FailedExports = failedExports;
	}

	/// <summary>Gets the stable module identity (<see cref="LuaModuleDescriptor.Name" />).</summary>
	public string ModuleName
	{
		get;
	}

	/// <summary>Gets what the release did, in the Client lease vocabulary.</summary>
	public LeaseReleaseKind Kind
	{
		get;
	}

	/// <summary>Gets the number of globals that still held the module's value and were set to <c>nil</c>.</summary>
	public int RemovedCount
	{
		get;
	}

	/// <summary>
	///     Gets the number of globals whose earlier value was put back. A generated module refuses to replace a defined
	///     global, so it always reports <c>0</c>.
	/// </summary>
	public int RestoredCount
	{
		get;
	}

	/// <summary>
	///     Gets the number of globals that no longer held the module's value (a third party replaced them, even with a
	///     wrapper of the module's function, or they were already <c>nil</c>); nothing was written to them.
	/// </summary>
	public int ReplacementCount
	{
		get;
	}

	/// <summary>
	///     Gets the number of globals whose cleanup this attempt did not confirm: the failed ones, or every global of a
	///     registration that was not released.
	/// </summary>
	public int RemainingCount
	{
		get;
	}

	/// <summary>Gets the globals whose protected read or write failed during the release, in registration order.</summary>
	public ImmutableArray<string> FailedExports
	{
		get;
	}

	/// <summary>
	///     Gets whether the release ended and nothing the module owned is known to remain: the rule of
	///     <see cref="LeaseReleaseOutcome.IsComplete" /> applied to <see cref="Kind" />.
	/// </summary>
	public bool IsComplete => new LeaseReleaseOutcome(Kind, CheatEngineHostEffect.Unknown).IsComplete;

	/// <summary>Creates a validated release outcome from every fact of the release.</summary>
	/// <param name="moduleName">The stable module identity.</param>
	/// <param name="kind">What the release did.</param>
	/// <param name="removedCount">The globals set to <c>nil</c> because they still held the module's value.</param>
	/// <param name="restoredCount">The globals whose earlier value was put back.</param>
	/// <param name="replacementCount">The globals that no longer held the module's value and were left untouched.</param>
	/// <param name="remainingCount">The globals whose cleanup was not confirmed.</param>
	/// <param name="failedExports">
	///     The globals whose release failed, in registration order; <see langword="default" /> means none.
	/// </param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> or a failed export name is blank, a failed export is listed twice, failed exports
	///     are named for a kind other than <see cref="LeaseReleaseKind.PartiallyReleased" /> or are missing for it, or
	///     <paramref name="remainingCount" /> is smaller than the number of failed exports.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="kind" /> is not a defined value, or a count is negative.
	/// </exception>
	public static LuaModuleReleaseOutcome Create(string moduleName, LeaseReleaseKind kind, int removedCount,
		int restoredCount, int replacementCount, int remainingCount, ImmutableArray<string> failedExports)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind), kind, "The lease release kind must be a defined value.");
		}

		ArgumentOutOfRangeException.ThrowIfNegative(removedCount);
		ArgumentOutOfRangeException.ThrowIfNegative(restoredCount);
		ArgumentOutOfRangeException.ThrowIfNegative(replacementCount);
		ArgumentOutOfRangeException.ThrowIfNegative(remainingCount);
		ImmutableArray<string> failed = failedExports.IsDefault ? [] : failedExports;
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (string export in failed)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(export, nameof(failedExports));
			if (!names.Add(export))
			{
				throw new ArgumentException(
					$"The release outcome of Lua module '{moduleName}' names the failed export '{export}' more than once.",
					nameof(failedExports));
			}
		}

		bool partiallyReleased = kind == LeaseReleaseKind.PartiallyReleased;
		if (partiallyReleased == failed.IsEmpty)
		{
			throw new ArgumentException(
				$"The release outcome of Lua module '{moduleName}' must name its failed exports exactly when it is " +
				$"{nameof(LeaseReleaseKind.PartiallyReleased)}.", nameof(failedExports));
		}

		if (remainingCount < failed.Length)
		{
			throw new ArgumentException(
				$"The release outcome of Lua module '{moduleName}' cannot report fewer remaining globals than failed exports.",
				nameof(remainingCount));
		}

		return new LuaModuleReleaseOutcome(moduleName, kind, removedCount, restoredCount, replacementCount,
			remainingCount, failed);
	}

	/// <summary>Creates the outcome of a release in which no protected read or write failed.</summary>
	/// <param name="moduleName">The stable module identity.</param>
	/// <param name="removedCount">The globals set to <c>nil</c>.</param>
	/// <param name="restoredCount">The globals whose earlier value was put back.</param>
	/// <param name="replacementCount">The globals left untouched because they no longer held the module's value.</param>
	/// <returns>A <see cref="LeaseReleaseKind.Released" /> outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="moduleName" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="moduleName" /> is empty or white space.</exception>
	/// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
	public static LuaModuleReleaseOutcome Released(string moduleName, int removedCount, int restoredCount,
		int replacementCount)
	{
		return Create(moduleName, LeaseReleaseKind.Released, removedCount, restoredCount, replacementCount, 0, []);
	}

	/// <summary>Creates the outcome of a release in which at least one global could not be released.</summary>
	/// <param name="moduleName">The stable module identity.</param>
	/// <param name="removedCount">The globals set to <c>nil</c>.</param>
	/// <param name="restoredCount">The globals whose earlier value was put back.</param>
	/// <param name="replacementCount">The globals left untouched because they no longer held the module's value.</param>
	/// <param name="failedExports">The globals whose release failed; at least one.</param>
	/// <returns>
	///     A <see cref="LeaseReleaseKind.PartiallyReleased" /> outcome whose <see cref="RemainingCount" /> is the number of
	///     failed exports.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="moduleName" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> or a failed export name is empty or white space, a failed export is listed
	///     twice, or <paramref name="failedExports" /> is empty.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
	public static LuaModuleReleaseOutcome PartiallyReleased(string moduleName, int removedCount, int restoredCount,
		int replacementCount, ImmutableArray<string> failedExports)
	{
		return Create(moduleName, LeaseReleaseKind.PartiallyReleased, removedCount, restoredCount, replacementCount,
			failedExports.IsDefault ? 0 : failedExports.Length, failedExports);
	}

	/// <summary>Creates the outcome of a release that found no registration left to release.</summary>
	/// <param name="moduleName">The stable module identity.</param>
	/// <returns>An <see cref="LeaseReleaseKind.AlreadyReleased" /> outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="moduleName" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="moduleName" /> is empty or white space.</exception>
	public static LuaModuleReleaseOutcome AlreadyReleased(string moduleName)
	{
		return Create(moduleName, LeaseReleaseKind.AlreadyReleased, 0, 0, 0, 0, []);
	}

	/// <summary>
	///     Creates the outcome of a release refused because the registration belongs to an earlier Lua attachment or state.
	/// </summary>
	/// <param name="moduleName">The stable module identity.</param>
	/// <param name="remainingCount">The globals of the registration, none of which was examined.</param>
	/// <returns>A <see cref="LeaseReleaseKind.RefusedRuntimeChanged" /> outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="moduleName" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="moduleName" /> is empty or white space.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="remainingCount" /> is negative.</exception>
	public static LuaModuleReleaseOutcome RefusedRuntimeChanged(string moduleName, int remainingCount)
	{
		return Create(moduleName, LeaseReleaseKind.RefusedRuntimeChanged, 0, 0, 0, remainingCount, []);
	}

	/// <summary>Creates the outcome of a release that could not begin; the module keeps its registration.</summary>
	/// <param name="moduleName">The stable module identity.</param>
	/// <param name="remainingCount">The globals of the registration, none of which was examined.</param>
	/// <returns>A retryable <see cref="LeaseReleaseKind.CleanupUnavailable" /> outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="moduleName" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="moduleName" /> is empty or white space.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="remainingCount" /> is negative.</exception>
	public static LuaModuleReleaseOutcome CleanupUnavailable(string moduleName, int remainingCount)
	{
		return Create(moduleName, LeaseReleaseKind.CleanupUnavailable, 0, 0, 0, remainingCount, []);
	}

	/// <summary>
	///     Formats the outcome as <c>Module=&lt;name&gt;; Kind=&lt;kind&gt;; Removed=&lt;n&gt;; Restored=&lt;n&gt;;
	///     Replacement=&lt;n&gt;; Remaining=&lt;n&gt;; Failed=&lt;comma-separated names&gt;</c>.
	/// </summary>
	/// <returns>A culture-invariant, single-line description.</returns>
	public override string ToString()
	{
		StringBuilder text = new();
		text.Append("Module=").Append(ModuleName)
			.Append("; Kind=").Append(Kind.ToString())
			.Append("; Removed=").Append(RemovedCount.ToString(CultureInfo.InvariantCulture))
			.Append("; Restored=").Append(RestoredCount.ToString(CultureInfo.InvariantCulture))
			.Append("; Replacement=").Append(ReplacementCount.ToString(CultureInfo.InvariantCulture))
			.Append("; Remaining=").Append(RemainingCount.ToString(CultureInfo.InvariantCulture))
			.Append("; Failed=").Append(string.Join(",", FailedExports));
		return text.ToString();
	}
}
