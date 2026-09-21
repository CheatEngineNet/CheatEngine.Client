using System.Collections.Immutable;

using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.RemoteExecution;

/// <summary>Describes one bounded remote call and its copied parameter payload.</summary>
public readonly record struct RemoteCallRequest
{
	/// <summary>Creates a remote-call request.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout" /> is not strictly positive.</exception>
	public RemoteCallRequest(Address entryPoint, ReadOnlySpan<byte> parameters, TimeSpan timeout)
	{
		if (timeout <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(timeout), "A remote-call timeout must be strictly positive.");
		}

		EntryPoint = entryPoint;
		Parameters = ImmutableArray.Create(parameters);
		Timeout = timeout;
	}

	/// <summary>Gets the target function entry-point address.</summary>
	public Address EntryPoint
	{
		get;
	}

	/// <summary>Gets an immutable copy of the call parameter bytes.</summary>
	public ImmutableArray<byte> Parameters
	{
		get;
	}

	/// <summary>Gets the strictly positive call timeout.</summary>
	public TimeSpan Timeout
	{
		get;
	}
}
