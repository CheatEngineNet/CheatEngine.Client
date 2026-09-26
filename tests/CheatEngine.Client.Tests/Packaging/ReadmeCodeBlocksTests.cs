namespace CheatEngine.Client.Tests.Packaging;

/// <summary>
///     The code block rules that <see cref="ReadmeSnippetCompilationTests" /> applies to the packed READMEs, proven on
///     fixed Markdown so that both CI legs check them without packages.
/// </summary>
public sealed class ReadmeCodeBlocksTests
{
	[Fact]
	public void ACSharpBlockIsCompiledAndOtherLanguagesAreIgnored()
	{
		const string markdown = """
			# Title

			```powershell
			dotnet build
			```

			```csharp
			namespace Sample;
			```

			```xml
			<Project />
			```
			""";

		ReadmeCodeBlockSet parsed = ReadmeCodeBlocks.Parse(markdown);

		Assert.Empty(parsed.Problems);
		ReadmeCodeBlock block = Assert.Single(parsed.Blocks);
		Assert.Equal(7, block.Line);
		Assert.False(block.NoCompile);
		Assert.Equal("namespace Sample;", block.Code);
	}

	[Fact]
	public void ANoCompileBlockNeedsItsReasonRightAboveIt()
	{
		const string markdown = """
			<!-- nocompile: the fragment continues the plugin above -->
			```csharp nocompile
			client.Memory.At(address).Write(1);
			```

			```csharp nocompile
			client.Memory.At(address).Write(2);
			```

			<!-- nocompile: -->
			```csharp nocompile
			client.Memory.At(address).Write(3);
			```

			<!-- nocompile: a blank line separates this reason from its block -->

			```csharp nocompile
			client.Memory.At(address).Write(4);
			```
			""";

		ReadmeCodeBlockSet parsed = ReadmeCodeBlocks.Parse(markdown);

		Assert.Equal([true, true, true, true], parsed.Blocks.Select(static block => block.NoCompile));
		Assert.Equal(3, parsed.Problems.Count);
		Assert.StartsWith("line 6:", parsed.Problems[0], StringComparison.Ordinal);
		Assert.StartsWith("line 11:", parsed.Problems[1], StringComparison.Ordinal);
		Assert.StartsWith("line 17:", parsed.Problems[2], StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("cs")]
	[InlineData("C#")]
	[InlineData("c-sharp")]
	public void ACSharpBlockUnderAnotherLabelIsRefused(string label)
	{
		string markdown = $"```{label}\nnamespace Sample;\n```\n";

		ReadmeCodeBlockSet parsed = ReadmeCodeBlocks.Parse(markdown);

		Assert.Empty(parsed.Blocks);
		Assert.Contains($"not '{label}'", Assert.Single(parsed.Problems), StringComparison.Ordinal);
	}

	[Fact]
	public void AnUnknownOptionAndAnUnclosedBlockAreRefused()
	{
		ReadmeCodeBlockSet unknown = ReadmeCodeBlocks.Parse("```csharp skip\nnamespace Sample;\n```\n");
		ReadmeCodeBlockSet unclosed = ReadmeCodeBlocks.Parse("Text\n```csharp\nnamespace Sample;\n");

		Assert.Contains("unknown code block option 'skip'", Assert.Single(unknown.Problems), StringComparison.Ordinal);
		Assert.Empty(unclosed.Blocks);
		Assert.Equal("line 2: the code block opened here is never closed.", Assert.Single(unclosed.Problems));
	}

	[Fact]
	public void AFenceClosesOnlyWithTheSameCharacterAndAtLeastItsLength()
	{
		const string markdown = """
			````markdown
			```csharp
			not a block of its own
			```
			````

			~~~csharp
			namespace Sample;
			```
			~~~~
			""";

		ReadmeCodeBlockSet parsed = ReadmeCodeBlocks.Parse(markdown);

		Assert.Empty(parsed.Problems);
		ReadmeCodeBlock block = Assert.Single(parsed.Blocks);
		Assert.Equal(7, block.Line);
		Assert.Equal("namespace Sample;\n```", block.Code);
	}
}
