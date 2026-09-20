namespace CheatEngine.Client.Tables;

/// <summary>Options for loading one explicitly trusted Cheat Engine table.</summary>
public readonly record struct TableLoadRequest(TrustedTableFile File, bool Merge = false);
