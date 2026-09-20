using CheatEngine.Client.Modules;

namespace CheatEngine.Client.Hosting;

/// <summary>Owns the reversible application callbacks inside one already-created Client activation.</summary>
/// <remarks>
///     This type deliberately knows nothing about the SDK, service-provider construction, or resource disposal. Keeping
///     the module/hook sequence isolated lets it be unit-tested without faking the SDK's process-wide plugin host.
/// </remarks>
internal sealed class ClientActivationLifecycle
{
	private readonly ICheatEngineClient _client;
	private readonly ICheatEngineClientModule[] _modules;
	private bool _cleanupStarted;
	private bool _clientEnableHookEntered;
	private int _enabledModuleCount;

	internal ClientActivationLifecycle(ICheatEngineClient client, ICheatEngineClientModule[] modules)
	{
		_client = client ?? throw new ArgumentNullException(nameof(client));
		_modules = modules ?? throw new ArgumentNullException(nameof(modules));
	}

	/// <summary>Enables modules in registration order, then invokes the application enable hook.</summary>
	/// <remarks>
	///     Each callback is marked as entered before execution. Consequently a callback that partially initializes and then
	///     throws still receives its compensating disable callback during rollback.
	/// </remarks>
	internal void Enable(Action<ICheatEngineClient> onClientEnabled)
	{
		ArgumentNullException.ThrowIfNull(onClientEnabled);

		for (int index = 0; index < _modules.Length; index++)
		{
			_enabledModuleCount = index + 1;
			_modules[index].OnEnabled(_client);
		}

		_clientEnableHookEntered = true;
		onClientEnabled(_client);
	}

	/// <summary>Runs compensations exactly once: application hook, then modules in reverse enable order.</summary>
	/// <remarks>
	///     Cleanup is best-effort: every callback gets a chance to release its state and all failures are returned to the
	///     owner for aggregation after provider and configuration cleanup also complete.
	/// </remarks>
	internal List<Exception> Cleanup(Action<ICheatEngineClient> onClientDisabling)
	{
		ArgumentNullException.ThrowIfNull(onClientDisabling);
		if (_cleanupStarted)
		{
			return [];
		}

		_cleanupStarted = true;
		List<Exception> failures = [];

		if (_clientEnableHookEntered)
		{
			try
			{
				onClientDisabling(_client);
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}
		}

		for (int index = _enabledModuleCount - 1; index >= 0; index--)
		{
			try
			{
				_modules[index].OnDisabling(_client);
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}
		}

		return failures;
	}
}
