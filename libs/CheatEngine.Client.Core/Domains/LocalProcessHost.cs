using System.ComponentModel;
using System.Diagnostics;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production process host: local metadata through the base class library.</summary>
/// <remarks>
///     Name and executable path come from <see cref="Process" /> and describe a local process only: they are not evidence
///     of a CEServer target or of a file opened as a process, whose identifiers do not name a local process.
/// </remarks>
internal sealed class LocalProcessHost : IProcessHost
{
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

	public IReadOnlyList<LocalProcessInfo> GetLocalProcesses()
	{
		using ProcessCollection processes = new(Process.GetProcesses());
		List<LocalProcessInfo> captured = new(processes.Count);
		for (int index = 0; index < processes.Count; index++)
		{
			if (TryCapture(processes[index], out LocalProcessInfo process))
			{
				captured.Add(process);
			}
		}

		return captured;
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
