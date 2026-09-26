using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>
///     Cheat Engine's selected local process for a real <see cref="ProcessClient" />, the selection binder of target-bound
///     leases: by default the x64 process 42 in its <see cref="FirstIncarnation" />. <see cref="Select" /> changes the
///     selection as Cheat Engine's own window does, without any Client call.
/// </summary>
internal sealed class FakeSelectedTarget : FakeRuntimeObservationPort, IProcessHost
{
	internal FakeSelectedTarget()
	{
		Select(FirstIncarnation);
	}

	/// <summary>Gets the incarnation of process 42 that the scripted SDK owners are bound to by default.</summary>
	internal static TargetProcessIncarnation FirstIncarnation =>
		TargetObservations.Incarnation(42, 638_000_000_000_000_000);

	/// <summary>Gets an incarnation of another process, 43.</summary>
	internal static TargetProcessIncarnation OtherProcessIncarnation =>
		TargetObservations.Incarnation(43, 638_000_000_100_000_000);

	/// <summary>Creates the process client of an activation over a selected target (a new one when omitted).</summary>
	/// <param name="dispatcher">The activation dispatcher.</param>
	/// <param name="target">The selected target.</param>
	/// <returns>The process client, which binds new target-bound owners to the selection.</returns>
	internal static ProcessClient CreateProcessClient(SdkMainThreadDispatcher dispatcher,
		FakeSelectedTarget? target = null)
	{
		target ??= new FakeSelectedTarget();
		return new ProcessClient(dispatcher, target, target, target, dispatcher.Lifetime);
	}

	/// <summary>Selects a local process incarnation, as Cheat Engine's own window does.</summary>
	/// <param name="incarnation">The newly selected incarnation.</param>
	internal void Select(TargetProcessIncarnation incarnation)
	{
		Target = TargetObservations.WithProcessId(Target, incarnation.ProcessId);
		Incarnation = incarnation;
	}

	public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
	{
		process = default;
		return false;
	}

	public IReadOnlyList<LocalProcessInfo> GetLocalProcesses()
	{
		return [];
	}

	public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName)
	{
		return [];
	}
}
