namespace CheatEngine.Client.Processes;

/// <summary>Describes an explicit executable launch that must also become the selected Cheat Engine target.</summary>
public readonly record struct ProcessStartRequest
{
	/// <summary>Creates an explicit process-launch request.</summary>
	public ProcessStartRequest(string executablePath, string? arguments = null, string? workingDirectory = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
		if (!Path.IsPathFullyQualified(executablePath))
		{
			throw new ArgumentException("The executable path must be absolute.", nameof(executablePath));
		}

		if (workingDirectory is { } directory && !Path.IsPathFullyQualified(directory))
		{
			throw new ArgumentException("The working directory must be absolute when specified.",
				nameof(workingDirectory));
		}

		ExecutablePath = executablePath;
		Arguments = arguments;
		WorkingDirectory = workingDirectory;
	}

	/// <summary>Gets the absolute executable path.</summary>
	public string ExecutablePath
	{
		get;
	}

	/// <summary>Gets the optional command-line arguments.</summary>
	public string? Arguments
	{
		get;
	}

	/// <summary>Gets the optional absolute working directory.</summary>
	public string? WorkingDirectory
	{
		get;
	}
}
