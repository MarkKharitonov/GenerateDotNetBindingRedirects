using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace Dayforce.CSharp.ProjectAssets
{
    public class VerboseLog : ILog, IDisposable
    {
        private readonly TextWriter m_logWriter;
        private readonly string m_logFolder;
        private readonly string m_baseDir;
        private readonly bool m_zip;
        public static readonly string DefaultLogDirectory = GetDefaultLogDirectory();

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

        public VerboseLog(string appName, string logFolder, string baseDir, string projectFilePath, bool zip)
        {
            if (logFolder == null)
            {
                logFolder = DefaultLogDirectory;
            }
            else if (File.Exists(logFolder))
            {
                throw new ApplicationException($"{logFolder} must not exist or be a directory.");
            }

            var delim = logFolder.EndsWith('\\') ? "" : "\\";
            string buildDefName = Environment.GetEnvironmentVariable("Build_DefinitionName");
            string buildNumber = Environment.GetEnvironmentVariable("Build_BuildNumber");
            var delim2 = string.IsNullOrEmpty(buildDefName) ? "" : ".";
            var delim3 = string.IsNullOrEmpty(buildNumber) ? "" : ".";
            logFolder += 
                delim + appName + delim2 + buildDefName + delim3 + buildNumber + "\\" + 
                Path.GetRelativePath(baseDir, projectFilePath).Replace("\\", "__").Replace(".csproj", "");
            Directory.CreateDirectory(logFolder);

            LogFilePath = logFolder + "\\verbose.log";
            m_logWriter = new StreamWriter(LogFilePath);
            m_logFolder = logFolder;
            m_baseDir = baseDir;
            m_zip = zip;
            Console.WriteLine("Verbose log folder: " + m_logFolder);
        }

        public void Dispose()
        {
            if (m_logWriter != null)
            {
                m_logWriter.Close();
                if (m_zip)
                {
                    var zipFile = m_logFolder + ".zip";
                    File.Delete(zipFile);
                    ZipFile.CreateFromDirectory(m_logFolder, zipFile);
                    Directory.Delete(m_logFolder, true);
                }
            }
        }

        public string LogFilePath { get; }

        public void Save(string projectAssetsJsonFilePath) 
        {
            Directory.CreateDirectory(m_logFolder);
            var dstFilePath = m_logFolder + "\\" + GetRelativeFilePath(projectAssetsJsonFilePath).Replace("\\", "__").Replace("__obj__project.assets.json", ".assets.json");
            File.Copy(projectAssetsJsonFilePath, dstFilePath, true);
        }

        public string GetRelativeFilePath(string filePath) => Path.GetRelativePath(m_baseDir, filePath);

        public void WriteVerbose(object obj) => m_logWriter.WriteLine(obj);
        public void WriteVerbose(string format, object arg) => m_logWriter.WriteLine(format, arg);
        public void WriteVerbose(string format, object arg1, object arg2) => m_logWriter.WriteLine(format, arg1, arg2);
        public void WriteVerbose(string format, object arg1, object arg2, object arg3) => m_logWriter.WriteLine(format, arg1, arg2, arg3);
        public void WriteVerbose(string format, params object[] args) => m_logWriter.WriteLine(format, args);
    }
}
