using System.Collections.Immutable;

namespace CheatEngine.Client.RemoteExecution;

/// <summary>Contains the copied result returned by a completed remote call.</summary>
public readonly record struct RemoteCallResult
{
	/// <summary>Creates a copied remote-call result.</summary>
	public RemoteCallResult(ulong returnValue, ReadOnlySpan<byte> output)
	{
		ReturnValue = returnValue;
		Output = ImmutableArray.Create(output);
	}

	/// <summary>Gets the target ABI return value represented as an unsigned machine value.</summary>
	public ulong ReturnValue
	{
		get;
	}

	/// <summary>Gets an immutable copy of any requested output parameter bytes.</summary>
	public ImmutableArray<byte> Output
	{
		get;
	}
}
