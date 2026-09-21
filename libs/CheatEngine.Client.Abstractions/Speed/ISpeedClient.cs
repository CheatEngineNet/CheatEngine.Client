using CheatEngine.Client.Results;

namespace CheatEngine.Client.Speed;

/// <summary>Reads and updates the selected target's validated speed multiplier.</summary>
public interface ISpeedClient
{
	/// <summary>Tries to get the selected target's current speed multiplier.</summary>
	public bool TryGetMultiplier(out SpeedMultiplier multiplier, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Gets the selected target's current speed multiplier or throws when unavailable.</summary>
	public SpeedMultiplier GetMultiplier(CancellationToken cancellationToken = default);

	/// <summary>Tries to set a finite, strictly positive speed multiplier.</summary>
	public bool TrySetMultiplier(SpeedMultiplier multiplier, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Sets a finite, strictly positive speed multiplier or throws when unavailable.</summary>
	public void SetMultiplier(SpeedMultiplier multiplier, CancellationToken cancellationToken = default);
}
