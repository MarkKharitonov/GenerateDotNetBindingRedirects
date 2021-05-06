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

        public static LibraryItem Create(LockFileTargetLibrary library, VersionRange versionRange, string packageFolder) =>
            library.Type == C.PACKAGE ? new PackageItem(library, versionRange, packageFolder) : (LibraryItem)new ProjectItem(library);

        protected LibraryItem(LockFileTargetLibrary library)
        {
            Library = library;
        }

        public abstract void CompleteConstruction(string packageFolder, NuGetFramework framework, SolutionsContext sc,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all);

        protected void SetNuGetDependencies(string packageFolder, NuGetFramework framework, HashSet<string> specialVersions,
            IReadOnlyDictionary<string, LibraryItem> all, Func<PackageDependency, bool> predicate = null)
        {
            IEnumerable<PackageDependency> deps = Library.Dependencies;
            if (predicate != null)
            {
                deps = deps.Where(predicate);
            }
            NuGetDependencies = deps
                .Select(dep => CreateNuGetDependency(dep, packageFolder, framework, specialVersions, all))
                .Where(o => o != null)
                .OrderBy(o => o.Id)
                .ToList();
        }

        protected NuGetDependency CreateNuGetDependency(PackageDependency dep, string packageFolder, NuGetFramework framework,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all)
        {
            if (!all.TryGetValue(dep.Id, out var lib))
            {
                throw new ApplicationException($"Failed to map {dep} to one of the NuGet packages on which {Name} depends.");
            }

            var depVersion = dep.VersionRange.MinVersion;
            if (depVersion == null)
            {
                depVersion = lib.Version;
            }

            var packageDir = $"{packageFolder}{dep.Id}/{depVersion}";
            if (!Directory.Exists(packageDir))
            {
                var specialVersion = specialVersions.FirstOrDefault(str => str.StartsWith(dep.Id, C.IGNORE_CASE) && str[dep.Id.Length] == ' ');
                if (specialVersion == null)
                {
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : unresolved dependency {1} - not found", Name, dep);
                    return new NuGetDependency(dep, s_unresolvedRuntimeAssemblies);
                }

                packageDir = $"{packageFolder}{dep.Id}/{lib.VersionRange.MinVersion}";
                if (!Directory.Exists(packageDir))
                {
                    throw new ApplicationException($"Failed to resolve {specialVersion} - {packageDir} does not exist");
                }
            }

            if (depVersion == lib.Version)
            {
                if (lib.HasRuntimeAssemblies)
                {
                    var path = Path.GetDirectoryName(((PackageItem)lib).RuntimeAssemblies[0].FilePath);
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : take dependency {1} - {2}", Name, dep, path);
                    return NuGetDependency.Create(this, dep, packageFolder, path);
                }
                else
                {
                    Log.Instance.WriteVerbose("CompleteConstruction({0}) : skip dependency {1} - no runtime assemblies", Name, dep);
                    return default;
                }
            }
            {
                var baseLibFolderPath = $"{packageDir}/lib";
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
                    throw new ApplicationException($"{dep} cannot be a NuGet dependency of {Name}, because none of its target frameworks is compatible with {framework} (\"{string.Join("\" \"", packageFrameworks)}\").");
                }
                Log.Instance.WriteVerbose("CompleteConstruction({0}) : take dependency {1} - {2}", Name, dep, path);
                return NuGetDependency.Create(this, dep, packageFolder, path);
            }
        }
    }
}