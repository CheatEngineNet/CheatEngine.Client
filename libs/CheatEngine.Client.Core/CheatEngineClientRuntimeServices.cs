using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core;

/// <summary>Internal grouping of the façade services that describe the active runtime and its dispatch boundary.</summary>
internal sealed record CheatEngineClientRuntimeServices(
	ICheatEngineRuntime Runtime,
	ICheatEngineDispatcher Dispatcher);
