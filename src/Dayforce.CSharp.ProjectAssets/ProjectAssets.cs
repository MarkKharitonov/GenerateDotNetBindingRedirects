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
        public readonly string PackageFolder;
        public readonly NuGetFramework TargetFramework;

        public ProjectAssets(SolutionsContext sc)
        {
            ProjectContext firstProject = null;
            SortedDictionary<string, LibraryItem> libs = null;
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

                var (projectAssets, versionRanges) = ProcessProjectFile(sc, project, projectAssetsJsonFilePath, libs);

                if (TargetFramework == null)
                {
                    PackageFolder = YieldMainPackageFolders(projectAssets).First().Path;
                    TargetFramework = projectAssets.Targets[0].TargetFramework;
                    firstProject = project;
                }

                libs[project.AssemblyName] = GetProjectLib(firstProject, project.AssemblyName, TargetFramework,
                    projectAssets.Targets[0].Libraries, versionRanges);

                specialVersions.UnionWith(projectAssets.ProjectFileDependencyGroups[0].Dependencies.Where(o => o.Contains("*")));
            }

            Log.Instance.WriteVerbose("ProjectAssets({0}) : {1} libraries", firstProject, libs?.Count);
            if (libs == null)
            {
                return;
            }

            Libraries = libs;

            Libraries.Values.ForEach(o => o.CompleteConstruction(PackageFolder, TargetFramework, sc, specialVersions, Libraries));
        }

        private static LibraryItem GetProjectLib(ProjectContext firstProject, string asmName, NuGetFramework framework,
            ICollection<LockFileTargetLibrary> projectDependencies, IDictionary<string, VersionRange> versionRanges)
        {
            Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2}", firstProject, asmName, C.V1.Value);
            return LibraryItem.Create(new LockFileTargetLibrary
            {
                Name = asmName,
                Version = C.V1.Value,
                Type = C.PROJECT,
                Framework = framework.DotNetFrameworkName,
                RuntimeAssemblies = new[] { new LockFileItem($"bin/placeholder/{asmName}.dll") },
                Dependencies = projectDependencies.Select(lib => new PackageDependency(lib.Name, GetVersionRange(versionRanges, lib))).ToList()
            }, C.V1.Range);
        }

        private static VersionRange GetVersionRange(IDictionary<string, VersionRange> versionRanges, LockFileTargetLibrary lib) =>
            versionRanges.TryGetValue(lib.Name, out var versionRange) ? versionRange : new VersionRange(lib.Version);

        private static (LockFile projectAssets, IDictionary<string, VersionRange> versionRanges) ProcessProjectFile(SolutionsContext sc, ProjectContext project, string projectAssetsJsonFilePath,
            IDictionary<string, LibraryItem> libs)
        {
            try
            {
                var projectAssets = new LockFileFormat().Read(projectAssetsJsonFilePath);
                var mainPackageFolders = YieldMainPackageFolders(projectAssets).ToList();
                if (mainPackageFolders.Count != 1)
                {
                    var pkgFolders = "";
                    if (mainPackageFolders.Count > 1)
                    {
                        pkgFolders = " - " + string.Join(" , ", mainPackageFolders.Select(o => o.Path));
                    }
                    throw new ApplicationException($"Expected to find exactly one nuget package folder ending with \"\\.nuget\\packages\" . {projectAssetsJsonFilePath} lists {mainPackageFolders.Count}{pkgFolders}");
                }

                sc.NormalizeProjectAssets(project, projectAssets.Targets[0].Libraries);

                var resolved = projectAssets.Targets[0].Libraries
                    .SelectMany(lib => lib.Dependencies)
                    .Concat(projectAssets.Targets[0].Libraries.Select(o => new PackageDependency(o.Name, new VersionRange(o.Version))))
                    .GroupBy(o => o.Id, C.IgnoreCase)
                    .ToDictionary(g => g.Key, g => VersionRange.CommonSubSet(g.Select(o => o.VersionRange)), C.IgnoreCase);

                foreach (var lib in projectAssets.Targets[0].Libraries)
                {
                    if (!libs.TryGetValue(lib.Name, out var prev))
                    {
                        Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2}", project, lib.Name, lib.Version);
                        libs[lib.Name] = LibraryItem.Create(lib, resolved[lib.Name]);
                    }
                    else if (prev.Version < lib.Version)
                    {
                        Log.Instance.WriteVerbose("ProjectAssets({0}) : {1}/{2} (prev {3})", project, lib.Name, lib.Version, prev.Version);
                        libs[lib.Name] = LibraryItem.Create(lib, resolved[lib.Name]);
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

        private static IEnumerable<LockFileItem> YieldMainPackageFolders(LockFile projectAssets) =>
            projectAssets.PackageFolders.Where(o => o.Path.EndsWith("\\.nuget\\packages\\") || o.Path.EndsWith("\\.nuget\\packages"));
    }
}
