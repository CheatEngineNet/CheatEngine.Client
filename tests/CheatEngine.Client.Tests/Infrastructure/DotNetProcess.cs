using System.Diagnostics;
using System.Text;

namespace CheatEngine.Client.Tests.Infrastructure;

/// <summary>
/// Runs the pinned <c>dotnet</c> command, or another command-line tool such as <c>pwsh</c>, without shell quoting or
/// shared CLI state.
/// </summary>
internal static class DotNetProcess
{
	private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

	internal static Task<DotNetProcessResult> RunAsync(string workingDirectory, params string[] arguments)
	{
		return RunAsync(workingDirectory, new Dictionary<string, string>(StringComparer.Ordinal), arguments);
	}

	internal static Task<DotNetProcessResult> RunAsync(string workingDirectory,
		IReadOnlyDictionary<string, string> environment, params string[] arguments)
	{
		return RunToolAsync("dotnet", workingDirectory, environment, arguments);
	}

	/// <summary>Runs <paramref name="fileName"/>, found on the PATH, with the same isolation and timeout.</summary>
	internal static async Task<DotNetProcessResult> RunToolAsync(string fileName, string workingDirectory,
		IReadOnlyDictionary<string, string> environment, params string[] arguments)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
		ArgumentNullException.ThrowIfNull(environment);
		ArgumentNullException.ThrowIfNull(arguments);

		ProcessStartInfo startInfo = new(fileName)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			UseShellExecute = false
		};
		foreach (string argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach ((string name, string value) in environment)
		{
			startInfo.Environment[name] = value;
		}

		using Process process = new()
		{
			StartInfo = startInfo
		};
		if (!process.Start())
		{
			throw new InvalidOperationException($"The {fileName} process did not start.");
		}

		Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
		Task<string> standardError = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource cancellation = new(Timeout);
		try
		{
			await process.WaitForExitAsync(cancellation.Token);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			process.Kill(true);
			await process.WaitForExitAsync();
			throw new TimeoutException($"{fileName} {string.Join(' ', arguments)} exceeded {Timeout}.");
		}

		return new DotNetProcessResult(arguments, process.ExitCode, await standardOutput, await standardError, fileName);
	}
}

/// <summary>Captures a completed <c>dotnet</c> (or other tool) invocation for assertion diagnostics.</summary>
internal sealed record DotNetProcessResult(
	IReadOnlyList<string> Arguments,
	int ExitCode,
	string StandardOutput,
	string StandardError,
	string FileName = "dotnet")
{
	public override string ToString()
	{
		return $"{FileName} {string.Join(' ', Arguments)} exited with {ExitCode}.{Environment.NewLine}" +
			   $"stdout:{Environment.NewLine}{StandardOutput}{Environment.NewLine}" +
			   $"stderr:{Environment.NewLine}{StandardError}";
	}
}
