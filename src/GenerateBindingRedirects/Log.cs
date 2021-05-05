using Dayforce.CSharp.ProjectAssets;
using System;
using System.IO;
using System.Linq;
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
    public class Log
    {
        private static ILog s_baseLog = NullLog.Default;

        public static string LogFilePath { get; private set; }

        public static bool Verbose => s_baseLog == NullLog.Default;

        public static void WriteVerbose(object obj) => s_baseLog.WriteVerbose(obj);
        public static void WriteVerbose(string format, object arg) => s_baseLog.WriteVerbose(format, arg);
        public static void WriteVerbose(string format, object arg, object arg2) => s_baseLog.WriteVerbose(format, arg, arg2);
        public static void WriteVerbose(string format, object arg, object arg2, object arg3) => s_baseLog.WriteVerbose(format, arg, arg2, arg3);
        public static void WriteVerbose(string format, params object[] args) => s_baseLog.WriteVerbose(format, args);

        internal static void Setup(string logPath, string solutionsListFile, string projectFilePath, bool zip)
        {
            var baseDir = Path.GetFullPath(solutionsListFile + (solutionsListFile.EndsWith("Solutions.txt") ? "\\.." : "\\..\\.."));
            var verboseLog = new VerboseLog("GenerateBindingRedirects", logPath, baseDir, projectFilePath, zip);
            LogFilePath = verboseLog.LogFilePath;
            s_baseLog = verboseLog;
            Dayforce.CSharp.ProjectAssets.Log.Instance = s_baseLog;
        }

        internal static void Cleanup()
        {
            using (s_baseLog as IDisposable) { }
            Dayforce.CSharp.ProjectAssets.Log.Instance = s_baseLog = NullLog.Default;
        }

        public static void CuriousCases(Dependents dependents)
        {
            if (!Verbose)
            {
                return;
            }

            foreach (var asmName in dependents.Where(o => o.Key != RuntimeAssembly.Unresolved.AssemblyName))
            {
                foreach (var version in asmName.Value)
                {
                    var log = version.Value.Count > 1;
                    if (log)
                    {
                        WriteVerbose("[Curious] Assembly({0}, Version={1}) : {2} packages", asmName.Key, version.Key, version.Value.Count);
                        version.Value.ForEach(runtimeAssembly => WriteVerbose("[Curious] Assembly({0}, Version={1}) : {2}", asmName.Key, version.Key, runtimeAssembly.Key.RelativeFilePath));
                    }
                    foreach (var runtimeAssembly in version.Value)
                    {
                        if (runtimeAssembly.Value.Count > 1)
                        {
                            WriteVerbose("[Curious] RuntimeAssembly({0}, Version={1}) : {2} variants of the same package", runtimeAssembly.Key.RelativeFilePath, version.Key, runtimeAssembly.Value.Count);
                            runtimeAssembly.Value.ForEach(dependency => WriteVerbose("[Curious] RuntimeAssembly({0}, Version={1}) : {2}", runtimeAssembly.Key.RelativeFilePath, version.Key, dependency.Key.VersionRange));
                        }
                    }
                }
            }
        }

        public static void DependentsByVersion(string asmName, DependentsByVersion dependentsByVersion)
        {
            if (!Verbose)
            {
                return;
            }

            WriteVerbose("DependencyOf({0}) : {1} packages", asmName, dependentsByVersion.Values.SelectMany(a => a.Values.SelectMany(d => d.Values.SelectMany(l => l))).Count());
            dependentsByVersion.ForEach(v =>
            {
                WriteVerbose("DependencyOf({0}, Version = {1}) : {2} packages", asmName, v.Key, v.Value.Values.SelectMany(d => d.Values.SelectMany(l => l)).Count());
                v.Value.ForEach(a =>
                {
                    a.Value.ForEach(d =>
                    {
                        WriteVerbose("DependencyOf({0}, Version = {1}, {2}, {3}) : {4} packages", asmName, v.Key, a.Key.RelativeFilePath, d.Key.VersionRange, d.Value.Count);
                        d.Value.ForEach(library => WriteVerbose("DependencyOf({0}, Version = {1}, {2}, {3}) : {4}", asmName, v.Key, a.Key.RelativeFilePath, d.Key.VersionRange, library));
                    });
                });
            });
        }
    }
}
