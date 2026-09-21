using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains.RemoteExecution;

/// <summary>Preserves remote-execution intent contracts until allocation and thread-affinity live gates are complete.</summary>
internal sealed class UnavailableRemoteExecutionClient : IRemoteExecutionClient
{
	public bool TryInjectLibrary(RemoteDllInjectionRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		failure = CreateFailure("RemoteExecution.InjectLibrary", cancellationToken);
		return false;
	}

	public void InjectLibrary(RemoteDllInjectionRequest request, CancellationToken cancellationToken = default)
	{
		_ = TryInjectLibrary(request, out CheatEngineFailure failure, cancellationToken);
		UnavailableCapabilityFailure.Throw(failure);
	}

	public bool TryInvoke(RemoteCallRequest request, out RemoteCallResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		result = default;
		failure = CreateFailure("RemoteExecution.Invoke", cancellationToken);
		return false;
	}

	public RemoteCallResult Invoke(RemoteCallRequest request, CancellationToken cancellationToken = default)
	{
		_ = TryInvoke(request, out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<RemoteCallResult>(failure);
	}

	private static CheatEngineFailure CreateFailure(string operation, CancellationToken cancellationToken)
	{
		return UnavailableCapabilityFailure.Create("Remote execution and injection", operation, cancellationToken);
	}
}
