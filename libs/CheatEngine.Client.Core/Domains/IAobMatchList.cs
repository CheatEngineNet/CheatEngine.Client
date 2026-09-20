using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal, activation-thread-owned view of an SDK AOB result list.</summary>
internal interface IAobMatchList : IDisposable
{
	public bool TryGetCount(out int count);

	public bool TryGetItem(int index, [NotNullWhen(true)] out string? value);
}
