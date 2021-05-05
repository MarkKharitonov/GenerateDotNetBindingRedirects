using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using Microsoft.Build.Construction;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;

namespace Dayforce.CSharp.ProjectAssets
{
    public class SolutionsContext
    {
        public readonly IReadOnlyDictionary<string, ProjectContext> ProjectsByAssemblyName;
        private readonly IList<string> m_solutions;
        private readonly IReadOnlyDictionary<(string, string), ProjectContext> m_projectsByName;
        public readonly ProjectContext ThisProjectContext;

        public ProjectContext GetProjectByName(string solution, string projectName)
        {
            if (m_projectsByName.TryGetValue((solution, projectName), out var pc))
            {
                return pc;
            }

            foreach (var sln in m_solutions)
            {
                if (m_projectsByName.TryGetValue((sln, projectName), out pc))
                {
                    return pc;
                }
            }

            throw new ApplicationException($"The project {projectName} could not be found in any of the solutions {string.Join(" , ", m_solutions)}");
        }

        public SolutionsContext(string solutionsListFile, string projectFilePath, ISolutionsListFileReader slnListFileReader)
        {
            var ignoreProjects = ConfigurationManager
                .AppSettings["IgnoreProjects"]
                ?.Split(',')
                .ToHashSet(C.IgnoreCase);

            ProjectContext.Count = 0;

            m_solutions = slnListFileReader.YieldSolutionFilePaths(solutionsListFile).ToList();
            m_projectsByName = m_solutions
                .Select(path => (Solution: SolutionFile.Parse(path), SolutionPath: path))
                .SelectMany(o => o.Solution.ProjectsInOrder.Select(p => (Solution: o.SolutionPath, Project: p)))
                .Where(o => o.Project.AbsolutePath.EndsWith(".csproj") && ignoreProjects?.Contains(o.Project.ProjectName) != true)
                .Select(o => ProjectContext.Create(this, o.Solution, Path.GetFullPath(o.Project.AbsolutePath)))
                .Where(pc => pc != null)
                .ToDictionary(pc => (pc.Solution, pc.ProjectName), C.IgnoreCase2);

            var projectsByAssemblyName = new Dictionary<string, ProjectContext>(C.IgnoreCase);
            foreach (var pc in m_projectsByName.Values)
            {
                if (projectsByAssemblyName.TryGetValue(pc.AssemblyName, out var existing))
                {
                    if (!pc.ProjectFilePath.Equals(existing.ProjectFilePath, C.IGNORE_CASE))
                    {
                        throw new ApplicationException($"Different projects ( {pc.RelativeProjectFilePath} and {existing.RelativeProjectFilePath} ) with the same assembly ( {pc.AssemblyName} ) name are not supported.");
                    }

                    const string PREFIX = "Project {0} is included in more than one solution - {1} and {2}. ";
                    // Same project is included in more than one solution. Take the one which solution is built earlier.
                    if (m_solutions.IndexOf(existing.Solution) < m_solutions.IndexOf(pc.Solution))
                    {
                        Log.Instance.WriteVerbose(PREFIX + "Keeping {3} as primary.", pc.RelativeProjectFilePath, pc.Solution, existing.Solution, existing.Solution);
                        continue;
                    }
                    Log.Instance.WriteVerbose(PREFIX + "Replacing {3} with {4} as primary.", pc.RelativeProjectFilePath, pc.Solution, existing.Solution, existing.Solution, pc.Solution);
                }
                projectsByAssemblyName[pc.AssemblyName] = pc;
            }

            ProjectsByAssemblyName = projectsByAssemblyName;

            foreach (var pc in m_projectsByName.Values.Where(o => !o.AssemblyName.Equals(o.ProjectName, C.IGNORE_CASE)))
            {
                if (ProjectsByAssemblyName.TryGetValue(pc.ProjectName, out var pc2))
                {
                    throw new ApplicationException($"ProjectName({pc.ProjectFilePath}) == AssemblyName({pc2.ProjectFilePath})");
                }
            }

            projectFilePath = Path.GetFullPath(projectFilePath);
            ThisProjectContext = ProjectsByAssemblyName.Values.FirstOrDefault(pc => pc.ProjectFilePath.Equals(projectFilePath, C.IGNORE_CASE));
        }

        public IEnumerable<ProjectContext> YieldProjects()
        {
            var dllReferences = ThisProjectContext.DllReferences.ToHashSet(C.IgnoreCase);
            dllReferences.IntersectWith(ProjectsByAssemblyName.Keys);

            var allDllReferences = new HashSet<string>(C.IgnoreCase);
            do
            {
                allDllReferences.UnionWith(dllReferences);
                dllReferences = dllReferences
                    .SelectMany(asmName => ProjectsByAssemblyName[asmName].DllReferences)
                    .ToHashSet(C.IgnoreCase);
                dllReferences.IntersectWith(ProjectsByAssemblyName.Keys);
                dllReferences.ExceptWith(allDllReferences);
            }
            while (dllReferences.Count > 0);

            yield return ThisProjectContext;

            var projects = allDllReferences.Select(asmName => ProjectsByAssemblyName[asmName]).ToList();
            var groupedBySolution = projects
                .GroupBy(p => p.Solution)
                .Select(g => (Solution: g.Key, Projects: g.ToList()))
                .ToList();

            var referenceMatrix = new bool?[m_projectsByName.Count, m_projectsByName.Count];

            foreach (var g in groupedBySolution)
            {
                var result = new List<ProjectContext>
                {
                    g.Projects[0]
                };

                for (int i = 1; i < g.Projects.Count; ++i)
                {
                    var project = g.Projects[i];
                    bool add = true;
                    for (int j = result.Count - 1; j >= 0 && add; --j)
                    {
                        if (ProjectReferenceExists(project, result[j]))
                        {
                            result.RemoveAt(j);
                        }
                        else if (ProjectReferenceExists(result[j], project))
                        {
                            add = false;
                        }
                    }
                    if (add)
                    {
                        result.Add(project);
                    }
                }

                foreach (var p in result)
                {
                    yield return p;
                }
            }

            bool ProjectReferenceExists(ProjectContext src, ProjectContext dst)
            {
                if (referenceMatrix[src.Index, dst.Index] != null)
                {
                    return referenceMatrix[src.Index, dst.Index].Value;
                }
                if (src.ReferencedProjects.Contains(dst))
                {
                    referenceMatrix[src.Index, dst.Index] = true;
                    return true;
                }

                referenceMatrix[src.Index, dst.Index] = src.ReferencedProjects.Any(p => ProjectReferenceExists(p, dst));
                return referenceMatrix[src.Index, dst.Index].Value;
            }
        }

        public void NormalizeProjectAssets(ProjectContext project, IList<LockFileTargetLibrary> libs)
        {
            foreach (var lib in libs.Where(o => o.Type == C.PROJECT && !ProjectsByAssemblyName.ContainsKey(o.Name)))
            {
                if (!m_projectsByName.TryGetValue((project.Solution, lib.Name), out var pc))
                {
                    throw new ApplicationException($"Project {lib.Name} not found.");
                }
                if (lib.RuntimeAssemblies.Count != 1)
                {
                    throw new ApplicationException($"Project entry for {lib.Name} has {lib.RuntimeAssemblies.Count} runtime assemblies - {string.Join(" , ", lib.RuntimeAssemblies.Select(o => o.Path))}");
                }

                Log.Instance.WriteVerbose("NormalizeAssets({0}) : replace {1} with {2}", project, lib.Name, pc.AssemblyName);

                foreach (var dependent in libs.Where(o => o.Type == C.PROJECT))
                {
                    var i = dependent.Dependencies.FindIndex(0, o => o.VersionRange.Equals(C.V1.Range) && o.Id.Equals(lib.Name, C.IGNORE_CASE));
                    if (i > -1)
                    {
                        Log.Instance.WriteVerbose("NormalizeAssets({0}) : DependencyOf({1}) : replace {2} with {3}", project, dependent.Name, lib.Name, pc.AssemblyName);
                        dependent.Dependencies[i] = new PackageDependency(pc.AssemblyName, C.V1.Range);
                    }
                }
                lib.Name = pc.AssemblyName;
                var ext = lib.RuntimeAssemblies[0].Path.Substring(lib.RuntimeAssemblies[0].Path.Length - 4);
                lib.RuntimeAssemblies[0] = new LockFileItem($"bin/placeholder/{pc.AssemblyName}{ext}");
            }
        }
    }
}
