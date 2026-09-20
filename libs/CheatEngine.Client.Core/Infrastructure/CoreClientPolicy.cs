namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Immutable activation policy supplied by the DI integration.</summary>
internal sealed class CoreClientPolicy
{
	internal CoreClientPolicy(IEnumerable<string> allowedTableRoots, bool enableUnsafeLuaExecution)
	{
		ArgumentNullException.ThrowIfNull(allowedTableRoots);
		List<string> roots = [];
		foreach (string root in allowedTableRoots)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(root);
			roots.Add(NormalizeDirectory(root));
		}

		AllowedTableRoots = roots.ToArray();
		EnableUnsafeLuaExecution = enableUnsafeLuaExecution;
	}

	internal static CoreClientPolicy SafeDefaults
	{
		get;
	} = new([], false);

	internal IReadOnlyList<string> AllowedTableRoots
	{
		get;
	}

	internal bool EnableUnsafeLuaExecution
	{
		get;
	}

	/// <summary>
	///     Validates a table path against the activation's explicit roots without following any observed link or junction.
	/// </summary>
	/// <remarks>
	///     Cheat Engine's native table functions accept only a path, not a managed file handle. A full handle-based
	///     TOCTOU-proof authorization is therefore impossible at this boundary. This policy deliberately rejects every
	///     path with an observed reparse point instead of resolving and trusting a link target; callers must keep the
	///     approved root tree non-reparse-pointed until the native load or save completes.
	/// </remarks>
	internal bool TryAuthorizeTableFile(string path, bool forLoad, out string reason)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			reason = "The table path must be non-empty and fully qualified.";
			return false;
		}

		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(path);
		}
		catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
		{
			reason = "The table path cannot be normalized safely.";
			return false;
		}

		foreach (string root in AllowedTableRoots)
		{
			if (!IsContainedBy(root, fullPath))
			{
				continue;
			}

			if (!TryVerifySafePath(root, fullPath, forLoad, out reason))
			{
				return false;
			}

			reason = string.Empty;
			return true;
		}

		reason = "The normalized table path is outside every configured allowed root.";
		return false;
	}

	private static string NormalizeDirectory(string path)
	{
		string fullPath = Path.GetFullPath(path);
		return Path.EndsInDirectorySeparator(fullPath) ? fullPath : fullPath + Path.DirectorySeparatorChar;
	}

	private static bool IsContainedBy(string normalizedRoot, string fullPath)
	{
		string root = Path.TrimEndingDirectorySeparator(normalizedRoot);
		string relative = Path.GetRelativePath(root, fullPath);
		return !Path.IsPathFullyQualified(relative) &&
		       !string.Equals(relative, "..", StringComparison.Ordinal) &&
		       !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
		       !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
	}

	private static bool TryVerifySafePath(string normalizedRoot, string fullPath, bool forLoad, out string reason)
	{
		string root = Path.TrimEndingDirectorySeparator(normalizedRoot);
		if (!Directory.Exists(root))
		{
			reason = "The configured table root does not currently exist and cannot be verified.";
			return false;
		}

		if (!TryVerifyDirectoryChain(root, out reason))
		{
			return false;
		}

		if (forLoad)
		{
			if (!File.Exists(fullPath))
			{
				reason = "A trusted table import source must be an existing regular file.";
				return false;
			}

			if (!TryVerifyDirectoryChain(Path.GetDirectoryName(fullPath)!, out reason))
			{
				return false;
			}

			return TryVerifyRegularFile(fullPath, out reason);
		}

		string? parent = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
		{
			reason = "A trusted table export destination must have an existing parent directory.";
			return false;
		}

		if (!TryVerifyDirectoryChain(parent, out reason))
		{
			return false;
		}

		return !File.Exists(fullPath) || TryVerifyRegularFile(fullPath, out reason);
	}

	private static bool TryVerifyDirectoryChain(string directoryPath, out string reason)
	{
		string fullPath = Path.GetFullPath(directoryPath);
		string? root = Path.GetPathRoot(fullPath);
		if (string.IsNullOrEmpty(root))
		{
			reason = "The table path does not have a verifiable filesystem root.";
			return false;
		}

		string current = Path.TrimEndingDirectorySeparator(root);
		if (current.Length == 2 && current[1] == ':')
		{
			current += Path.DirectorySeparatorChar;
		}

		if (!TryVerifyNotReparsePoint(new DirectoryInfo(current), out reason))
		{
			return false;
		}

		string relative = Path.GetRelativePath(current, fullPath);
		if (relative == ".")
		{
			return true;
		}

		foreach (string segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
			         StringSplitOptions.RemoveEmptyEntries))
		{
			current = Path.Combine(current, segment);
			if (!TryVerifyNotReparsePoint(new DirectoryInfo(current), out reason))
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryVerifyRegularFile(string fullPath, out string reason)
	{
		return TryVerifyNotReparsePoint(new FileInfo(fullPath), out reason);
	}

	private static bool TryVerifyNotReparsePoint(FileSystemInfo item, out string reason)
	{
		try
		{
			item.Refresh();
			if ((item.Attributes & FileAttributes.ReparsePoint) != 0 || item.LinkTarget is not null)
			{
				reason = "The trusted table path contains a symbolic link, junction, or another reparse point.";
				return false;
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
			                                  or NotSupportedException)
		{
			reason = "The trusted table path cannot be verified without following a filesystem link.";
			return false;
		}

		reason = string.Empty;
		return true;
	}
}
