using System.Collections.Generic;
using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.ProjectModel;

namespace Dayforce.CSharp.ProjectAssets
{
    public class ProjectItem : LibraryItem
    {
        [JsonIgnore]
        public override bool HasRuntimeAssemblies => true;

        public ProjectItem(LockFileTargetLibrary library) : base(library)
        {
        }

        public override void CompleteConstruction(string packageFolder, NuGetFramework framework, SolutionsContext sc,
            HashSet<string> specialVersions, IReadOnlyDictionary<string, LibraryItem> all)
        {
            SetNuGetDependencies(packageFolder, framework, specialVersions, all,
                dep => !(dep.VersionRange.Equals(C.V1.Range) && sc.ProjectsByAssemblyName.ContainsKey(dep.Id)));
        }

        public override string ToString() => $"{Name} (nd = {NuGetDependencies.Count})";
    }
}