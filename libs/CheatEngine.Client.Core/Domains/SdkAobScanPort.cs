using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production adapter that copies and releases the SDK-owned AOB list on the CE dispatch thread.</summary>
/// <remarks>
///     The SDK owner is handed to <see cref="SdkAobMatchList" /> through <see cref="OwnershipHandoff" />, so a failure
///     between acquisition and publication releases the Cheat Engine list exactly once (audit F13). After publication
///     the match list is the single release authority.
/// </remarks>
internal sealed class SdkAobScanPort : IAobScanPort
{
	public AobScanHostStatus TryScan(string pattern, AobScanOptions options,
		[NotNullWhen(true)] out IAobMatchList? matches)
	{
		matches = null;
		if (!AobScanner.TryScan(pattern, options, out Owned<StringList>? owner))
		{
			return AobScanHostStatus.NoResultList;
		}

		matches = OwnershipHandoff.Adopt(owner, static acquired => new SdkAobMatchList(acquired));
		return AobScanHostStatus.Success;
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

		/// <summary>Releases the Cheat Engine list through the SDK owner.</summary>
		/// <remarks>
		///     CheatEngine.SDK 2.0.0 <c>Owned&lt;T&gt;.Dispose</c> throws <see cref="InvalidOperationException" /> and
		///     retains ownership when no Lua operation can be admitted (for example a detached runtime); it does not throw
		///     when <c>destroy()</c> raises or when the owner belongs to a previous runtime. That exception is the only
		///     "release not confirmed" signal this port observes, so it is propagated to <see cref="PatternScanner" />
		///     unchanged.
		/// </remarks>
		public void Dispose()
		{
			_owner.Dispose();
		}
	}
}
