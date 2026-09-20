using System.Diagnostics;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Copied local process metadata; it does not own a <see cref="Process" /> handle.</summary>
internal readonly record struct LocalProcessInfo(int Id, string? Name, string? ExecutablePath);
