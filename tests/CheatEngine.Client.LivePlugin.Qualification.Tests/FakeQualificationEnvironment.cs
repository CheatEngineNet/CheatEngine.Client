using System.Globalization;

using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     An in-memory <see cref="IQualificationEnvironment" />: variables, files, the clock and process images are set by
///     the test, so every gate and fault-switch decision is proven without Cheat Engine or a target process.
/// </summary>
internal sealed class FakeQualificationEnvironment : IQualificationEnvironment
{
	internal const int HostProcessId = 4100;
	internal const int TargetProcessId = 4200;
	internal const string ManifestPath = "fixture/live-probe-authorization.json";
	internal const string TargetSha256 = "34A04005BCAF206EEC990BD9637D9FDB6725E0A0C0D4AEBF003F17F4C956EB5C";

	internal static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 15, 30, TimeSpan.Zero);

	private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, ProcessImage> _images = [];

	/// <summary>Creates an environment in which the gate allows the run: the exact host, a valid manifest, a live target.</summary>
	internal FakeQualificationEnvironment()
	{
		_variables[QualificationAuthorization.AcknowledgementVariable] = QualificationAuthorization.Acknowledgement;
		_variables[QualificationAuthorization.ManifestVariable] = ManifestPath;
		_images[HostProcessId] = new ProcessImage(QualificationAuthorization.ExactCheatEngineSha256, "Amd64",
			QualificationAuthorization.ExactCheatEngineFileVersion);
		_images[TargetProcessId] = new ProcessImage(TargetSha256, "Amd64", null);
		WriteManifest();
	}

	public DateTimeOffset UtcNow
	{
		get;
		set;
	} = Now;

	public int CurrentProcessId => HostProcessId;

	public bool Is64BitProcess
	{
		get;
		set;
	} = true;

	public string? GetVariable(string name)
	{
		return _variables.TryGetValue(name, out string? value) ? value : null;
	}

	public bool TryReadFile(string path, out string text)
	{
		return _files.TryGetValue(path.Replace('\\', '/'), out text!);
	}

	public bool TryDescribeProcessImage(int processId, out ProcessImage image)
	{
		return _images.TryGetValue(processId, out image);
	}

	internal void SetVariable(string name, string? value)
	{
		if (value is null)
		{
			_variables.Remove(name);
		}
		else
		{
			_variables[name] = value;
		}
	}

	internal void SetFile(string path, string? text)
	{
		string key = path.Replace('\\', '/');
		if (text is null)
		{
			_files.Remove(key);
		}
		else
		{
			_files[key] = text;
		}
	}

	internal void SetImage(int processId, ProcessImage? image)
	{
		if (image is { } value)
		{
			_images[processId] = value;
		}
		else
		{
			_images.Remove(processId);
		}
	}

	/// <summary>Writes the <c>ce77-live-probe-v1</c> manifest the SDK runner writes, with the given overrides.</summary>
	internal void WriteManifest(
		string acknowledgement = QualificationAuthorization.Acknowledgement,
		string hostSha256 = QualificationAuthorization.ExactCheatEngineSha256,
		int targetProcessId = TargetProcessId,
		string targetSha256 = TargetSha256,
		bool disposable = true,
		TimeSpan? validFor = null)
	{
		DateTimeOffset expires = Now + (validFor ?? TimeSpan.FromMinutes(30));
		string manifest = string.Create(CultureInfo.InvariantCulture, $$"""
			{
			  "schema": "{{QualificationAuthorization.ManifestSchema}}",
			  "acknowledgement": "{{acknowledgement}}",
			  "hostSha256": "{{hostSha256}}",
			  "targetProcessId": {{targetProcessId}},
			  "targetSha256": "{{targetSha256}}",
			  "disposable": {{(disposable ? "true" : "false")}},
			  "expiresUtc": "{{expires:o}}"
			}
			""");
		SetFile(ManifestPath, manifest);
	}
}
