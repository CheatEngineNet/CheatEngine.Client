namespace CheatEngine.Client.Tables;

/// <summary>Options for saving the current Cheat Engine table without advanced signing or protection.</summary>
public readonly record struct TableSaveRequest(TrustedTableFile File);
