namespace CheatEngine.Client.Tables;

/// <summary>Options for saving the current Cheat Engine table without advanced signing or protection.</summary>
public readonly record struct TableSaveRequest
{
	/// <summary>Creates the options of one table save.</summary>
	/// <param name="file">The destination file, already admitted by the activation's allowed table roots.</param>
	public TableSaveRequest(TrustedTableFile file)
	{
		File = file;
	}

	/// <summary>Gets the destination file.</summary>
	public TrustedTableFile File
	{
		get;
	}
}
