using System.Collections.Generic;
using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace Dayforce.CSharp.ProjectAssets
{
    public class ProjectItem : LibraryItem
    {
        [JsonIgnore]
        public override bool HasRuntimeAssemblies => true;

        public ProjectItem(LockFileTargetLibrary library) : base(library)
        {
        }

        public override void CompleteConstruction(List<string> packageFolders, NuGetFramework framework, SolutionsContext sc,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all,
            Dictionary<(string, NuGetVersion), LibraryItem> discarded)
        {
            SetNuGetDependencies(packageFolders, framework, specialVersions, all, discarded,
                dep => !(dep.VersionRange.Equals(C.V1.Range) && sc.ProjectsByAssemblyName.ContainsKey(dep.Id)));
        }

        public override string ToString() => $"{Name} (nd = {NuGetDependencies.Count})";
    }
}