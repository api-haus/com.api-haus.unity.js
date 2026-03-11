using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityJS.Runtime.Tests")]

namespace UnityJS.Runtime
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using UnityEngine;

	/// <summary>
	/// Static registry for JS script search paths.
	/// Allows registering multiple directories to search for scripts.
	/// </summary>
	public static class JsScriptSearchPaths
	{
		static readonly List<string> s_searchPaths = new();
		static readonly object s_searchPathLock = new object();
		static bool s_initialized;
		static string s_defaultScriptsPath;

		public static string DefaultScriptsPath
		{
			get
			{
				if (s_defaultScriptsPath == null)
				{
					s_defaultScriptsPath = Path.Combine(Application.streamingAssetsPath, "js");
				}
				return s_defaultScriptsPath;
			}
		}

		public static void Initialize()
		{
			lock (s_searchPathLock)
			{
				if (s_initialized)
					return;
				s_searchPaths.Clear();
				s_searchPaths.Add(DefaultScriptsPath);
				s_initialized = true;
			}
		}

		public static void AddSearchPath(string absolutePath, int priority = 0)
		{
			lock (s_searchPathLock)
			{
				if (!s_initialized)
					Initialize();
				if (string.IsNullOrEmpty(absolutePath))
					return;

				s_searchPaths.Remove(absolutePath);

				var index = Math.Min(priority, Math.Max(0, s_searchPaths.Count - 1));
				s_searchPaths.Insert(index, absolutePath);
			}
		}

		public static void RemoveSearchPath(string absolutePath)
		{
			lock (s_searchPathLock)
			{
				if (absolutePath != DefaultScriptsPath)
					s_searchPaths.Remove(absolutePath);
			}
		}

		public static void ClearSearchPaths()
		{
			lock (s_searchPathLock)
			{
				s_searchPaths.Clear();
				s_searchPaths.Add(DefaultScriptsPath);
			}
		}

		public static IReadOnlyList<string> GetSearchPaths()
		{
			lock (s_searchPathLock)
			{
				if (!s_initialized)
					Initialize();
				return s_searchPaths.ToArray();
			}
		}

		public static bool TryFindScript(
			string relativePath,
			out string foundPath,
			out string searchedBasePath
		)
		{
			foundPath = string.Empty;
			searchedBasePath = string.Empty;

			if (string.IsNullOrEmpty(relativePath))
				return false;

			lock (s_searchPathLock)
			{
				if (!s_initialized)
					Initialize();

				foreach (var basePath in s_searchPaths)
				{
					var fullPath = Path.Combine(basePath, relativePath);
					if (File.Exists(fullPath))
					{
						foundPath = fullPath;
						searchedBasePath = basePath;
						return true;
					}
				}
			}
			return false;
		}

		public static bool ScriptExists(string relativePath)
		{
			return TryFindScript(relativePath, out _, out _);
		}

		public static string GetScriptPath(string relativePath)
		{
			if (TryFindScript(relativePath, out var foundPath, out _))
				return foundPath;

			return Path.Combine(DefaultScriptsPath, relativePath);
		}

		/// <summary>
		/// Reset state for testing. Not thread-safe — call only in test SetUp.
		/// </summary>
		internal static void Reset()
		{
			lock (s_searchPathLock)
			{
				s_searchPaths.Clear();
				s_initialized = false;
				s_defaultScriptsPath = null;
			}
		}
	}
}
