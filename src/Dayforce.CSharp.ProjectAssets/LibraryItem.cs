﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace Dayforce.CSharp.ProjectAssets
{
    public abstract class LibraryItem
    {
        private static readonly IReadOnlyList<RuntimeAssembly> s_unresolvedRuntimeAssemblies = new[] { RuntimeAssembly.Unresolved };
        public static readonly NuGetDependency UnresolvedNuGetDependency = new NuGetDependency(new PackageDependency(RuntimeAssembly.Unresolved.AssemblyName), s_unresolvedRuntimeAssemblies);

        [JsonIgnore]
        public readonly LockFileTargetLibrary Library;
        [JsonIgnore]
        public string Name => Library.Name;
        public string Type => Library.Type;
        [JsonIgnore]
        public NuGetVersion Version => Library.Version;
        [JsonIgnore]
        public virtual VersionRange VersionRange => throw new NotSupportedException();
        public IReadOnlyList<NuGetDependency> NuGetDependencies { get; private set; }

        public bool ShouldSerializeNuGetDependencies() => NuGetDependencies.Count > 0;

        [JsonIgnore]
        public abstract bool HasRuntimeAssemblies { get; }

        [JsonIgnore]
        public abstract bool HasRuntimeTargets { get; }

        public static LibraryItem Create(LockFileTargetLibrary library, VersionRange versionRange, List<string> packageFolders) =>
            library.Type == C.PACKAGE ? new PackageItem(library, versionRange, packageFolders) : (LibraryItem)new ProjectItem(library);

        protected LibraryItem(LockFileTargetLibrary library)
        {
            Library = library;
        }

        public abstract void CompleteConstruction(List<string> packageFolders, NuGetFramework framework, SolutionsContext sc,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all,
            Dictionary<(string, NuGetVersion), LibraryItem> discarded);

        protected void SetNuGetDependencies(List<string> packageFolders, NuGetFramework framework, HashSet<string> specialVersions,
            IReadOnlyDictionary<string, LibraryItem> all,
            Dictionary<(string, NuGetVersion), LibraryItem> discarded,
            Func<PackageDependency, bool> predicate = null)
        {
            IEnumerable<PackageDependency> deps = Library.Dependencies;
            if (predicate != null)
            {
                deps = deps.Where(predicate);
            }
            NuGetDependencies = deps
                .Select(dep => CreateNuGetDependency(dep, packageFolders, framework, specialVersions, all, discarded))
                .Where(o => o != null)
                .OrderBy(o => o.Id)
                .ToList();
        }

        protected NuGetDependency CreateNuGetDependency(PackageDependency dep, List<string> packageFolders, NuGetFramework framework,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all,
            Dictionary<(string, NuGetVersion), LibraryItem> discarded)
        {
            var (lib, depVersion) = GetLibraryMatchingDependency(dep, all, discarded);

            var packageDir = packageFolders.Select(packageFolder => $"{packageFolder}{dep.Id}\\{depVersion}").FirstOrDefault(Directory.Exists);
            if (packageDir == null)
            {
                var specialVersion = specialVersions.FirstOrDefault(str => str.StartsWith(dep.Id, C.IGNORE_CASE) && str[dep.Id.Length] == ' ');
                if (specialVersion == null)
                {
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : unresolved dependency {1} - not found", Name, dep);
                    return new NuGetDependency(dep, s_unresolvedRuntimeAssemblies);
                }

                lib = all[dep.Id];
                depVersion = lib.Version;
                var packageDirs = packageFolders.Select(packageFolder => $"{packageFolder}{dep.Id}/{depVersion}").ToList();
                packageDir = packageDirs.FirstOrDefault(Directory.Exists);
                if (packageDir == null)
                {
                    throw new ApplicationException($"Failed to resolve {specialVersion} - none of \"{string.Join("\" \"", packageDirs)} exists");
                }
            }

            if (lib != null)
            {
                if (lib.HasRuntimeAssemblies)
                {
                    var runtimeAssemblies = ((PackageItem)lib).RuntimeAssemblies;
                    var path = Path.GetDirectoryName(runtimeAssemblies[0].FilePath);
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : take dependency {1} - {2}", Name, dep, path);
                    return NuGetDependency.Create(this, dep, runtimeAssemblies);
                }
                else
                {
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : skip dependency {1} - no runtime assemblies", Name, dep);
                    return default;
                }
            }
            {
                var baseLibFolderPath = $"{packageDir}\\lib";
                if (!Directory.Exists(baseLibFolderPath))
                {
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : skip dependency {1} - no runtime assemblies", Name, dep);
                    return default;
                }

                var packageFrameworks = Directory
                    .EnumerateDirectories(baseLibFolderPath)
                    .Select(libFolderPath => new FrameworkFromLibFolderPath(libFolderPath))
                    .ToList();
                var path = packageFrameworks.Count > 0 ? packageFrameworks.GetNearest(framework)?.LibFolderPath : baseLibFolderPath;
                if (path == null)
                {
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : skip dependency {1} - incompatible with \"{2}\" (\"{3}\")", Name, dep, 
                        framework, string.Join("\" \"", packageFrameworks));
                    return default;
                }
                else
                {
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : take dependency {1} - {2}", Name, dep, path);
                }
                var packageFolder = packageFolders.First(packageDir.StartsWith);
                return NuGetDependency.Create(this, dep, packageFolder, path);
            }
        }

        private (LibraryItem, NuGetVersion) GetLibraryMatchingDependency(PackageDependency dep, IReadOnlyDictionary<string, LibraryItem> all, Dictionary<(string, NuGetVersion), LibraryItem> discarded)
        {
            if (discarded != null && discarded.TryGetValue((dep.Id, dep.VersionRange.MinVersion), out var lib))
            {
                return (lib, lib.Version);
            }

            if (!all.TryGetValue(dep.Id, out lib))
            {
                throw new ApplicationException($"Failed to map {dep} to one of the NuGet packages on which {Name} depends.");
            }

            if (lib.Version == dep.VersionRange.MinVersion || dep.VersionRange.MinVersion == null)
            {
                return (lib, lib.Version);
            }

            return (null, dep.VersionRange.MinVersion);
        }
    }
}