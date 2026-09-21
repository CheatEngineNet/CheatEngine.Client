namespace CheatEngine.Client.Assembly;

/// <summary>Owns one Client-applied Auto Assembler patch for the current activation and target selection.</summary>
public interface IAutoAssemblerPatchLease : IDisposable
{
	/// <summary>Gets the Client diagnostic name supplied when the patch was applied.</summary>
	public string? Name
	{
		get;
	}

	/// <summary>Gets the target-selection epoch captured by this patch.</summary>
	public long SelectionEpoch
	{
		get;
	}

	/// <summary>Gets whether the patch has already executed its disable cleanup or was invalidated.</summary>
	public bool IsReleased
	{
		get;
	}
}
