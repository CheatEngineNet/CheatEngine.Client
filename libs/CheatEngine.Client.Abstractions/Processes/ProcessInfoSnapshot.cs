using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Processes;

/// <summary>Copied local-process metadata that is independent of Cheat Engine's selected target.</summary>
public readonly record struct ProcessInfoSnapshot
{
	/// <summary>Creates copied local-process metadata.</summary>
	public ProcessInfoSnapshot(TargetProcessId id, string? name, string? executablePath)
	{
		if (name is { Length: 0 })
		{
			throw new ArgumentException("A process name must be null or non-empty.", nameof(name));
		}

		if (executablePath is { Length: 0 })
		{
			throw new ArgumentException("An executable path must be null or non-empty.", nameof(executablePath));
		}

		Id = id;
		Name = name;
		ExecutablePath = executablePath;
	}

	/// <summary>Gets the local process identifier.</summary>
	public TargetProcessId Id
	{
		get;
	}

	/// <summary>Gets the display name when managed metadata could be read.</summary>
	public string? Name
	{
		get;
	}

	/// <summary>Gets the executable path when managed metadata could be read.</summary>
	public string? ExecutablePath
	{
		get;
	}
}
