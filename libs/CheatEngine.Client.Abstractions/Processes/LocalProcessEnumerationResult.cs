using System.Collections.Immutable;

namespace CheatEngine.Client.Processes;

/// <summary>A copied, bounded local-process enumeration result.</summary>
public readonly struct LocalProcessEnumerationResult
{
	private readonly ImmutableArray<LocalProcessSnapshot> _processes;

	/// <summary>Creates a copied process enumeration result.</summary>
	public LocalProcessEnumerationResult(ImmutableArray<LocalProcessSnapshot> processes, bool isTruncated)
	{
		_processes = processes.IsDefault ? ImmutableArray<LocalProcessSnapshot>.Empty : processes;
		if (isTruncated && _processes.IsEmpty)
		{
			throw new ArgumentException("A truncated process result must retain at least one copied process.",
				nameof(processes));
		}

		IsTruncated = isTruncated;
	}

	/// <summary>Gets copied local-process metadata, ordered by process identifier.</summary>
	/// <remarks>Empty for the <see langword="default" /> value, never a default array.</remarks>
	public ImmutableArray<LocalProcessSnapshot> Processes =>
		_processes.IsDefault ? ImmutableArray<LocalProcessSnapshot>.Empty : _processes;

	/// <summary>Gets whether matching local processes were omitted at the caller's explicit limit.</summary>
	public bool IsTruncated
	{
		get;
	}
}
