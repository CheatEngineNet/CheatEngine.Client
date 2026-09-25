using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.ValueScanning;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     The AOB options and the value-scan first request share one translation of the public scan options, so both scan
///     routes send Cheat Engine the same protection text and fast-scan method for every filter and alignment.
/// </summary>
public sealed class ScanOptionTranslationTests
{
	private const string Operation = "ValueScans.FirstScan";

	[Fact]
	public void BothScanRoutesWriteTheSameOptionsForEveryFilterAndAlignment()
	{
		ScanProtectionRequirement[] requirements = Enum.GetValues<ScanProtectionRequirement>();
		ScanAlignment[] alignments = [ScanAlignment.None, ScanAlignment.AlignedTo(16), ScanAlignment.LastDigits("f0")];
		int compared = 0;
		foreach (ScanAlignment alignment in alignments)
		{
			foreach (ScanProtectionRequirement executable in requirements)
			{
				foreach (ScanProtectionRequirement copyOnWrite in requirements)
				{
					foreach (ScanProtectionRequirement writable in requirements)
					{
						ScanProtectionFilter protection = new(executable, copyOnWrite, writable);
						AobScanOptions aob = AobScanMapping.ToSdkOptions(protection, alignment);
						Assert.True(ValueScanRequests.TryCreateFirst(
							ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)).WithProtection(protection)
								.WithAlignment(alignment), Operation, out FirstScanRequest values, out _));

						Assert.Equal(aob.ProtectionFlags, values.ProtectionFlags);
						Assert.Equal(aob.AlignmentMethod, values.FastScanMethod);
						// The only difference is the positional request's empty parameter where the AOB options omit it.
						Assert.Equal(aob.AlignmentParameter ?? string.Empty, values.AlignmentParameter);
						compared++;
					}
				}
			}
		}

		Assert.Equal(alignments.Length * requirements.Length * requirements.Length * requirements.Length, compared);
	}

	[Fact]
	public void BothScanRoutesAcceptEveryOptionThePublicFactoriesCreate()
	{
		// One check serves both routes: every value a public constructor or factory creates is defined, and so are the
		// default filter and alignment; only a tampered value is refused, by the AOB and value-scan validation alike.
		foreach (ScanProtectionRequirement requirement in Enum.GetValues<ScanProtectionRequirement>())
		{
			ScanProtectionFilter filter = new(requirement, requirement, requirement);
			Assert.True(ScanOptionTranslation.IsDefined(filter));
		}

		Assert.True(ScanOptionTranslation.IsDefined(default(ScanProtectionFilter)));
		Assert.True(ScanOptionTranslation.IsDefined(default(ScanAlignment)));
		Assert.True(ScanOptionTranslation.IsDefined(ScanAlignment.AlignedTo(4)));
		Assert.True(ScanOptionTranslation.IsDefined(ScanAlignment.LastDigits("f0")));
	}

	[Fact]
	public void OnlyTheAobOptionsOmitTheParameterWithoutAlignment()
	{
		Assert.Null(ScanOptionTranslation.ToFastScan(ScanAlignment.None, null).Parameter);
		Assert.Equal(string.Empty, ScanOptionTranslation.ToFastScan(ScanAlignment.None, string.Empty).Parameter);
		Assert.Equal("F0", ScanOptionTranslation.ToFastScan(ScanAlignment.LastDigits("f0"), null).Parameter);
		Assert.Equal("16", ScanOptionTranslation.ToFastScan(ScanAlignment.AlignedTo(16), string.Empty).Parameter);
	}
}
