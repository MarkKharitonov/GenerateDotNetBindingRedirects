using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace Dayforce.CSharp.ProjectAssets
{
    public class ProjectAssets
    {
        public readonly IReadOnlyDictionary<string, LibraryItem> Libraries;
        public readonly List<string> PackageFolders;
        public readonly NuGetFramework TargetFramework;

        public ProjectAssets(SolutionsContext sc)
        {
            ProjectContext firstProject = null;
            SortedDictionary<string, LibraryItem> libs = null;
            Dictionary<(string, NuGetVersion), LibraryItem> discarded = null;
            var specialVersions = new HashSet<string>(C.IgnoreCase);
            foreach (var project in sc.YieldProjects())
            {
                var projectAssetsJsonFilePath = $"{project.ProjectFilePath}\\..\\obj\\project.assets.json";
                if (!File.Exists(projectAssetsJsonFilePath))
                {
                    continue;
                }
                Log.Instance.Save(projectAssetsJsonFilePath);

                if (libs == null)
                {
                    libs = new SortedDictionary<string, LibraryItem>(C.IgnoreCase);
                }

                if (libs.ContainsKey(project.AssemblyName))
                {
                    continue;
                }

                var (projectAssets, versionRanges) = ProcessProjectFile(sc, project, projectAssetsJsonFilePath, libs, ref PackageFolders, ref discarded);

                if (TargetFramework == null)
                {
                    TargetFramework = projectAssets.Targets[0].TargetFramework;
                    firstProject = project;
                }

                libs[project.AssemblyName] = GetProjectLib(firstProject, project.AssemblyName, projectAssets.Targets[0].Libraries, versionRanges);

                specialVersions.UnionWith(projectAssets.ProjectFileDependencyGroups[0].Dependencies.Where(o => o.Contains("*")));
            }

            Log.Instance.WriteVerbose("ProjectAssets({0}) : {1} libraries", firstProject, libs?.Count);
            if (libs == null)
            {
                return;
            }

            Libraries = libs;
            Libraries.Values.ForEach(o => o.CompleteConstruction(PackageFolders, TargetFramework, sc, specialVersions, Libraries, discarded));
        }

        private LibraryItem GetProjectLib(ProjectContext firstProject, string asmName,
            ICollection<LockFileTargetLibrary> projectDependencies, IDictionary<string, VersionRange> versionRanges)
        {
            Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2}", firstProject, asmName, C.V1.Value);
            return LibraryItem.Create(new LockFileTargetLibrary
            {
                Name = asmName,
                Version = C.V1.Value,
                Type = C.PROJECT,
                Framework = TargetFramework.DotNetFrameworkName,
                RuntimeAssemblies = new[] { new LockFileItem($"bin/placeholder/{asmName}.dll") },
                Dependencies = projectDependencies.Select(lib => new PackageDependency(lib.Name, GetVersionRange(versionRanges, lib))).ToList()
            }, C.V1.Range, PackageFolders);
        }

        private static VersionRange GetVersionRange(IDictionary<string, VersionRange> versionRanges, LockFileTargetLibrary lib) =>
            versionRanges.TryGetValue(lib.Name, out var versionRange) ? versionRange : new VersionRange(lib.Version);

        private static (LockFile projectAssets, IDictionary<string, VersionRange> versionRanges) ProcessProjectFile(SolutionsContext sc, ProjectContext project, string projectAssetsJsonFilePath,
            IDictionary<string, LibraryItem> libs, ref List<string> packageFolders,
            ref Dictionary<(string, NuGetVersion), LibraryItem> discarded)
        {
            try
            {
                var projectAssets = new LockFileFormat().Read(projectAssetsJsonFilePath);
                sc.NormalizeProjectAssets(project, projectAssets.Targets[0].Libraries);

                var resolved = projectAssets.Targets[0].Libraries
                    .SelectMany(lib => lib.Dependencies)
                    .Concat(projectAssets.Targets[0].Libraries.Select(o => new PackageDependency(o.Name, new VersionRange(o.Version))))
                    .GroupBy(o => o.Id, C.IgnoreCase)
                    .ToDictionary(g => g.Key, g => VersionRange.CommonSubSet(g.Select(o => o.VersionRange)), C.IgnoreCase);

                if (packageFolders == null)
                {
                    packageFolders = projectAssets.PackageFolders.Select(o => o.Path + (o.Path[^1] == '\\' ? "" : "\\")).ToList();
                    if (packageFolders.Count > 1)
                    {
                        Log.Instance.WriteVerbose("ProjectAssets({0}) : using {1} package folders", project, packageFolders.Count);
                        for (int i = 0; i < packageFolders.Count; ++i)
                        {
                            Log.Instance.WriteVerbose("ProjectAssets({0}) :   package folder #{1} - {2}", project, i + 1, packageFolders[i]);
                        }
                    }
                }

                foreach (var lib in projectAssets.Targets[0].Libraries)
                {
                    if (!libs.TryGetValue(lib.Name, out var prev))
                    {
                        Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2}", project, lib.Name, lib.Version);
                        libs[lib.Name] = LibraryItem.Create(lib, resolved[lib.Name], packageFolders);
                    }
                    else if (prev.Version < lib.Version)
                    {
                        Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2} (prev {3})", project, lib.Name, lib.Version, prev.Version);
                        SaveDiscarded(ref discarded, prev);
                        libs[lib.Name] = LibraryItem.Create(lib, resolved[lib.Name], packageFolders);
                    }
                    else if (prev.Version > lib.Version)
                    {
                        Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2} (discard {3})", project, lib.Name, prev.Version, lib.Version);
                        SaveDiscarded(ref discarded, LibraryItem.Create(lib, resolved[lib.Name], packageFolders));
                    }
                    else
                    {
                        Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2} (same)", project, lib.Name, lib.Version);
                    }
                }

                return (projectAssets, resolved);
            }
            catch (Exception exc) when (!(exc is ApplicationException))
            {
                throw new ApplicationException("Failed to process " + projectAssetsJsonFilePath, exc);
            }
        }

        private static void SaveDiscarded(ref Dictionary<(string, NuGetVersion), LibraryItem> discarded, LibraryItem lib)
        {
            if (discarded == null)
            {
                discarded = new Dictionary<(string, NuGetVersion), LibraryItem>();
            }
            discarded[(lib.Name, lib.Version)] = lib;
        }
    }
}
