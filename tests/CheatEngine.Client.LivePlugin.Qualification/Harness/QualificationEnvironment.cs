using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace LivePlugin.Qualification.Harness;

/// <summary>
///     The production <see cref="IQualificationEnvironment" />: process variables, files, the clock, and read-only facts
///     about process images (SHA-256, COFF machine, file version). It opens no handle other than a read of the image files
///     and never writes anything.
/// </summary>
internal sealed class QualificationEnvironment : IQualificationEnvironment
{
	/// <summary>The shared instance.</summary>
	internal static readonly QualificationEnvironment Instance = new();

	private readonly Dictionary<string, ProcessImage> _imagesByPath = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _gate = new();

	private QualificationEnvironment()
	{
	}

	/// <inheritdoc />
	public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

	/// <inheritdoc />
	public int CurrentProcessId => Environment.ProcessId;

	/// <inheritdoc />
	public bool Is64BitProcess => Environment.Is64BitProcess;

	/// <inheritdoc />
	public string? GetVariable(string name)
	{
		return Environment.GetEnvironmentVariable(name);
	}

	/// <inheritdoc />
	public bool TryReadFile(string path, out string text)
	{
		text = string.Empty;
		try
		{
			string fullPath = Path.GetFullPath(path);
			if (!File.Exists(fullPath))
			{
				return false;
			}

			text = File.ReadAllText(fullPath);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
											  or NotSupportedException)
		{
			return false;
		}
	}

	/// <inheritdoc />
	public bool TryDescribeProcessImage(int processId, out ProcessImage image)
	{
		image = default;
		string? path;
		try
		{
			using Process process = Process.GetProcessById(processId);
			path = process.MainModule?.FileName;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
											  or NotSupportedException or Win32Exception)
		{
			return false;
		}

		if (string.IsNullOrEmpty(path))
		{
			return false;
		}

		lock (_gate)
		{
			if (_imagesByPath.TryGetValue(path, out image))
			{
				return true;
			}
		}

		try
		{
			string sha256;
			string machine;
			using (FileStream stream = File.OpenRead(path))
			{
				sha256 = Convert.ToHexString(SHA256.HashData(stream));
				stream.Position = 0;
				using PEReader reader = new(stream, PEStreamOptions.LeaveOpen);
				machine = reader.PEHeaders.CoffHeader.Machine.ToString();
			}

			image = new ProcessImage(sha256, machine, FileVersionInfo.GetVersionInfo(path).FileVersion);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
											  or BadImageFormatException)
		{
			return false;
		}

		lock (_gate)
		{
			_imagesByPath[path] = image;
		}

		return true;
	}
}
