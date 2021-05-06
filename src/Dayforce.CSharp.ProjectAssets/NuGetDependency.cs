using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Dayforce.CSharp.ProjectAssets
{
    public class NuGetDependency : IEquatable<NuGetDependency>
    {
        [JsonIgnore]
        private readonly PackageDependency m_prototype;
        public readonly IReadOnlyList<RuntimeAssembly> RuntimeAssemblyItems;

        public static NuGetDependency Create(LibraryItem owner, PackageDependency prototype, IReadOnlyList<RuntimeAssembly> runtimeAssemblyItems)
        {
            var res = new NuGetDependency(prototype, runtimeAssemblyItems);
            Log.Instance.WriteVerbose("CompleteConstruction({0}) : take dependency {1}", owner.Name, res);
            return res;
        }

        public static NuGetDependency Create(LibraryItem owner, PackageDependency prototype, string packageFolder, string path)
        {
            var runtimeAssemblyItems = Directory
                .EnumerateFiles(path, "*.dll")
                .Concat(Directory.EnumerateFiles(path, "*.exe"))
                .Select(dll => new RuntimeAssembly(packageFolder, dll))
                .OrderBy(o => o.RelativeFilePath)
                .ToList();

            if (runtimeAssemblyItems.Count == 0)
            {
                Log.Instance.WriteVerbose("CompleteConstruction({0}) : skip dependency {1} - no runtime assemblies", owner.Name, prototype);
                return null;
            }
            return Create(owner, prototype, runtimeAssemblyItems);
        }

        public NuGetDependency(PackageDependency prototype, IReadOnlyList<RuntimeAssembly> runtimeAssemblyItems)
        {
            m_prototype = prototype;
            RuntimeAssemblyItems = runtimeAssemblyItems;
        }

        public void AssertSimple(LibraryItem owner)
        {
            if (m_prototype.Include.Count > 0 || m_prototype.Exclude.Count > 0)
            {
                throw new ApplicationException($"{owner.Name} has dependency {Id}/{VersionRange} that has Include and/or Exclude.");
            }
        }

        public string Id => m_prototype.Id;
        public VersionRange VersionRange => m_prototype.VersionRange;

        public override string ToString() => $"{m_prototype} ({string.Join(" , ", RuntimeAssemblyItems.Where(o => !o.FilePath.EndsWith(".resources.dll")))})";

        public override bool Equals(object obj) => Equals(obj as NuGetDependency);

        public bool Equals(NuGetDependency other) => other != null &&
            Id.Equals(other.Id, C.IGNORE_CASE) &&
            VersionRange.Equals(other.VersionRange);

        public override int GetHashCode() => HashCode.Combine(Id, VersionRange);
    }
}