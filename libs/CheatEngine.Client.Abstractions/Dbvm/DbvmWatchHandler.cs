namespace CheatEngine.Client.Dbvm;

/// <summary>Handles a copied DBVM watch event synchronously without blocking the host callback thread.</summary>
public delegate void DbvmWatchHandler(DbvmWatchEvent watchEvent);
