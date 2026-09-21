namespace CheatEngine.Client.Hashing;

/// <summary>Contains a copied, normalized digest value.</summary>
public readonly record struct HashDigest
{
	/// <summary>Creates a digest result.</summary>
	/// <exception cref="ArgumentException"><paramref name="value" /> is blank or the algorithm is undefined.</exception>
	public HashDigest(TargetHashAlgorithm algorithm, string value)
	{
		if (!Enum.IsDefined(algorithm))
		{
			throw new ArgumentOutOfRangeException(nameof(algorithm));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		Algorithm = algorithm;
		Value = value;
	}

	/// <summary>Gets the algorithm that produced <see cref="Value" />.</summary>
	public TargetHashAlgorithm Algorithm
	{
		get;
	}

	/// <summary>Gets the copied textual digest.</summary>
	public string Value
	{
		get;
	}
}
