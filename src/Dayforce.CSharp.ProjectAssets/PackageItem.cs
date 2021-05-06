using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace Dayforce.CSharp.ProjectAssets
{
    public class PackageItem : LibraryItem, IEquatable<PackageItem>
    {
        public override VersionRange VersionRange { get; }
        public IReadOnlyList<RuntimeAssembly> RuntimeAssemblies { get; private set; }

        [JsonIgnore]
        public override bool HasRuntimeAssemblies => RuntimeAssemblies.Count > 0;

        public bool ShouldSerializeRuntimeAssemblies() => RuntimeAssemblies.Count > 0;

        public PackageItem(LockFileTargetLibrary library, VersionRange versionRange, List<string> packageFolders) : base(library)
        {
            VersionRange = versionRange;

            var baseDirs = packageFolders.Select(packageFolder => $"{packageFolder}{Name}\\{Version}\\").ToList();
            RuntimeAssemblies = Library.RuntimeAssemblies
                .Where(o => o.Path.IsExecutable())
                .Select(o =>
                {
                    if (o.Properties?.Count > 0)
                    {
                        Log.Instance.WriteVerbose("PackageItem({0}) : runtime assembly {1} has {2} properties.", Name, o.Path, o.Properties.Count);
                    }
                    var filePath = baseDirs.Select(baseDir => baseDir + o.Path).FirstOrDefault(File.Exists);
                    if (filePath == null)
                    {
                        throw new ApplicationException($"{o.Path} not found under any of \"{string.Join("\" \"", baseDirs)}\"");
                    }
                    var packageFolder = packageFolders.First(filePath.StartsWith);
                    return new RuntimeAssembly(packageFolder, filePath);
                })
                .OrderBy(o => o.RelativeFilePath)
                .ToList();
        }

        public override void CompleteConstruction(List<string> packageFolders, NuGetFramework framework, SolutionsContext sc,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all,
            Dictionary<(string, NuGetVersion), LibraryItem> discarded)
        {
            SetNuGetDependencies(packageFolders, framework, specialVersions, all, discarded);
        }

        public override string ToString() => $"{Name}/{VersionRange} (r = {RuntimeAssemblies.Count} , nd = {NuGetDependencies.Count})";

        public override bool Equals(object obj) => Equals(obj as PackageItem);

        public bool Equals(PackageItem other) => other != null &&
            Name.Equals(other.Name, C.IGNORE_CASE) &&
            VersionRange.Equals(other.VersionRange);

        public override int GetHashCode() => HashCode.Combine(Name, VersionRange);
    }
}