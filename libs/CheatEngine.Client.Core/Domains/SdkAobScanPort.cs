using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production adapter that copies and releases the SDK-owned AOB list on the CE dispatch thread.</summary>
/// <remarks>
///     <para>
///         The global route calls <c>AobScanner.TryScanOutcome</c> with its target context and copies both into an
///         <see cref="AobHostOutcome" />; classification happens in <see cref="PatternScanner" />, never here.
///     </para>
///     <para>
///         The SDK owner is handed to <see cref="SdkAobMatchList" /> through <see cref="OwnershipHandoff" />, so a failure
///         between acquisition and publication releases the Cheat Engine list exactly once (audit F13). After publication
///         the match list is the single release authority, and it releases through the never-throwing
///         <c>Owned&lt;StringList&gt;.ReleaseWithOutcome</c>.
///     </para>
/// </remarks>
internal sealed class SdkAobScanPort : IAobScanPort
{
	public AobHostOutcome TryScan(string pattern, AobScanOptions options, out IAobMatchList? matches)
	{
		matches = null;
		AobScanOutcome outcome = AobScanner.TryScanOutcome(pattern, options, out Owned<StringList>? owner,
			out AobScanTargetContext context);
		AobHostOutcome host = new(outcome.Kind, outcome.LuaStatus, outcome.ResultCount,
			SdkRuntimeObservationPort.Copy(context.Before), SdkRuntimeObservationPort.Copy(context.After));

		// The SDK hands out an owner only with a successful outcome. The guard keeps a contract break (a success
		// without an owner) from reaching OwnershipHandoff.Adopt, whose ArgumentNullException would otherwise escape a
		// Try method; PatternScanner classifies that outcome as an invalid host result. An owner handed out with any
		// other outcome is still adopted, so PatternScanner releases it once.
		if (owner is null)
		{
			return host;
		}

		matches = OwnershipHandoff.Adopt(owner, static acquired => new SdkAobMatchList(acquired),
			static acquired => SdkReleaseOutcomes.FromTarget(acquired.ReleaseWithOutcome().Status));
		return host;
	}

	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateModules(destination, out written);
	}

	private sealed class SdkAobMatchList(Owned<StringList> owner) : IAobMatchList
	{
		private readonly Owned<StringList> _owner = owner ?? throw new ArgumentNullException(nameof(owner));

		public bool TryGetCount(out int count)
		{
			return _owner.Value.TryGetCount(out count);
		}

		public bool TryGetItem(int index, [NotNullWhen(true)] out string? value)
		{
			return _owner.Value.TryGetItem(index, out value);
		}

		/// <summary>Releases the Cheat Engine list through the SDK owner, once, without throwing.</summary>
		/// <remarks>
		///     CheatEngine.SDK 2.0.0 <c>Owned&lt;T&gt;.ReleaseWithOutcome</c> always consumes the owner and never retries
		///     <c>destroy()</c>: <c>Released</c> after a confirmed destroy, <c>UnconfirmedAfterInvocation</c> when it
		///     raised, <c>RefusedRuntimeChanged</c> when the owner belongs to a previous Lua runtime, and <c>NotInvoked</c>
		///     when no Lua operation could be admitted. <see cref="PatternScanner" /> treats every status but
		///     <c>Released</c> as an unconfirmed release.
		/// </remarks>
		public TargetReleaseStatus Release()
		{
			return _owner.ReleaseWithOutcome().Status;
		}
	}
}
