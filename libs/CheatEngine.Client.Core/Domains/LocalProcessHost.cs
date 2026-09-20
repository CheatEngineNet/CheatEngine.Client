using System.ComponentModel;
using System.Diagnostics;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

internal sealed class LocalProcessHost : IProcessHost
{
	public long GetOpenedProcessId()
	{
		return ClientLuaGlobals.GetOpenedProcessId();
	}

	public void OpenProcess(long processId)
	{
		ClientLuaGlobals.OpenProcess(processId);
	}

	public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
	{
		try
		{
			using Process managedProcess = Process.GetProcessById(processId);
			return TryCapture(managedProcess, out process);
		}
		catch (ArgumentException)
		{
			process = default;
			return false;
		}
	}

	public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(processName);
		using ProcessCollection processes = new(Process.GetProcessesByName(processName));
		List<LocalProcessInfo> matches = new(processes.Count);
		for (int index = 0; index < processes.Count; index++)
		{
			if (TryCapture(processes[index], out LocalProcessInfo process) &&
			    string.Equals(process.Name, processName, StringComparison.OrdinalIgnoreCase))
			{
				matches.Add(process);
			}
		}

		return matches;
	}

	public CheatEngineArchitecture GetTargetArchitecture()
	{
		return ClientLuaGlobals.TargetIs64Bit()
			? CheatEngineArchitecture.X64
			: CheatEngineArchitecture.X86;
	}

	private static bool TryCapture(Process process, out LocalProcessInfo captured)
	{
		try
		{
			string? executablePath = TryGetExecutablePath(process);
			captured = new LocalProcessInfo(process.Id, process.ProcessName, executablePath);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
		{
			captured = default;
			return false;
		}
	}

	private static string? TryGetExecutablePath(Process process)
	{
		try
		{
			return process.MainModule?.FileName;
		}
		catch (Exception exception) when (exception is InvalidOperationException or Win32Exception
			                                  or NotSupportedException)
		{
			return null;
		}
	}

	private sealed class ProcessCollection(Process[] processes) : IDisposable
	{
		private readonly Process[] _processes = processes;

		internal int Count => _processes.Length;

		internal Process this[int index] => _processes[index];

		public void Dispose()
		{
			foreach (Process process in _processes)
			{
				process.Dispose();
			}
		}
	}
}
