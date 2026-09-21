namespace CheatEngine.Client.Dbvm;

/// <summary>Contains a copied DBVM status observation.</summary>
public readonly record struct DbvmStatusSnapshot
{
	/// <summary>Creates a copied DBVM status observation.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="state" /> is not a defined value.</exception>
	public DbvmStatusSnapshot(DbvmState state, string? version = null)
	{
		if (!Enum.IsDefined(state))
		{
			throw new ArgumentOutOfRangeException(nameof(state));
		}

		if (version is not null && string.IsNullOrWhiteSpace(version))
		{
			throw new ArgumentException("A DBVM version must be null or non-empty.", nameof(version));
		}

		State = state;
		Version = version;
	}

	/// <summary>Gets the observed state without causing initialization.</summary>
	public DbvmState State
	{
		get;
	}

	/// <summary>Gets the optional copied DBVM version reported by the host.</summary>
	public string? Version
	{
		get;
	}
}
