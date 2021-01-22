using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using System.Linq;

namespace GenerateBindingRedirectsTests
{
    [SetUpFixture]
    public class GlobalContext
    {
        public static string RootDir = Path.GetFullPath($"{Assembly.GetExecutingAssembly().Location}\\..\\..\\..\\..");

        [OneTimeSetUp]
        public static void SetUp()
        {
            var msBuildExe = GetMSBuildExe();
            Assert.IsNotNull(msBuildExe, "Failed to find msbuild.exe");
            Array.ForEach(File.ReadAllLines($"{RootDir}\\Input\\Solutions.txt"), slnFileName => RestoreNuGetPackages(msBuildExe, slnFileName));
        }

        private static string GetMSBuildExe()
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

        private static void RestoreNuGetPackages(string msBuildExe, string slnFileName)
        {
            RunProcess(msBuildExe, $"/t:Restore /v:m /m {RootDir}\\Input\\{slnFileName}");
        }

        private static void RunProcess(string exe, string arguments)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    Arguments = arguments,
                    FileName = exe,
                    CreateNoWindow = true,
                    LoadUserProfile = false,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                },
            };
            p.Start();
            p.WaitForExit();
            Assert.AreEqual(0, p.ExitCode);
        }
    }
}
