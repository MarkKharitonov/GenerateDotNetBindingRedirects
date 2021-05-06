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

        public PackageItem(LockFileTargetLibrary library, VersionRange versionRange, string packageFolder) : base(library)
        {
            VersionRange = versionRange;

            var baseDir = $"{packageFolder}{Name}/{Version}/";
            RuntimeAssemblies = Library.RuntimeAssemblies
                .Where(o => o.Path.IsExecutable())
                .Select(o =>
                {
                    if (o.Properties?.Count > 0)
                    {
                        Log.Instance.WriteVerbose("PackageItem({0}) : runtime assembly {1} has {2} properties.", Name, o.Path, o.Properties.Count);
                    }
                    var filePath = $"{baseDir}{o.Path}";
                    if (!File.Exists(filePath))
                    {
                        throw new ApplicationException($"Not found: {filePath}");
                    }
                    return new RuntimeAssembly(packageFolder, filePath);
                })
                .OrderBy(o => o.RelativeFilePath)
                .ToList();
        }

        public override void CompleteConstruction(string packageFolder, NuGetFramework framework, SolutionsContext sc,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all)
        {
            SetNuGetDependencies(packageFolder, framework, specialVersions, all);
        }

        public override string ToString() => $"{Name}/{VersionRange} (r = {RuntimeAssemblies.Count} , nd = {NuGetDependencies.Count})";

        public override bool Equals(object obj) => Equals(obj as PackageItem);

        public bool Equals(PackageItem other) => other != null &&
            Name.Equals(other.Name, C.IGNORE_CASE) &&
            VersionRange.Equals(other.VersionRange);

        public override int GetHashCode() => HashCode.Combine(Name, VersionRange);
    }
}