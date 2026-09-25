namespace CheatEngine.Client.Tables;

/// <summary>Options for loading one explicitly trusted Cheat Engine table.</summary>
public readonly record struct TableLoadRequest
{
	/// <summary>Creates the options of one trusted table load.</summary>
	/// <param name="file">The table file, already admitted by the activation's allowed table roots.</param>
	/// <param name="merge">Whether the table is merged into the current Address List instead of replacing it.</param>
	public TableLoadRequest(TrustedTableFile file, bool merge = false)
	{
		File = file;
		Merge = merge;
	}

	/// <summary>Gets the table file to load.</summary>
	public TrustedTableFile File
	{
		get;
	}

	/// <summary>Gets whether the table is merged into the current Address List instead of replacing it.</summary>
	public bool Merge
	{
		get;
	}
}
