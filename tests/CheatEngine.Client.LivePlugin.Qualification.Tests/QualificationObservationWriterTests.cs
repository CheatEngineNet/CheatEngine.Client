using System.Text.Json;

using CheatEngine.Client.Results;

using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     The JSON a harness function returns is bounded and redacted by construction: it names its schema, never carries a
///     local path, a failure message or exception text, and reduces an address list to its count and edges.
/// </summary>
public sealed class QualificationObservationWriterTests
{
	[Fact]
	public void ObservationJsonHasTheSchemaIdAndNoLocalPath()
	{
		using QualificationObservation observation = new("status");

		string json = observation.String("plugin", @"C:\Users\someone\ce\plugins\CheatEngine.Client.LivePlugin.Qualification.dll")
			.String("unc", @"\\server\share\bundle")
			.String("home", "/home/someone/bundle")
			.String("uri", "file:///c:/bundle")
			.String("kept", "https://github.com/CheatEngineNet/CheatEngine.Client and %LOCALAPPDATA%")
			.Strings("list", [@"D:\wt\bundle", "stage.enabled"])
			.Complete();
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		Assert.Equal(QualificationObservation.Schema, root.GetProperty("schema").GetString());
		Assert.Equal("status", root.GetProperty("function").GetString());
		foreach (string name in (string[]) ["plugin", "unc", "home", "uri"])
		{
			Assert.Equal(QualificationObservation.RedactedPath, root.GetProperty(name).GetString());
		}

		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.Client and %LOCALAPPDATA%",
			root.GetProperty("kept").GetString());
		Assert.Equal([QualificationObservation.RedactedPath, "stage.enabled"],
			root.GetProperty("list").EnumerateArray().Select(static item => item.GetString()!));
		Assert.DoesNotContain("someone", json, StringComparison.Ordinal);
	}

	[Fact]
	public void ObservationNeverCarriesFailureMessagesOrExceptionText()
	{
		CheatEngineFailure failure = new(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
			@"No result list for pattern DE AD BE EF at C:\Users\someone\target.exe",
			new InvalidOperationException("secret 0x7FF712345678"));
		using QualificationObservation observation = new("aob");

		string json = observation.Failure("failure", failure).Complete();
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement written = document.RootElement.GetProperty("failure");

		Assert.Equal("IndeterminateHostResult", written.GetProperty("kind").GetString());
		Assert.Equal("Patterns.Scan", written.GetProperty("operation").GetString());
		Assert.Equal(failure.HostEffect.ToString(), written.GetProperty("hostEffect").GetString());
		Assert.Equal(["kind", "operation", "hostEffect"], written.EnumerateObject().Select(static property => property.Name));
		Assert.DoesNotContain("DE AD BE EF", json, StringComparison.Ordinal);
		Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
		Assert.DoesNotContain("0x7FF712345678", json, StringComparison.Ordinal);
	}

	[Fact]
	public void AddressListsAreBoundedToTheFirstAndLastEntries()
	{
		ulong[] many = [.. Enumerable.Range(0, 20000).Select(static index => 0x1_0000_0000UL + ((ulong) index * 8))];
		ulong[] few = [0x1000, 0x2000, 0x3000];
		ulong[] twelve = [.. Enumerable.Range(1, 12).Select(static index => (ulong) index)];
		using QualificationObservation observation = new("aob");

		string json = observation.Addresses("many", many).Addresses("few", few).Addresses("twelve", twelve)
			.Addresses("none", []).Complete();
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		Assert.Equal(20000, root.GetProperty("many").GetProperty("count").GetInt32());
		Assert.Equal(QualificationObservation.AddressListEdge, root.GetProperty("many").GetProperty("first").GetArrayLength());
		Assert.Equal(QualificationObservation.AddressListEdge, root.GetProperty("many").GetProperty("last").GetArrayLength());
		Assert.Equal("0x100000000", root.GetProperty("many").GetProperty("first")[0].GetString());
		Assert.Equal("0x1000270F8", root.GetProperty("many").GetProperty("last")[7].GetString());
		Assert.Equal(["0x1000", "0x2000", "0x3000"],
			root.GetProperty("few").GetProperty("first").EnumerateArray().Select(static item => item.GetString()!));
		Assert.Equal(0, root.GetProperty("few").GetProperty("last").GetArrayLength());
		Assert.Equal(["0x9", "0xA", "0xB", "0xC"],
			root.GetProperty("twelve").GetProperty("last").EnumerateArray().Select(static item => item.GetString()!));
		Assert.Equal(0, root.GetProperty("none").GetProperty("count").GetInt32());
		Assert.True(json.Length < 2048, "An observation of 20000 addresses stays small.");
	}

	[Fact]
	public void UnknownFactsAreWrittenAsNullNeverAsADefault()
	{
		using QualificationObservation observation = new("runtime");

		string json = observation.OptionalNumber("configuredPointerSizeBytes", null)
			.OptionalNumber("knownBytes", 8)
			.OptionalBoolean("targetIsAndroid", null)
			.OptionalBoolean("differsFromBitness", false)
			.Complete();
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredPointerSizeBytes").ValueKind);
		Assert.Equal(8, root.GetProperty("knownBytes").GetInt32());
		Assert.Equal(JsonValueKind.Null, root.GetProperty("targetIsAndroid").ValueKind);
		Assert.Equal(JsonValueKind.False, root.GetProperty("differsFromBitness").ValueKind);
	}
}
