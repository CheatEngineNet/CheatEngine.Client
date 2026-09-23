using CheatEngine.Client.Core.Domains.Events;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Speed;

namespace CheatEngine.Client.Core.Domains.Speed;

/// <summary>Preserves validated speed semantics until Cheat Engine speed control passes its live-host gate.</summary>
internal sealed class UnavailableSpeedClient : ISpeedClient
{
	private readonly CoreLifetime? _lifetime;

	internal UnavailableSpeedClient(CoreLifetime? lifetime = null)
	{
		_lifetime = lifetime;
	}

	public bool TryGetMultiplier(out SpeedMultiplier multiplier, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		multiplier = default;
		failure = CreateFailure("Speed.GetMultiplier", cancellationToken);
		return false;
	}

	public SpeedMultiplier GetMultiplier(CancellationToken cancellationToken = default)
	{
		_ = TryGetMultiplier(out _, out CheatEngineFailure failure, cancellationToken);
		return UnavailableCapabilityFailure.Throw<SpeedMultiplier>(failure);
	}

	public bool TrySetMultiplier(SpeedMultiplier multiplier, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		failure = CreateFailure("Speed.SetMultiplier", cancellationToken);
		return false;
	}

	public void SetMultiplier(SpeedMultiplier multiplier, CancellationToken cancellationToken = default)
	{
		_ = TrySetMultiplier(multiplier, out CheatEngineFailure failure, cancellationToken);
		UnavailableCapabilityFailure.Throw(failure);
	}

	private CheatEngineFailure CreateFailure(string operation, CancellationToken cancellationToken)
	{
		return UnavailableCapabilityFailure.Create(_lifetime, ClientCapabilityId.Speed, "Target speed control",
			operation, cancellationToken);
	}
}
