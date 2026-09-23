using System.Diagnostics;

using CheatEngine.Client.Core.Domains;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class LocalProcessHostTests
{
	[Fact]
	public void TryGetLocalProcessCopiesMetadataForTheCurrentManagedProcess()
	{
		using Process current = Process.GetCurrentProcess();
		LocalProcessHost host = new();

		bool succeeded = host.TryGetLocalProcess(current.Id, out LocalProcessInfo process);

		Assert.True(succeeded);
		Assert.Equal(current.Id, process.Id);
		Assert.Equal(current.ProcessName, process.Name);
	}

	[Fact]
	public void TryGetLocalProcessReturnsFalseForAnInvalidProcessId()
	{
		LocalProcessHost host = new();

		bool succeeded = host.TryGetLocalProcess(-1, out LocalProcessInfo process);

		Assert.False(succeeded);
		Assert.Equal(default, process);
	}

	[Fact]
	[Trait("Qualification", "Q45")]
	public void LocalProcessHostArchitectureProbesRequireAnEnabledPluginContext()
	{
		// The fact members are read-only generated bindings: without an enabled plugin the SDK refuses them before
		// reaching Cheat Engine.
		LocalProcessHost host = new();

		Assert.Contains("plugin is not enabled",
			Assert.Throws<InvalidOperationException>(() => host.TargetIs64Bit()).Message,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains("plugin is not enabled",
			Assert.Throws<InvalidOperationException>(() => host.TargetIsX86()).Message,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains("plugin is not enabled",
			Assert.Throws<InvalidOperationException>(() => host.TargetIsArm()).Message,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains("plugin is not enabled",
			Assert.Throws<InvalidOperationException>(() => host.GetConfiguredPointerSize()).Message,
			StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void GetLocalProcessesContainsCopiedMetadataForTheCurrentManagedProcess()
	{
		using Process current = Process.GetCurrentProcess();
		LocalProcessHost host = new();

		IReadOnlyList<LocalProcessInfo> processes = host.GetLocalProcesses();
		LocalProcessInfo currentSnapshot = Assert.Single(processes, process => process.Id == current.Id);

		Assert.Equal(current.ProcessName, currentSnapshot.Name);
	}
}
