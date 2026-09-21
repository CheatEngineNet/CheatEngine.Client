using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production adapter that copies and releases the SDK-owned AOB list on the CE dispatch thread.</summary>
internal sealed class SdkAobScanPort : IAobScanPort
{
	public AobScanHostStatus TryScan(string pattern, AobScanOptions options,
		[NotNullWhen(true)] out IAobMatchList? matches)
	{
		matches = null;
		if (!AobScanner.TryScan(pattern, options, out Owned<StringList>? owner))
		{
			return AobScanHostStatus.Rejected;
		}

		matches = new SdkAobMatchList(owner);
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

		public void Dispose()
		{
			_owner.Dispose();
		}
	}
}
