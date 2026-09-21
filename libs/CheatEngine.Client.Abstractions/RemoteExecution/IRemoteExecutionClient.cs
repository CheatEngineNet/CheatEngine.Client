using CheatEngine.Client.Results;

namespace CheatEngine.Client.RemoteExecution;

/// <summary>Injects existing DLLs and executes bounded target calls without exposing target allocation handles.</summary>
public interface IRemoteExecutionClient
{
	/// <summary>Tries to inject an existing absolute DLL path into the selected target process.</summary>
	public bool TryInjectLibrary(RemoteDllInjectionRequest request, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Injects an existing absolute DLL path or throws when the host rejects it.</summary>
	public void InjectLibrary(RemoteDllInjectionRequest request, CancellationToken cancellationToken = default);

	/// <summary>Tries to call a target function with a positive timeout and copied parameter result data.</summary>
	public bool TryInvoke(RemoteCallRequest request, out RemoteCallResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Calls a target function or throws when remote execution fails.</summary>
	public RemoteCallResult Invoke(RemoteCallRequest request, CancellationToken cancellationToken = default);
}
