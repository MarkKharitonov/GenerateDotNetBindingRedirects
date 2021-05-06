using System.Collections.Generic;
using System.IO;
using Dayforce.CSharp.ProjectAssets;
using GenerateNuGetUsageReport;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class NuGetUsageReportTests
    {
        private static readonly bool s_updateExpectedResults = false;

        [OneTimeSetUp]
        public static void OneTimeSetUp()
        {
            var slnListFileReader = new SimpleSolutionsListFileReader();
            slnListFileReader
                .YieldSolutionFilePaths($"{GlobalContext.RootDir}\\Input\\5\\build-solutions.yml")
                .ForEach(GlobalContext.MSBuildExe.RestoreNuGetPackages);
        }

        [TearDown]
        public void TearDown()
        {
            GlobalContext.CleanOutputDir();
        }

        [Test]
        public void Generate()
        {
            const string SLN_LIST_FILE_PATH = "5\\build-solutions.yml";
            const string PROJECT_FILE_PATH = "5\\src\\Gateway\\Gateway\\Gateway.csproj";
            const string REPORT_FILE_NAME = "NuGetUsageReport-Gateway.json";

            var expectedDir = GlobalContext.RootDir + "\\Expected\\5\\Gateway\\";

            var args = new List<string>
            {
                "-s",
                GlobalContext.RootDir + "\\Input\\" + SLN_LIST_FILE_PATH,
                "-f",
                GlobalContext.RootDir + "\\Input\\" + PROJECT_FILE_PATH,
                "-u",
                GlobalContext.OutputDir,
                "-v:" + GlobalContext.OutputDir
            };

            Assert.Zero(Program.Main(args.ToArray()));
            Assert.IsNotNull(Program.LogFilePath, "Actual verbose log file not found.");

            if (s_updateExpectedResults)
            {
                Directory.CreateDirectory(expectedDir);
                File.Move(GlobalContext.OutputDir + REPORT_FILE_NAME, expectedDir + REPORT_FILE_NAME, true);
            }
            else
            {
                FileAssert.AreEqual(expectedDir + REPORT_FILE_NAME, GlobalContext.OutputDir + REPORT_FILE_NAME, "Report files do not match");
            }
            File.Move(Program.LogFilePath, expectedDir + "Verbose.log", true);
        }
    }
}