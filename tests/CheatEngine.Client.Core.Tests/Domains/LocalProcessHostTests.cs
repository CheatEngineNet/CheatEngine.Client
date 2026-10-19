using System.Diagnostics;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;

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
	public void GetLocalProcessesContainsCopiedMetadataForTheCurrentManagedProcess()
	{
		using Process current = Process.GetCurrentProcess();
		LocalProcessHost host = new();

		IReadOnlyList<LocalProcessInfo> processes = host.GetLocalProcesses();
		LocalProcessInfo currentSnapshot = Assert.Single(processes, process => process.Id == current.Id);

		Assert.Equal(current.ProcessName, currentSnapshot.Name);
	}

	[Fact]
	public void LocalCatalogSkipsSystemPseudoProcessesWhileRetainingTheCurrentProcess()
	{
		using Process current = Process.GetCurrentProcess();
		LocalProcessHost host = new();

		bool succeeded = LocalProcessCatalog.TryEnumerate(host,
			new LocalProcessEnumerationRequest(int.MaxValue), out LocalProcessEnumerationResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Contains(result.Processes, process =>
			process.Id.Value == current.Id && process.Name == current.ProcessName);
		Assert.All(result.Processes, process => Assert.True(process.Id.Value > 0));
	}
}
