using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production selection port: CheatEngine.SDK 2.0.0 <c>RuntimeProcessOperations.SelectAndObserve</c>.</summary>
/// <remarks>
///     This type is the only Client code that references <c>SelectAndObserve</c>, and <c>ProcessClient.TryAttach</c> is
///     its only caller; the architecture ratchet (<c>RuntimeProbeCallsOnlyReadOnlySdkOperations</c>) keeps both. No
///     hosted test has a Cheat Engine process or Lua state, so its lines are excluded from the coverage metric;
///     <c>SdkProcessSelectionPortTests</c> proves that the call requires an enabled plugin.
/// </remarks>
internal sealed class SdkProcessSelectionPort : IProcessSelectionPort
{
	private SdkProcessSelectionPort()
	{
	}

	/// <summary>Gets the stateless production port.</summary>
	internal static SdkProcessSelectionPort Instance
	{
		get;
	} = new();

	public ProcessOperationStatus SelectAndObserve(TargetProcessId processId,
		out CurrentProcessObservation observation)
	{
		return RuntimeProcessOperations.SelectAndObserve(processId, out observation);
	}
}
