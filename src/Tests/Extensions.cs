using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Tests
{
    public static class Extensions
    {
        public static string GetTempDirectoryName()
        {
            var name = Path.GetTempFileName();
            File.Delete(name);
            return name + "\\";
        }

        public static string GetMSBuildExe()
        {
            var drives = new[] { 'C', 'D', 'E', 'F' };
            var products = new[] { "BuildTools", "Enterprise", "Professional", "Community" };
            var toolVersions = new[] { "Current", "15.0" };
            var years = new[] { 2019, 2017 };
            return
                drives.SelectMany(drive =>
                    years.SelectMany(year =>
                        products.SelectMany(product =>
                            toolVersions.Select(toolVersion =>
                                @$"{drive}:\Program Files (x86)\Microsoft Visual Studio\{year}\{product}\MSBuild\{toolVersion}\bin\msbuild.exe"))))
                .FirstOrDefault(File.Exists);
        }

        public static int RunProcess(this string exe, string arguments)
        {
#pragma warning disable CA1416 // Validate platform compatibility
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    Arguments = arguments,
                    FileName = exe,
                    CreateNoWindow = true,
                    LoadUserProfile = false,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                },
            };
#pragma warning restore CA1416 // Validate platform compatibility
            p.Start();
            p.WaitForExit();
            return p.ExitCode;
        }

        public static void RestoreNuGetPackages(this string msBuildExe, string slnFilePath)
        {
            var file = Path.GetTempFileName();
            var exitCode = msBuildExe.RunProcess($"/t:Restore /v:m /m /nologo /noConsoleLogger {slnFilePath} /fl /flp:LogFile={file};Verbosity=minimal");
            TestContext.Progress.WriteLine(File.ReadAllText(file));
            File.Delete(file);
            Assert.AreEqual(0, exitCode);
        }
    }
}
