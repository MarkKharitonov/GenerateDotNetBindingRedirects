using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    public static class Log
    {
        private static string s_baseDir;
        private static TextWriter s_logWriter;
        private static string s_logPath;
        private static bool s_zip;

        public static string LogFilePath { get; private set; }

        internal static void Save(string projectAssetsJsonFilePath)
        {
            if (Verbose)
            {
                var dstFilePath = s_logPath + "\\" + GetRelativeFilePath(projectAssetsJsonFilePath).Replace("\\", "__").Replace("__obj__project.assets.json", ".assets.json");
                File.Copy(projectAssetsJsonFilePath, dstFilePath, true);
            }
        }

        internal static readonly string DefaultLogDirectory = GetDefaultLogDirectory();

        private static string GetDefaultLogDirectory()
        {
            var logDir = Environment.GetEnvironmentVariable("Build_StagingDirectory");
            if (string.IsNullOrEmpty(logDir))
            {
                logDir = Environment.GetEnvironmentVariable("System_ArtifactsDirectory");
                if (string.IsNullOrEmpty(logDir))
                {
                    logDir = $"{Path.GetTempPath()}\\a{Process.GetCurrentProcess().Id}_{DateTime.Now:yyyyMMddHHmmss}";
                    // May already exist if called twice from different scripts when ran locally
                    Directory.CreateDirectory(logDir);
                    return logDir;
                }
            }
            logDir += "\\drop";
            Directory.CreateDirectory(logDir);
            return logDir;
        }

        public static bool Verbose { get; private set; }

        public static void WriteVerbose(object obj)
        {
            if (Verbose)
            {
                s_logWriter.WriteLine(obj);
            }
        }

        public static void WriteVerbose(string format, object arg)
        {
            if (Verbose)
            {
                s_logWriter.WriteLine(format, arg);
            }
        }
        public static void WriteVerbose(string format, object arg, object arg2)
        {
            if (Verbose)
            {
                s_logWriter.WriteLine(format, arg, arg2);
            }
        }

        public static void WriteVerbose(string format, object arg, object arg2, object arg3)
        {
            if (Verbose)
            {
                s_logWriter.WriteLine(format, arg, arg2, arg3);
            }
        }
        public static void WriteVerbose(string format, params object[] args)
        {
            if (Verbose)
            {
                s_logWriter.WriteLine(format, args);
            }
        }

        public static string GetRelativeFilePath(string filePath)
        {
            if (!Verbose)
            {
                return null;
            }

            if (s_baseDir == null)
            {
                return filePath;
            }

            return Path.GetRelativePath(s_baseDir, filePath);
        }

        internal static void Setup(string logPath, string solutionsListFile, string projectFilePath, bool zip)
        {
            if (File.Exists(logPath))
            {
                throw new ApplicationException($"{logPath} must not exist or be a directory.");
            }

            Verbose = true;
            s_baseDir = solutionsListFile + "\\..";
            var relativeFilePath = GetRelativeFilePath(projectFilePath);
            if (logPath == null)
            {
                logPath = DefaultLogDirectory;
            }
            var delim = logPath.EndsWith('\\') ? "" : "\\";
            string buildDefName = Environment.GetEnvironmentVariable("Build_DefinitionName");
            string buildNumber = Environment.GetEnvironmentVariable("Build_BuildNumber");
            var delim2 = string.IsNullOrEmpty(buildDefName) ? "" : ".";
            var delim3 = string.IsNullOrEmpty(buildNumber) ? "" : ".";
            s_logPath = $"{logPath}{delim}GenerateBindingRedirects{delim2}{buildDefName}{delim3}{buildNumber}";
            Directory.CreateDirectory(s_logPath);
            s_logPath += "\\" + relativeFilePath.Replace("\\", "__").Replace(".csproj", "");
            Directory.CreateDirectory(s_logPath);
            LogFilePath = s_logPath + "\\verbose.log";
            s_logWriter = new StreamWriter(LogFilePath);
            Console.WriteLine("Verbose log folder: " + s_logPath);
            s_zip = zip;
        }

        internal static string ToString(IEnumerable<object> items) => Verbose ? string.Join(" , ", items) : null;

        public static void Cleanup()
        {
            if (s_logWriter != null)
            {
                s_logWriter.Close();
                s_logWriter = null;
                if (s_zip)
                {
                    var zipFile = s_logPath + ".zip";
                    File.Delete(zipFile);
                    ZipFile.CreateFromDirectory(s_logPath, zipFile);
                    Directory.Delete(s_logPath, true);
                }
            }
            Verbose = false;
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
