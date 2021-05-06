using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Tests
{
    [SetUpFixture]
    public class GlobalContext
    {
        public static readonly string RootDir = Path.GetFullPath($"{Assembly.GetExecutingAssembly().Location}\\..\\..\\..\\..");
        public static readonly string MSBuildExe = Extensions.GetMSBuildExe();
        private static string s_outputDir;

        public static string OutputDir
        {
            get
            {
                if (s_outputDir == null)
                {
                    s_outputDir = Extensions.GetTempDirectoryName();
                }
                return s_outputDir;
            }
        }

        public static void CleanOutputDir()
        {
            if (s_outputDir != null && Directory.Exists(s_outputDir))
            {
                Directory.Delete(s_outputDir, true);
            }
        }

        [OneTimeSetUp]
        public static void SetUp()
        {
            Assert.IsNotNull(MSBuildExe, "Failed to find msbuild.exe");
        }
    }
}
