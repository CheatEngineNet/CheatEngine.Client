using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal, activation-thread-owned view of an SDK AOB result list.</summary>
internal interface IAobMatchList
{
	public bool TryGetCount(out int count);

	public bool TryGetItem(int index, [NotNullWhen(true)] out string? value);

	/// <summary>Releases the Cheat Engine list once and reports what happened (<c>Owned&lt;T&gt;.ReleaseWithOutcome</c>).</summary>
	/// <returns>
	///     The SDK release status. Only <see cref="TargetReleaseStatus.Released" /> confirms the release; the list is
	///     consumed on every path and is never released again.
	/// </returns>
	/// <remarks>The SDK release never throws; an implementation must not either.</remarks>
	public TargetReleaseStatus Release();
}
