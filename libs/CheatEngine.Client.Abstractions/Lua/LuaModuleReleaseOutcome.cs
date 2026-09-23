using System.Collections.Immutable;
using System.Text;

namespace CheatEngine.Client.Lua;

/// <summary>Copied, handle-free result of one release of a Lua module registration.</summary>
/// <remarks>
///     <para>
///         The outcome is immutable and published as a whole, so a reader on another thread never observes a partially
///         built result. <see cref="Kind" /> is computed from the export statuses: any <see cref="LuaExportReleaseStatus.Failed" />
///         export makes the release <see cref="LuaModuleReleaseKind.PartiallyReleased" />; a release in which every export is
///         <see cref="LuaExportReleaseStatus.NotAttempted" /> is <see cref="LuaModuleReleaseKind.Stale" />; a release whose
///         exports are all <see cref="LuaExportReleaseStatus.Removed" />, <see cref="LuaExportReleaseStatus.Replaced" />, or
///         <see cref="LuaExportReleaseStatus.Absent" /> is <see cref="LuaModuleReleaseKind.Released" />.
///     </para>
///     <para>
///         The counts map to the CheatEngine.SDK 2.0 registration lease outcome: the values 1 to 3 of <see cref="Kind" />
///         map one to one onto <c>LuaRegistrationReleaseKind</c> (whose <c>NotAttempted</c> and <c>AlreadyReleased</c>
///         values have no Client counterpart), and the SDK <c>ReplacementCount</c> corresponds to
///         <see cref="ReplacedCount" /> plus <see cref="AbsentCount" />.
///     </para>
/// </remarks>
public sealed class LuaModuleReleaseOutcome
{
	/// <summary>Initializes one validated module release result.</summary>
	/// <param name="moduleName">The stable module identity.</param>
	/// <param name="exports">One result per exported Lua global, in the module's export order.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="moduleName" /> is blank; <paramref name="exports" /> is default or empty, contains an uninitialized
	///     result or a duplicate name, or mixes <see cref="LuaExportReleaseStatus.NotAttempted" /> with other statuses.
	/// </exception>
	public LuaModuleReleaseOutcome(string moduleName, ImmutableArray<LuaExportReleaseOutcome> exports)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
		if (exports.IsDefaultOrEmpty)
		{
			throw new ArgumentException(
				$"The release outcome of Lua module '{moduleName}' must report at least one export.", nameof(exports));
		}

		HashSet<string> names = new(StringComparer.Ordinal);
		int notAttempted = 0;
		foreach (LuaExportReleaseOutcome export in exports)
		{
			if (export.Name is null)
			{
				throw new ArgumentException(
					$"The release outcome of Lua module '{moduleName}' contains an uninitialized export result.",
					nameof(exports));
			}

			if (!names.Add(export.Name))
			{
				throw new ArgumentException(
					$"The release outcome of Lua module '{moduleName}' reports the export '{export.Name}' more than once.",
					nameof(exports));
			}

			switch (export.Status)
			{
				case LuaExportReleaseStatus.Removed:
					RemovedCount++;
					break;
				case LuaExportReleaseStatus.Replaced:
					ReplacedCount++;
					break;
				case LuaExportReleaseStatus.Absent:
					AbsentCount++;
					break;
				case LuaExportReleaseStatus.Failed:
					FailedCount++;
					break;
				case LuaExportReleaseStatus.NotAttempted:
					notAttempted++;
					break;
				default:
					throw new ArgumentException(
						$"The release outcome of Lua module '{moduleName}' reports the undefined status '{export.Status}' for export '{export.Name}'.",
						nameof(exports));
			}
		}

		if (notAttempted != 0 && notAttempted != exports.Length)
		{
			throw new ArgumentException(
				$"The release outcome of Lua module '{moduleName}' mixes NotAttempted exports with attempted ones; a stale release attempts none.",
				nameof(exports));
		}

		ModuleName = moduleName;
		Exports = exports;
		Kind = notAttempted != 0
			? LuaModuleReleaseKind.Stale
			: FailedCount != 0
				? LuaModuleReleaseKind.PartiallyReleased
				: LuaModuleReleaseKind.Released;
	}

	/// <summary>Gets the stable module identity.</summary>
	public string ModuleName
	{
		get;
	}

	/// <summary>Gets the classification computed from <see cref="Exports" />.</summary>
	public LuaModuleReleaseKind Kind
	{
		get;
	}

	/// <summary>Gets one result per exported Lua global, in the module's export order.</summary>
	public ImmutableArray<LuaExportReleaseOutcome> Exports
	{
		get;
	}

	/// <summary>Gets the number of exports that were still the module's and were set to <c>nil</c>.</summary>
	public int RemovedCount
	{
		get;
	}

	/// <summary>Gets the number of exports a third party had replaced; they were left untouched.</summary>
	public int ReplacedCount
	{
		get;
	}

	/// <summary>Gets the number of exports that were already <c>nil</c>.</summary>
	public int AbsentCount
	{
		get;
	}

	/// <summary>Gets the number of exports whose read, comparison, or clear failed.</summary>
	public int FailedCount
	{
		get;
	}

	/// <summary>
	///     Gets whether no release step failed: <see cref="Kind" /> is <see cref="LuaModuleReleaseKind.Released" /> or
	///     <see cref="LuaModuleReleaseKind.Stale" />, the same meaning as the CheatEngine.SDK 2.0 <c>IsComplete</c>.
	/// </summary>
	/// <remarks>
	///     A <see cref="LuaModuleReleaseKind.Stale" /> release attempted no Lua operation: the registration's Lua state or
	///     attachment is gone, so nothing of it can be examined or cleared from the current state. It does not claim that
	///     the earlier state was cleaned up.
	/// </remarks>
	public bool IsComplete => Kind is LuaModuleReleaseKind.Released or LuaModuleReleaseKind.Stale;

	/// <summary>Formats the outcome as <c>Module=&lt;name&gt;; Kind=&lt;kind&gt;; &lt;export&gt;=&lt;status&gt;; ...</c>.</summary>
	/// <returns>A culture-invariant, single-line description.</returns>
	public override string ToString()
	{
		StringBuilder text = new();
		text.Append("Module=").Append(ModuleName).Append("; Kind=").Append(Kind.ToString());
		foreach (LuaExportReleaseOutcome export in Exports)
		{
			text.Append("; ").Append(export.Name).Append('=').Append(export.Status.ToString());
		}

		return text.ToString();
	}
}
