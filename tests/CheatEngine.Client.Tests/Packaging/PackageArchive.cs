using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace CheatEngine.Client.Tests.Packaging;

/// <summary>One dependency declared by a nuspec.</summary>
internal sealed record PackageDependency(string Id, string Version, string? Exclude);

/// <summary>A .nupkg or .snupkg read once into memory: file hash, nuspec metadata and every entry.</summary>
internal sealed class PackageArchive
{
	private readonly Dictionary<string, byte[]> _entries;

	private PackageArchive(string path, Dictionary<string, byte[]> entries, XDocument nuspec)
	{
		Path = path;
		FileName = System.IO.Path.GetFileName(path);
		IsSymbolPackage = FileName.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase);
		_entries = entries;
		Nuspec = nuspec;
		XNamespace ns = nuspec.Root!.Name.Namespace;
		Metadata = nuspec.Root.Element(ns + "metadata")
				   ?? throw new InvalidOperationException($"Package '{path}' does not declare metadata.");
		Id = Metadata.Element(ns + "id")?.Value
			 ?? throw new InvalidOperationException($"Package '{path}' does not declare an id.");
		Version = Metadata.Element(ns + "version")?.Value
				  ?? throw new InvalidOperationException($"Package '{path}' does not declare a version.");

		List<PackageDependency> dependencies = [];
		foreach (XElement dependency in Metadata.Descendants(ns + "dependency"))
		{
			dependencies.Add(new PackageDependency((string) dependency.Attribute("id")!, (string) dependency.Attribute("version")!,
				(string?) dependency.Attribute("exclude")));
		}

		Dependencies = dependencies;
		Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
	}

	/// <summary>The absolute path of the archive.</summary>
	internal string Path
	{
		get;
	}

	/// <summary>The archive file name.</summary>
	internal string FileName
	{
		get;
	}

	/// <summary>Whether this is a symbol package.</summary>
	internal bool IsSymbolPackage
	{
		get;
	}

	/// <summary>The nuspec id.</summary>
	internal string Id
	{
		get;
	}

	/// <summary>The nuspec version.</summary>
	internal string Version
	{
		get;
	}

	/// <summary>The lowercase SHA-256 of the archive file.</summary>
	internal string Sha256
	{
		get;
	}

	/// <summary>The nuspec document.</summary>
	internal XDocument Nuspec
	{
		get;
	}

	/// <summary>The nuspec <c>metadata</c> element.</summary>
	internal XElement Metadata
	{
		get;
	}

	/// <summary>Every dependency of every group.</summary>
	internal IReadOnlyList<PackageDependency> Dependencies
	{
		get;
	}

	/// <summary>The entry names, with forward slashes.</summary>
	internal IEnumerable<string> EntryNames => _entries.Keys;

	/// <summary>Reads every <c>.nupkg</c> and <c>.snupkg</c> of a directory.</summary>
	internal static IReadOnlyList<PackageArchive> ReadDirectory(string directory)
	{
		List<PackageArchive> archives = [];
		foreach (string file in Directory.EnumerateFiles(directory))
		{
			if (file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
			{
				archives.Add(Read(file));
			}
		}

		archives.Sort(static (left, right) => string.CompareOrdinal(left.FileName, right.FileName));
		return archives;
	}

	/// <summary>Reads one archive.</summary>
	internal static PackageArchive Read(string path)
	{
		Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);
		using (ZipArchive archive = ZipFile.OpenRead(path))
		{
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (entry.FullName.EndsWith('/'))
				{
					continue;
				}

				using Stream stream = entry.Open();
				using MemoryStream copy = new();
				stream.CopyTo(copy);
				entries[entry.FullName.Replace('\\', '/')] = copy.ToArray();
			}
		}

		KeyValuePair<string, byte[]> nuspec = Assert.Single(entries,
			static entry => !entry.Key.Contains('/', StringComparison.Ordinal) && entry.Key.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
		using MemoryStream nuspecStream = new(nuspec.Value);
		return new PackageArchive(path, entries, XDocument.Load(nuspecStream));
	}

	/// <summary>Whether the archive has this entry.</summary>
	internal bool Contains(string entryName)
	{
		return _entries.ContainsKey(entryName);
	}

	/// <summary>The bytes of an entry; fails the test when it is missing.</summary>
	internal byte[] Entry(string entryName)
	{
		Assert.True(_entries.TryGetValue(entryName, out byte[]? bytes), $"{FileName} has no entry '{entryName}'.");
		return bytes!;
	}

	/// <summary>The text of an entry (UTF-8).</summary>
	internal string EntryText(string entryName)
	{
		using StreamReader reader = new(new MemoryStream(Entry(entryName)), detectEncodingFromByteOrderMarks: true);
		return reader.ReadToEnd();
	}

	/// <summary>The value of a nuspec metadata element, or <see langword="null"/>.</summary>
	internal string? MetadataValue(string name)
	{
		return Metadata.Element(Metadata.Name.Namespace + name)?.Value;
	}

	/// <summary>A nuspec metadata element, or <see langword="null"/>.</summary>
	internal XElement? MetadataElement(string name)
	{
		return Metadata.Element(Metadata.Name.Namespace + name);
	}
}
