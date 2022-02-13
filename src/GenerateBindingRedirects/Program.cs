using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Dayforce.CSharp.ProjectAssets;
using Mono.Cecil;
using Mono.Options;
using Newtonsoft.Json;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Versioning;
using Dependents = System.Collections.Generic.SortedDictionary<string,
    System.Collections.Generic.Dictionary<System.Version,
        System.Collections.Generic.Dictionary<Dayforce.CSharp.ProjectAssets.RuntimeAssembly,
            System.Collections.Generic.Dictionary<Dayforce.CSharp.ProjectAssets.NuGetDependency,
                System.Collections.Generic.List<Dayforce.CSharp.ProjectAssets.LibraryItem>>>>>;
using DependentsByVersion = System.Collections.Generic.Dictionary<System.Version,
        System.Collections.Generic.Dictionary<Dayforce.CSharp.ProjectAssets.RuntimeAssembly,
            System.Collections.Generic.Dictionary<Dayforce.CSharp.ProjectAssets.NuGetDependency,
                System.Collections.Generic.List<Dayforce.CSharp.ProjectAssets.LibraryItem>>>>;

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
            string logPath = VerboseLog.DefaultLogDirectory;
            List<string> extraArgs = new List<string>();
            var test = false;
            string privateProbingPath = null;
            string[] verboseTargets = null;
            string nuGetUsageReport = null;
            bool allowNonexistingSolutions = false;
            bool forceAssert = false;
            bool dumpSolutionContext = false;
            var options = new OptionSet()
                .Add("h|help|?", "Show help", _ => help = true)
                .Add("v|verbose:", $"Produces verbose output. May be given a custom directory path where to collect extended information. Defaults to {logPath}", v => { logPath = v ?? logPath; verbose = true; })
                .Add("vt|verboseTargets=", "Comma separated list of names of projects for which to collect the verbose logs. The default is for all.", v => verboseTargets = v.Split(','))
                .Add("test", "Test mode:\r\n" +
                " - generate relative file paths in the --targetFiles output, so that it could be compared across the machine boundaries\r\n" +
                " - do not zip the verbose payload, so that tests could examine it easily", _ => test = true)
                .Add("debugMode=", $"Debug mode. One of {string.Join(" , ", Enum.GetNames(typeof(DebugMode)))} . Defaults to {debugMode} .", (DebugMode v) => debugMode = v)
                .Add("f|projectFile=", "[Required] The project file.", v => projectFilePath = v)
                .Add("s|solutions=", "[Required] A file listing all the relevant solutions.", v => solutionsListFile = v)
                .Add("t|targetFiles=", "Output the target file paths to the given file. Use - to output to console.", v => outputTargetFiles = v)
                .Add("r|bindingRedirects=", "Output the binding redirects to the given file. Use - to output to console.", v => outputBindingRedirects = v)
                .Add("w|writeBindingRedirects", "Write the binding redirects to the respective config file. Mutually exclusive with --assert and --forceAssert. " +
                                                "If a new app.config file is created, then it is automatically added to the local .gitignore, which is created if needed.", _ => writeBindingRedirects = true)
                .Add("p|privateProbingPath=", @"Include the <probing privatePath=.../> element in the generated assembly binding redirects.", v => privateProbingPath = v.Replace('\\', '/'))
                .Add("a|assert", "Asserts that the binding redirects are correct. Mutually exclusive with --writeBindingRedirects and --forceAssert. " +
                                    "The parameter behaves as --forceAssert if --bindingRedirects is given. " +
                                    "The parameter behaves as --writeBindingRedirects (except it does not create .gitignore) otherwise AND if { the app.config file does not exist initially OR if it is not tracked by git }. " +
                                    "To force the assertion logic in all the cases use the flag --forceAssert.", _ => assert = true)
                .Add("forceAssert", "Unconditionally asserts that the binding redirects are correct. Mutually exclusive with--writeBindingRedirects and --assert.", _ => forceAssert = true)
                .Add("u|nuGetUsageReport=", "Generate a report listing all the nuget packages on which the given project depends and save it under the given file path.", v => nuGetUsageReport = v)
                .Add("allowNonexistingSolutions", "Silently skip non existing solutions mentioned in the given solutions list file.", _ => allowNonexistingSolutions = true)
                .Add("dumpSolutionContext", "Dumps the solution context as JSON and exits. Most of other command line arguments are silently ignored.", _ => dumpSolutionContext = true)
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
            if (!dumpSolutionContext)
            {
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
            if (forceAssert && writeBindingRedirects)
            {
                LogErrorMessage($"--forceAssert and --writeBindingRedirects are mutually exclusive.");
                return 2;
            }
            if (forceAssert && assert)
            {
                LogErrorMessage($"--forceAssert and --assert are mutually exclusive.");
                return 2;
            }

            try
            {
                if (verbose && (verboseTargets == null || verboseTargets.Contains(Path.GetFileNameWithoutExtension(projectFilePath), C.IgnoreCase)))
                {
                    Log.Setup(logPath, solutionsListFile, projectFilePath, !test);
                }

                Run(projectFilePath, solutionsListFile, outputTargetFiles, outputBindingRedirects, writeBindingRedirects,
                    privateProbingPath, assert, test, nuGetUsageReport, allowNonexistingSolutions, forceAssert, dumpSolutionContext);
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
            string nuGetUsageReport = null,
            bool allowNonexistingSolutions = false,
            bool forceAssert = false,
            bool dumpSolutionContext = false)
        {
            var sc = new SolutionsContext(solutionsListFile, new DayforceSolutionsListFileReader(), allowNonexistingSolutions);
            if (dumpSolutionContext)
            {
                Console.WriteLine(JsonConvert.SerializeObject(sc, Formatting.Indented));
                return;
            }

            var focus = sc.GetProjectContext(projectFilePath);
            if (focus == null)
            {
                throw new ApplicationException($"The project {projectFilePath} cannot be processed, because no solution seems to contain it. Most likely a case of corrupt solution file.");
            }

            var projectAssets = new ProjectAssets(sc, focus);

            if (projectAssets.PackageFolders == null)
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
            if (nuGetUsageReport != null)
            {
                projectAssets.GenerateNuGetUsageReport(focus.ProjectName, nuGetUsageReport);
            }

            var dependents = GetDependents(projectAssets);
            Log.CuriousCases(dependents);

            var frameworkRedistList = GetFrameworkRedistList(projectAssets.TargetFramework);
            AddAssemblyReferencesForUnresolvedOrMismatchingNuGetDependencies(dependents, frameworkRedistList);

            var assemblyBindingRedirects = dependents
                .Where(kvp => kvp.Value.Count > 1)
                .Select(kvp => GetAssemblyBindingRedirect(kvp.Key, kvp.Value, frameworkRedistList))
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
                        Path.GetRelativePath(projectAssets.PackageFolders.First(a.TargetFilePath.StartsWith), a.TargetFilePath)).OrderBy(o => o);
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

            if (outputBindingRedirects != null || writeBindingRedirects || assert || forceAssert)
            {
                var res = string.Join(Environment.NewLine, assemblyBindingRedirects
                    .Where(r => !string.IsNullOrEmpty(r.PublicKeyToken) || privateProbingPath != null)
                    .OrderBy(r => r.AssemblyName)
                    .Select(a => a.Render(privateProbingPath)));
                if (outputBindingRedirects != null)
                {
                    if (outputBindingRedirects == "-")
                    {
                        Console.WriteLine(res);
                    }
                    else if (forceAssert || assert)
                    {
                        AssertBindingRedirectsInFile(outputBindingRedirects, res);
                        forceAssert = true;
                    }
                    else
                    {
                        Directory.CreateDirectory($"{outputBindingRedirects}\\..");
                        File.WriteAllText(outputBindingRedirects, res);
                    }
                }

                if (writeBindingRedirects || assert || forceAssert)
                {
                    var writer = new BindingRedirectsWriter(focus);
                    writer.WriteBindingRedirects(res, assert, forceAssert);
                }
            }
        }

        private static IReadOnlyDictionary<(string, Version), AssemblyBindingRedirect> GetFrameworkRedistList(NuGetFramework framework)
        {
            var version = framework.DotNetFrameworkName.Replace($"{framework.Framework},Version=", "");
            string path = @$"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\{framework.Framework}\{version}\RedistList\FrameworkList.xml";
            return XDocument
                .Load(path)
                .Element("FileList")
                .Elements("File")
                .Select(e => new AssemblyBindingRedirect(
                    e.Attribute("AssemblyName").Value,
                    Version.Parse(e.Attribute("Version").Value),
                    e.Attribute("Culture").Value,
                    e.Attribute("PublicKeyToken").Value))
                .ToDictionary(a => (a.AssemblyName, a.Version));
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

            res = new AssemblyBindingRedirect(found.Key.FilePath);
            Log.WriteVerbose("NewAssemblyBindingRedirect : {0} - {1}", found.Key.FilePath, res);
            return res;
        }

        private static NuGetVersion GetMaxNuGetVersion(Dictionary<NuGetDependency, List<LibraryItem>> value) => value.Keys.Select(o => o.VersionRange.MinVersion).Max();

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
