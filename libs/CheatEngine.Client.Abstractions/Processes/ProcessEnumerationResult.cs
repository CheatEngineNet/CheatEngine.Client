using System.Collections.Immutable;

namespace CheatEngine.Client.Processes;

/// <summary>A copied, bounded local-process enumeration result.</summary>
public readonly record struct ProcessEnumerationResult
{
	/// <summary>Creates a copied process enumeration result.</summary>
	public ProcessEnumerationResult(ImmutableArray<ProcessInfoSnapshot> processes, bool isTruncated)
	{
		Processes = processes.IsDefault ? ImmutableArray<ProcessInfoSnapshot>.Empty : processes;
		if (isTruncated && Processes.IsEmpty)
		{
			throw new ArgumentException("A truncated process result must retain at least one copied process.",
				nameof(processes));
		}

		IsTruncated = isTruncated;
	}

	/// <summary>Gets copied local-process metadata, ordered by process identifier.</summary>
	public ImmutableArray<ProcessInfoSnapshot> Processes
	{
		get;
	}

	/// <summary>Gets whether matching local processes were omitted at the caller's explicit limit.</summary>
	public bool IsTruncated
	{
		get;
	}
}
