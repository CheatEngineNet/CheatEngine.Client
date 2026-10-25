using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Client.Scanning;

using FsCheck;
using FsCheck.Fluent;

namespace CheatEngine.Client.Abstractions.Tests.Scanning;

/// <summary>Bounded, offline fuzz properties for the text accepted at the AOB scan boundary.</summary>
public sealed class AobPatternPropertyTests
{
	// FsCheck reports the random seed and shrunk counterexample on failure. Fixed Fact names keep arbitrary UTF-16
	// (including isolated surrogates and control characters) out of TRX test identities.
	private static readonly Config FuzzConfiguration = Config.QuickThrowOnFailure.WithMaxTest(1000).WithEndSize(128);

	private static readonly Regex Grammar = new(@"\A(?:\s*(?:[0-9a-fA-F]{2}|\?\?))+\s*\z",
		RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));

	private static readonly char[] Whitespace = Enumerable.Range(0, char.MaxValue + 1)
		.Select(static value => (char) value).Where(char.IsWhiteSpace).ToArray();

	private static readonly char[] InvalidCharacters = [',', '*', 'G', '\0', '\u200B', '\uD800', '\uDC00'];

	[Fact]
	public void ArbitraryUtf16MatchesThePatternGrammarWithoutThrowing()
	{
		Arbitrary<char[]> inputs = Arb.From(Gen.Choose(0, char.MaxValue).Select(static value => (char) value).ArrayOf(),
			static value => value.Length == 0 ? [] : new[] { value[..(value.Length / 2)], value[1..] });

		Check.One(FuzzConfiguration, Prop.ForAll(inputs, input =>
		{
			string text = new(input);
			bool accepted = AobPattern.TryParse(text, out AobPattern pattern);
			Assert.Equal(Grammar.IsMatch(text), accepted);
			if (accepted)
			{
				Assert.Equal(new AobPattern(text), pattern);
				Assert.True(AobPattern.TryParse(pattern.Value, out AobPattern reparsed));
				Assert.Equal(pattern, reparsed);
			}
			else
			{
				Assert.Equal(default, pattern);
				Assert.Throws<ArgumentException>(() => new AobPattern(text));
			}
		}));
	}

	[Fact]
	public void GeneratedBytesAndWildcardsPreservePositionsAndCanonicalText()
	{
		Arbitrary<int[]> inputs = Arb.From(Gen.Choose(0, ushort.MaxValue).ArrayOf(),
			static value => value.Length == 0 ? [] : new[] { value[..(value.Length / 2)], value[1..] });

		Check.One(FuzzConfiguration, Prop.ForAll(inputs, input =>
		{
			int[] tokens = input.Length == 0 ? [256] : input;
			StringBuilder text = new();
			string[] expected = new string[tokens.Length];
			bool wildcardOnly = true;
			for (int index = 0; index < tokens.Length; index++)
			{
				int token = tokens[index] % 257;
				expected[index] = token == 256 ? "??" : token.ToString("X2", CultureInfo.InvariantCulture);
				wildcardOnly &= token == 256;
				// Include concatenated tokens as well as every Unicode whitespace separator accepted by the contract.
				if ((tokens[index] & 1) == 0)
				{
					text.Append(Whitespace[tokens[index] % Whitespace.Length]);
				}

				text.Append((tokens[index] & 2) == 0 ? expected[index].ToLowerInvariant() : expected[index]);
			}

			text.Append(Whitespace[tokens[0] % Whitespace.Length]);
			Assert.True(AobPattern.TryParse(text.ToString(), out AobPattern pattern));
			Assert.Equal(string.Join(' ', expected), pattern.Value);
			Assert.Equal(tokens.Length, pattern.ByteLength);
			Assert.Equal(wildcardOnly, pattern.IsWildcardOnly);
			Assert.Equal(pattern.Value, pattern.ToString());
			Assert.Equal(pattern, new AobPattern(pattern.Value));
		}));
	}

	[Fact]
	public void InvalidCharactersAreRejectedAtEveryGeneratedInsertionPoint()
	{
		Check.One(FuzzConfiguration, Prop.ForAll<byte[], ushort>((bytes, offset) =>
		{
			string valid = Convert.ToHexString(bytes.Length == 0 ? [0] : bytes);
			int insertion = offset % (valid.Length + 1);
			// Punctuation, non-hex letters, embedded NUL, zero-width space, and both unpaired surrogate halves.
			foreach (char invalid in InvalidCharacters)
			{
				string text = valid.Insert(insertion, invalid.ToString());
				Assert.False(AobPattern.TryParse(text, out AobPattern pattern));
				Assert.Equal(default, pattern);
				Assert.Throws<ArgumentException>(() => new AobPattern(text));
			}
		}));
	}
}
