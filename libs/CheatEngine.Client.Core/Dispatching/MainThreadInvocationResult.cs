namespace CheatEngine.Client.Core.Dispatching;

internal readonly record struct MainThreadInvocationResult<T>(T Result, Exception? Exception);
