using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Delegates the Hosting cleanup boundary to the activation-owned Core lifetime.</summary>
internal sealed class CheatEngineClientActivationCleanup(CoreLifetime lifetime)
	: ICheatEngineClientActivationCleanup
{
	private readonly CoreLifetime _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));

	public IDisposable EnterCleanupScope()
	{
		return _lifetime.EnterCleanupScope();
	}

	public void DrainOwnedResourcesForDisable()
	{
		_lifetime.DrainOwnedResourcesForDisable();
	}
}
