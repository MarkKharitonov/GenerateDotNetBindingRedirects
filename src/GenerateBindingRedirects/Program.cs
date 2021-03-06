﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Mono.Cecil;
using Mono.Options;
using NuGet.ProjectModel;
using NuGet.Versioning;
using Dependents = System.Collections.Generic.SortedDictionary<string,
    System.Collections.Generic.Dictionary<System.Version,
        System.Collections.Generic.Dictionary<GenerateBindingRedirects.RuntimeAssembly,
            System.Collections.Generic.Dictionary<GenerateBindingRedirects.NuGetDependency,
                System.Collections.Generic.List<GenerateBindingRedirects.LibraryItem>>>>>;
using DependentsByVersion = System.Collections.Generic.Dictionary<System.Version,
        System.Collections.Generic.Dictionary<GenerateBindingRedirects.RuntimeAssembly,
            System.Collections.Generic.Dictionary<GenerateBindingRedirects.NuGetDependency,
                System.Collections.Generic.List<GenerateBindingRedirects.LibraryItem>>>>;

namespace GenerateBindingRedirects
{
    public enum DebugMode
    {
        None,
        Break,
        Loop
    }

    public static partial class Program
    {
        public static int Main(string[] args)
        {
            string projectFilePath = null;
            string solutionsListFile = null;
            var writeBindingRedirects = false;
            string outputBindingRedirects = null;
            string outputTargetFiles = null;
            var assert = false;
            var help = false;
            var debugMode = DebugMode.None;
            var verbose = false;
            string logPath = Log.DefaultLogDirectory;
            List<string> extraArgs = new List<string>();
            var test = false;
            string privateProbingPath = null;
            string[] verboseTargets = null;
            var options = new OptionSet()
                .Add("h|help|?", "Show help", _ => help = true)
                .Add("v|verbose:", $"Produces verbose output. May be given a custom directory path where to collect extended information. Defaults to {logPath}", v => { logPath = v ?? logPath; verbose = true; })
                .Add("vt|verboseTargets=", "Comma separated list of names of projects for which to collect the verbose logs. The default is for all.", v => verboseTargets = v.Split(','))
                .Add("test", "Test mode:\r\n" +
                " - generate relative file paths in the --targetFiles output, so that it could be compared across the machine boundaries\r\n" +
                " - do not zip the verbose payload, so that tests could examine it easily", _ => test = true)
                .Add("debugMode=", $"Debug mode. One of {string.Join(" , ", Enum.GetNames(typeof(DebugMode)))} . Defaults to {debugMode} .", (DebugMode v) => debugMode = v)
                .Add("f=|projectFile=", "[Required] The project file.", v => projectFilePath = v)
                .Add("s=|solutions=", "[Required] A file listing all the relevant solutions.", v => solutionsListFile = v)
                .Add("t|targetFiles=", "Output the target file paths to the given file. Use - to output to console.", v => outputTargetFiles = v)
                .Add("r|bindingRedirects=", "Output the binding redirects to the given file. Use - to output to console.", v => outputBindingRedirects = v)
                .Add("w|writeBindingRedirects", "Write the binding redirects to the respective config file. Mutually exclusive with --assert.", _ => writeBindingRedirects = true)
                .Add("p|privateProbingPath=", @"Include the <probing privatePath=.../> element in the generated assembly binding redirects.", v => privateProbingPath = v.Replace('\\', '/'))
                .Add("a|assert", "Asserts that the binding redirects are correct. Mutually exclusive with --writeBindingRedirects.", _ => assert = true)
                .Add("<>", extraArgs.Add);
            ;

            options.Parse(args);

            BreakIfNeeded(projectFilePath, debugMode, extraArgs);

            if (help || args.Length == 0)
            {
                options.WriteOptionDescriptions(Console.Out);
                return 0;
            }
            if (extraArgs.Count > 0)
            {
                LogErrorMessage($"Unrecognized command line arguments \"{string.Join(" ", extraArgs)}\"");
                Console.WriteLine();
                options.WriteOptionDescriptions(Console.Out);
                return 2;
            }
            if (projectFilePath == null)
            {
                LogErrorMessage($"--projectFile is required.");
                return 2;
            }
            if (!File.Exists(projectFilePath))
            {
                LogErrorMessage($"The file {projectFilePath} does not exist.");
                return 2;
            }
            if (solutionsListFile == null)
            {
                LogErrorMessage($"--solutions is required.");
                return 2;
            }
            if (!File.Exists(solutionsListFile))
            {
                LogErrorMessage($"The file {solutionsListFile} does not exist.");
                return 2;
            }
            if (assert && writeBindingRedirects)
            {
                LogErrorMessage($"--assert and --writeBindingRedirects are mutually exclusive.");
                return 2;
            }

            try
            {
                Run(projectFilePath, solutionsListFile, outputTargetFiles, outputBindingRedirects, writeBindingRedirects,
                    privateProbingPath, assert, test, verbose, logPath, verboseTargets);
            }
            catch (ApplicationException exc)
            {
                LogErrorMessage(exc.Message);
                Log.WriteVerbose(exc);
                return 3;
            }
            catch (Exception exc)
            {
                LogErrorMessage(exc.ToString());
                LogErrorMessage(exc.Message);
                Log.WriteVerbose(exc);
                return 3;
            }
            finally
            {
                Log.Cleanup();
            }
            return 0;
        }

        private static void LogErrorMessage(string msg) => Console.WriteLine("GenerateBindingRedirects: ERROR: " + msg);

        public static void Run(string projectFilePath, string solutionsListFile, string outputTargetFiles, string outputBindingRedirects, bool writeBindingRedirects,
            string privateProbingPath = null,
            bool assert = false,
            bool test = false,
            bool verbose = false,
            string logPath = null,
            string[] verboseTargets = null)
        {
            if (verbose && (verboseTargets == null || verboseTargets.Contains(Path.GetFileNameWithoutExtension(projectFilePath), C.IgnoreCase)))
            {
                Log.Setup(logPath, solutionsListFile, projectFilePath, !test);
            }

            var sc = new SolutionsContext(solutionsListFile, projectFilePath);
            if (sc.ThisProjectContext == null)
            {
                throw new ApplicationException($"The project {projectFilePath} cannot be processed.");
            }

            var projectAssets = new ProjectAssets(sc);

            if (projectAssets.PackageFolder == null)
            {
                throw new ApplicationException($"No project.assets.json is associated with {projectFilePath} and {solutionsListFile}.");
            }

            if (Log.Verbose)
            {
                OutputShortPackageSummary(projectAssets, "some", p => p.RuntimeAssemblies.Count > 0);
                OutputShortPackageSummary(projectAssets, "no", p => p.RuntimeAssemblies.Count == 0);
                var projects = projectAssets.Libraries.Values.OfType<ProjectItem>();

                Log.WriteVerbose("{0} projects:", projects.Count());
                projects.ForEach(p => Log.WriteVerbose("  {0}", p.Name));
            }

            var dependents = GetDependents(projectAssets);
            Log.CuriousCases(dependents);
            AddAssemblyReferencesForUnresolvedOrMismatchingNuGetDependencies(dependents, projectAssets.FrameworkRedistList);

            var assemblyBindingRedirects = dependents
                .Where(kvp => kvp.Value.Count > 1)
                .Select(kvp => GetAssemblyBindingRedirect(kvp.Key, kvp.Value, projectAssets.FrameworkRedistList))
                .Where(o => o != null)
                .ToList();

            if (outputTargetFiles != null)
            {
                IEnumerable<string> targetFiles;
                if (test)
                {
                    targetFiles = assemblyBindingRedirects.Select(a =>
                        a.IsFrameworkAssembly ?
                        a.TargetFilePath :
                        Path.GetRelativePath(projectAssets.PackageFolder, a.TargetFilePath)).OrderBy(o => o);
                }
                else
                {
                    targetFiles = assemblyBindingRedirects
                        .Where(a => !a.IsFrameworkAssembly)
                        .Select(a => a.TargetFilePath);
                }
                if (outputTargetFiles == "-")
                {
                    targetFiles.ForEach(Console.WriteLine);
                }
                else
                {
                    Directory.CreateDirectory($"{outputTargetFiles}\\..");
                    File.WriteAllLines(outputTargetFiles, targetFiles);
                }
            }

            if (outputBindingRedirects != null || writeBindingRedirects || assert)
            {
                var res = string.Join(Environment.NewLine, assemblyBindingRedirects.OrderBy(r => r.AssemblyName).Select(a => a.Render(privateProbingPath)));
                if (outputBindingRedirects != null)
                {
                    if (outputBindingRedirects == "-")
                    {
                        Console.WriteLine(res);
                    }
                    else if (assert)
                    {
                        AssertBindingRedirectsInFile(outputBindingRedirects, res);
                    }
                    else
                    {
                        Directory.CreateDirectory($"{outputBindingRedirects}\\..");
                        File.WriteAllText(outputBindingRedirects, res);
                    }
                }

                if (writeBindingRedirects || assert)
                {
                    sc.ThisProjectContext.WriteBindingRedirects(res, assert);
                }
            }
        }

        private static void AssertBindingRedirectsInFile(string outputBindingRedirects, string expected)
        {
            if (!File.Exists(outputBindingRedirects))
            {
                if (!string.IsNullOrWhiteSpace(expected))
                {
                    throw new ApplicationException($"Found some binding redirects, but {outputBindingRedirects} does not exist.");
                }
                return;
            }
            var actual = File.ReadAllText(outputBindingRedirects).TrimEnd();
            if (actual != expected)
            {
                throw new ApplicationException($"Actual binding redirects in {outputBindingRedirects} do not match the expectation.");
            }
        }

        private static void BreakIfNeeded(string projectFilePath, DebugMode debugMode, List<string> extraArgs)
        {
            if (debugMode != DebugMode.None)
            {
                if (extraArgs.Count == 0 ||
                    projectFilePath == null ||
                    extraArgs.Any(keyword => projectFilePath.IndexOf(keyword, C.IGNORE_CASE) >= 0))
                {
                    switch (debugMode)
                    {
                    case DebugMode.Break:
                        Debugger.Launch();
                        break;
                    case DebugMode.Loop:
                        while (true)
                        {
                            Thread.Sleep(1000);
                        }
                    }
                }
                extraArgs.Clear();
            }
        }

        private static void OutputShortPackageSummary(ProjectAssets projectAssets, string keyword, Func<PackageItem, bool> predicate)
        {
            var packages = projectAssets.Libraries.Values.OfType<PackageItem>().Where(predicate);

            Log.WriteVerbose("{0} NuGet packages with {1} runtime assemblies:", packages.Count(), keyword);
            packages.ForEach(p => Log.WriteVerbose("  [{0}] = {1}", p.Name, p));
        }

        private static void AddAssemblyReferencesForUnresolvedOrMismatchingNuGetDependencies(Dependents dependents, IReadOnlyDictionary<(string, Version), AssemblyBindingRedirect> frameworkRedistList)
        {
            // 1. Microsoft.AspNet.Web.Optimization.1.1.3 depends on WebGrease.1.5.2
            // 2. WebGrease.1.5.2 contains WebGrease.dll with the assembly version of 1.5.2.14234
            // 3. Microsoft.AspNet.Web.Optimization.1.1.3 contains System.Web.Optimization.dll which references WebGrease.dll with the assembly version of 1.5.1.25624
            // When NuGet packages lie we have no choice but examine the assemblies
            var assemblyReferences = new SortedDictionary<(string, Version), HashSet<PackageItem>>();

            foreach (var package in dependents.Values.SelectMany(v => v.Values.SelectMany(r => r.Values.SelectMany(d => d.Values.SelectMany(l => l).OfType<PackageItem>()))))
            {
                foreach (var runtimeAssembly in package.RuntimeAssemblies)
                {
                    var module = ModuleDefinition.ReadModule(runtimeAssembly.FilePath);
                    foreach (var asmRef in module.AssemblyReferences)
                    {
                        if (asmRef.PublicKeyToken?.Length == 0 && asmRef.PublicKey?.Length == 0)
                        {
                            Log.WriteVerbose("AssemblyReferenceOf({0}) : skip {1} ({2}) - unsigned", runtimeAssembly.RelativeFilePath, asmRef.Name, asmRef.Version);
                            continue;
                        }

                        if (!dependents.TryGetValue(asmRef.Name, out var dependentsByVersion))
                        {
                            var extra = frameworkRedistList.ContainsKey((asmRef.Name, asmRef.Version)) ? "framework assembly" : "unknown";
                            Log.WriteVerbose("AssemblyReferenceOf({0}) : skip {1} ({2}) - not found ({3})", runtimeAssembly.RelativeFilePath, asmRef.Name, asmRef.Version, extra);
                            continue;
                        }

                        if (dependentsByVersion.ContainsKey(asmRef.Version))
                        {
                            Log.WriteVerbose("AssemblyReferenceOf({0}) : skip {1} ({2}) - version match", runtimeAssembly.RelativeFilePath, asmRef.Name, asmRef.Version);
                            continue;
                        }

                        Log.WriteVerbose("AssemblyReferenceOf({0}) : add {1} ({2})", runtimeAssembly.RelativeFilePath, asmRef.Name, asmRef.Version);
                        if (!assemblyReferences.TryGetValue((asmRef.Name, asmRef.Version), out var packages))
                        {
                            assemblyReferences[(asmRef.Name, asmRef.Version)] = packages = new HashSet<PackageItem>();
                        }
                        packages.Add(package);
                    }
                }
            }

            foreach (var o in assemblyReferences)
            {
                var (asmRefName, asmRefVersion) = o.Key;
                foreach (var package in o.Value)
                {
                    var dependentsByVersion = dependents[asmRefName];
                    dependentsByVersion[asmRefVersion] = new Dictionary<RuntimeAssembly, Dictionary<NuGetDependency, List<LibraryItem>>>
                    {
                        [RuntimeAssembly.Unresolved] = new Dictionary<NuGetDependency, List<LibraryItem>>
                        {
                            [LibraryItem.UnresolvedNuGetDependency] = new List<LibraryItem> { package }
                        }
                    };
                }
            }

            dependents.Remove(RuntimeAssembly.Unresolved.AssemblyName);
        }

        private static Dependents GetDependents(ProjectAssets projectAssets)
        {
            var dependents = new Dependents(C.IgnoreCase);
            foreach (var lib in projectAssets.Libraries.Values)
            {
                foreach (var dep in lib.NuGetDependencies)
                {
                    dep.AssertSimple(lib);
                    foreach (var r in dep.RuntimeAssemblyItems)
                    {
                        if (!lib.HasRuntimeAssemblies)
                        {
                            Log.WriteVerbose("DependencyOf({0}, Version = {1}) : skip {2} - no runtime assemblies", r.AssemblyName, r.AssemblyVersion, lib);
                            continue;
                        }

                        if (!dependents.TryGetValue(r.AssemblyName, out var asmVersions))
                        {
                            dependents[r.AssemblyName] = asmVersions = new DependentsByVersion();
                        }
                        if (!asmVersions.TryGetValue(r.AssemblyVersion, out var runtimeAssemblies))
                        {
                            asmVersions[r.AssemblyVersion] = runtimeAssemblies = new Dictionary<RuntimeAssembly, Dictionary<NuGetDependency, List<LibraryItem>>>();
                        }
                        if (!runtimeAssemblies.TryGetValue(r, out var dependencies))
                        {
                            runtimeAssemblies[r] = dependencies = new Dictionary<NuGetDependency, List<LibraryItem>>();
                        }
                        if (!dependencies.TryGetValue(dep, out var libraries))
                        {
                            dependencies[dep] = libraries = new List<LibraryItem>();
                        }
                        libraries.Add(lib);
                    }
                }
            }

            return dependents;
        }

        public static AssemblyBindingRedirect GetAssemblyBindingRedirect(string asmName, DependentsByVersion dependentsByVersion,
            IReadOnlyDictionary<(string, Version), AssemblyBindingRedirect> frameworkRedistList)
        {
            Log.DependentsByVersion(asmName, dependentsByVersion);

            var maxAsmVersion = dependentsByVersion.Keys.Max();
            var runtimeAssemblies = dependentsByVersion[maxAsmVersion];
            var found = runtimeAssemblies.First();
            if (runtimeAssemblies.Count > 1)
            {
                var maxNuGetVersion = GetMaxNuGetVersion(found.Value);
                foreach (var r in runtimeAssemblies.Skip(1))
                {
                    var other = GetMaxNuGetVersion(r.Value);
                    if (other > maxNuGetVersion)
                    {
                        found = r;
                        maxNuGetVersion = other;
                    }
                }
            }

            AssemblyBindingRedirect res;
            if (found.Key.FilePath == RuntimeAssembly.Unresolved.FilePath)
            {
                // We do not have an actual file for the binding redirect. Maybe it is part of the .Net framework?
                if (frameworkRedistList.TryGetValue((asmName, maxAsmVersion), out res))
                {
                    Log.WriteVerbose("NewAssemblyBindingRedirect : skip framework assembly {0}", res);
                    return null;
                }

                throw new ApplicationException($"Unable to resolve assembly binding redirect for {asmName}, Version = {maxAsmVersion}.");
            }

            if (found.Key.IsUnsigned)
            {
                Log.WriteVerbose("NewAssemblyBindingRedirect : skip unsigned assembly {0} - {1}", found.Key.RelativeFilePath, found.Key.AssemblyName);
                return null;
            }

            res = new AssemblyBindingRedirect(found.Key.FilePath);
            Log.WriteVerbose("NewAssemblyBindingRedirect : {0} - {1}", found.Key.FilePath, res);
            return res;
        }

        private static NuGetVersion GetMaxNuGetVersion(Dictionary<NuGetDependency, List<LibraryItem>> value) => value.Keys.Select(o => o.VersionRange.MinVersion).Max();

        public static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
        {
            foreach (var item in items)
            {
                action(item);
            }
        }

        public static int FindIndex<T>(this IList<T> source, int startIndex, Predicate<T> match)
        {
            for (int i = startIndex; i < source.Count; ++i)
            {
                if (match(source[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        public static bool IsExecutable(this string path) => path.EndsWith(".dll", C.IGNORE_CASE) || path.EndsWith(".exe", C.IGNORE_CASE);

        public static bool HasExecutable(this IList<LockFileItem> files) => files.Any(o => o.Path.IsExecutable());

        public static void RemoveAll<K, V>(this IDictionary<K, V> d, Predicate<KeyValuePair<K, V>> predicate)
        {
            List<K> remove = null;
            foreach (var kvp in d)
            {
                if (predicate(kvp))
                {
                    if (remove == null)
                    {
                        remove = new List<K>();
                    }
                    remove.Add(kvp.Key);
                }
            }
            remove?.ForEach(k => d.Remove(k));
        }
    }
}
