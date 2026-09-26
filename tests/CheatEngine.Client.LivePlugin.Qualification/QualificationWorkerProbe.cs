using CheatEngine.Client.Lua;
using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Qualification;

/// <summary>
///     The Q19 probe module. It is never added to the activation: <c>worker_admission("start")</c> calls its generated
///     <see cref="ILuaModule.Register" /> directly from a worker thread, where CheatEngine.SDK must refuse the Lua
///     admission, so its global must never exist in a qualification run.
/// </summary>
[CheatEngineLuaModule(typeof(QualificationWorkerProbeFunctions), "client_qualification_worker_probe")]
internal sealed partial class QualificationWorkerProbeModule : ILuaModule;

/// <summary>The one export of the Q19 probe module.</summary>
internal static partial class QualificationWorkerProbeFunctions
{
	/// <summary>The global the driver checks is absent after the worker's refused registration.</summary>
	internal const string ProbeGlobal = "cheatengine_client_qualification_worker_probe";

	/// <summary>Never callable in a qualification run: its registration from a worker is refused.</summary>
	[LuaFunction(ProbeGlobal)]
	public static string Probe()
	{
		return "worker-probe-registered";
	}
}
