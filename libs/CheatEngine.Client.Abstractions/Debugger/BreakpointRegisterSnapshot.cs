namespace CheatEngine.Client.Debugger;

/// <summary>Contains one copied register observation associated with a breakpoint event.</summary>
public readonly record struct BreakpointRegisterSnapshot
{
	/// <summary>Creates a copied register observation.</summary>
	/// <exception cref="ArgumentException"><paramref name="name" /> is blank.</exception>
	public BreakpointRegisterSnapshot(string name, ulong value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		Value = value;
	}

	/// <summary>Gets the normalized register name.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the copied register machine value.</summary>
	public ulong Value
	{
		get;
	}
}
