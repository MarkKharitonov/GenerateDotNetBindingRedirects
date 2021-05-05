using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenerateBindingRedirects;
using NUnit.Framework;

namespace GenerateBindingRedirectsTests
{
    [TestFixture]
    public class Tests
    {
        private static readonly bool s_updateExpectedResults = false;

        public static IEnumerable<TestCaseData> AllTestCases => (new (string, bool, bool)[]
        {
            ("1\\DataSvc", false, false),
            ("1\\BJE", true, false), 
            ("1\\ReportingSvc", false, false),
            ("1\\CommonTests", false, true),
            ("1\\RuleEngineTests", false, false),
            ("1\\UnitTests", true, true),
            ("2\\CandidatePortal", false, false),
            ("2\\MyDayforce", false, false),
            ("3\\Api", false, false),
            ("3\\OData", false, false),
            ("4\\Clock", false, false)
        }).Select(path => CreateTestCase(path));

        public static TestCaseData[] AFewTestCases => new[]
        {
            CreateTestCase(("1\\DataSvc", false, false)),
            CreateTestCase(("1\\CommonTests", false, true))
        };

        public static TestCaseData[] ATestCase => new[]
        {
            CreateTestCase(("1\\DataSvc", false, false))
        };

        public static TestCaseData[] NewConfigFileTestCase => new[]
        {
            CreateTestCase(("1\\UnitTests", true, true))
        };

        private static TestCaseData CreateTestCase((string Path, bool NewAppConfig, bool ModifiesProjectFile) a)
        {
            var outputDir = Path.GetTempFileName();
            File.Delete(outputDir);
            Directory.CreateDirectory(outputDir);

            return new TestCaseData(
                $"{GlobalContext.RootDir}\\Input\\{a.Path}\\{Path.GetFileName(a.Path)}.csproj",
                $"{GlobalContext.RootDir}\\Expected\\{a.Path}",
                outputDir,
                a.NewAppConfig,
                a.ModifiesProjectFile)
            {
                TestName = $"{{m}}({a.Path})"
            };
        }

        private static string GetConfigFile(string projectFilePath, bool newAppConfig = false)
        {
            var configFile = Path.GetFullPath($"{projectFilePath}\\..\\{(projectFilePath.EndsWith("BJE.csproj") || projectFilePath.EndsWith("Tests.csproj") ? "app" : "web")}.config");
            if (newAppConfig)
            {
                FileAssert.DoesNotExist(configFile, "The config file is not supposed to exist before the test.");
            }
            else if (!s_updateExpectedResults)
            {
                FileAssert.Exists(configFile, "Failed to locate the config file.");
            }
            return configFile;
        }

        [SetUp]
        public void Setup()
        {
            bool modifiesProjectFile = (bool)TestContext.CurrentContext.Test.Arguments[4];
            if (modifiesProjectFile)
            {
                var projectFile = (string)TestContext.CurrentContext.Test.Arguments[0];
                string projectFileBackup = $"{projectFile}.backup";
                if (!File.Exists(projectFileBackup))
                {
                    File.Copy(projectFile, projectFileBackup);
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            var outputDir = (string)TestContext.CurrentContext.Test.Arguments[2];
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }

            bool modifiesProjectFile = (bool)TestContext.CurrentContext.Test.Arguments[4];
            if (modifiesProjectFile)
            {
                var projectFile = (string)TestContext.CurrentContext.Test.Arguments[0];
                string projectFileBackup = $"{projectFile}.backup";
                if (File.Exists(projectFileBackup))
                {
                    File.Move(projectFileBackup, projectFile, true);
                }
            }

            bool newAppConfig = (bool)TestContext.CurrentContext.Test.Arguments[3];
            if (newAppConfig)
            {
                var projectFile = (string)TestContext.CurrentContext.Test.Arguments[0];
                var appConfigFile = $"{projectFile}\\..\\app.config";
                File.Delete(appConfigFile);
            }
        }

        [TestCaseSource(nameof(AllTestCases))]
        public void Generate(string projectFilePath, string expectedDir, string outputDir, bool newAppConfig, bool _2)
        {
            var actualTargetFilesFilePath = $"{outputDir}\\TargetFiles.txt";
            FileAssert.DoesNotExist(actualTargetFilesFilePath);

            var actualBindingRedirectsFilePath = $"{outputDir}\\BindingRedirects.txt";
            FileAssert.DoesNotExist(actualBindingRedirectsFilePath);
            string configFile = GetConfigFile(projectFilePath, newAppConfig);
            var expectedConfigFileTimestamp = s_updateExpectedResults || newAppConfig ? default : File.GetLastWriteTimeUtc(configFile);

            var args = new List<string>
            {
                "--test",
                "--projectFile",
                projectFilePath,
                "--targetFiles",
                actualTargetFilesFilePath,
                "--bindingRedirects",
                actualBindingRedirectsFilePath,
                "--solutions",
                $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                "--writeBindingRedirects",
                $"-v:{outputDir}"
            };

            if (projectFilePath.EndsWith("Tests.csproj"))
            {
                args.Add("--privateProbingPath=bin");
            }

            Assert.Zero(Program.Main(args.ToArray()));
            Assert.IsNotNull(Log.LogFilePath, "Actual verbose log file not found.");

            if (s_updateExpectedResults)
            {
                Directory.CreateDirectory(expectedDir);
                File.Move(actualTargetFilesFilePath, $"{expectedDir}\\TargetFiles.txt", true);
                File.Move(actualBindingRedirectsFilePath, $"{expectedDir}\\BindingRedirects.txt", true);
                File.Move(Log.LogFilePath, $"{expectedDir}\\Verbose.log", true);
            }
            else
            {
                FileAssert.AreEqual($"{expectedDir}\\TargetFiles.txt", actualTargetFilesFilePath, "Target files do not match");
                FileAssert.AreEqual($"{expectedDir}\\BindingRedirects.txt", actualBindingRedirectsFilePath, "Binding Redirects do not match");
                if (newAppConfig)
                {
                    FileAssert.Exists(configFile, $"The config file {configFile} was not created.");
                }
                else
                {
                    Assert.AreEqual(expectedConfigFileTimestamp, File.GetLastWriteTimeUtc(configFile), $"The config file {configFile} was modified.");
                }
            }
        }

        [TestCaseSource(nameof(AFewTestCases))]
        public void GenerateCreateNewConfigFile(string projectFilePath, string _, string outputDir, bool _1, bool _2)
        {
            if (s_updateExpectedResults)
            {
                Assert.Ignore($"The test is irrelevant when updating the expected results.");
                return;
            }

            var expectedConfigFile = $"{outputDir}\\Config.xml";
            var actualConfigFile = GetConfigFile(projectFilePath);
            File.Move(actualConfigFile, expectedConfigFile);
            FileAssert.DoesNotExist(actualConfigFile);

            try
            {
                Program.Run(
                    projectFilePath,
                    $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                    null, null, true, projectFilePath.EndsWith("Tests.csproj") ? "bin" : null);

                FileAssert.AreEqual(expectedConfigFile, actualConfigFile);
            }
            finally
            {
                File.Move(expectedConfigFile, actualConfigFile, true);
            }
        }

        [TestCaseSource(nameof(AFewTestCases))]
        public void GenerateOverwriteMismatchingConfigFileNoRuntimeElement(string projectFilePath, string _, string outputDir, bool _1, bool _2)
        {
            GenerateOverwriteMismatchingConfigFile(projectFilePath, outputDir, "runtime");
        }

        [TestCaseSource(nameof(AFewTestCases))]
        public void GenerateOverwriteMismatchingConfigFileNoAssemblyBindingElement(string projectFilePath, string _, string outputDir, bool _1, bool _2)
        {
            GenerateOverwriteMismatchingConfigFile(projectFilePath, outputDir, "assemblyBinding");
        }

        [TestCaseSource(nameof(AFewTestCases))]
        public void GenerateOverwriteMismatchingConfigFileNoDependentAssemblyElement(string projectFilePath, string _, string outputDir, bool _1, bool _2)
        {
            GenerateOverwriteMismatchingConfigFile(projectFilePath, outputDir, "dependentAssembly");
        }

        private static void GenerateOverwriteMismatchingConfigFile(string projectFilePath, string outputDir, string elementName)
        {
            if (s_updateExpectedResults)
            {
                Assert.Ignore($"The test is irrelevant when updating the expected results.");
                return;
            }

            var openTag = "<" + elementName;
            var closeTag = "</" + elementName + ">";

            var expectedConfigFile = $"{outputDir}\\Config.xml";
            var actualConfigFile = GetConfigFile(projectFilePath);
            File.Move(actualConfigFile, expectedConfigFile);
            var skip = false;
            File.WriteAllLines(actualConfigFile, File.ReadLines(expectedConfigFile).Where(line =>
            {
                if (skip)
                {
                    skip = !line.Contains(closeTag);
                    return false;
                }
                skip = line.Contains(openTag);
                return !skip;
            }));
            FileAssert.AreNotEqual(expectedConfigFile, actualConfigFile);

            try
            {
                Program.Run(
                    projectFilePath,
                    $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                    null, null, true, projectFilePath.EndsWith("Tests.csproj") ? "bin" : null);

                FileAssert.AreEqual(expectedConfigFile, actualConfigFile);
            }
            finally
            {
                File.Move(expectedConfigFile, actualConfigFile, true);
            }
        }

        [TestCaseSource(nameof(AFewTestCases))]
        public void AssertPass(string projectFilePath, string expectedDir, string outputDir, bool _1, bool _2)
        {
            if (s_updateExpectedResults)
            {
                Assert.Ignore($"The test is irrelevant when updating the expected results.");
                return;
            }

            var actualTargetFilesFilePath = $"{outputDir}\\TargetFiles.txt";
            var bindingRedirectsFilePath = $"{expectedDir}\\BindingRedirects.txt";
            var expectedBindingRedirectsFileTimestamp = File.GetLastWriteTimeUtc(bindingRedirectsFilePath);
            var configFile = GetConfigFile(projectFilePath);
            var expectedConfigFileTimestamp = File.GetLastWriteTimeUtc(configFile);

            Program.Run(
                projectFilePath,
                $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                actualTargetFilesFilePath,
                bindingRedirectsFilePath,
                true, projectFilePath.EndsWith("Tests.csproj") ? "bin" : null, true, true);

            FileAssert.AreEqual($"{expectedDir}\\TargetFiles.txt", actualTargetFilesFilePath, "Target files do not match");
            Assert.AreEqual(expectedBindingRedirectsFileTimestamp, File.GetLastWriteTimeUtc(bindingRedirectsFilePath), $"The binding redirects file {bindingRedirectsFilePath} was modified.");
            Assert.AreEqual(expectedConfigFileTimestamp, File.GetLastWriteTimeUtc(configFile), $"The config file {configFile} was modified.");
        }

        [TestCaseSource(nameof(ATestCase))]
        public void AssertFailNoBindingRedirectsTxtFile(string projectFilePath, string _, string outputDir, bool _1, bool _2)
        {
            if (s_updateExpectedResults)
            {
                Assert.Ignore($"The test is irrelevant when updating the expected results.");
                return;
            }

            var nonExistingBindingRedirectsFilePath = $"{outputDir}\\BindingRedirects.txt";
            FileAssert.DoesNotExist(nonExistingBindingRedirectsFilePath);

            var exc = Assert.Throws<ApplicationException>(() => Program.Run(
                projectFilePath,
                $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                null,
                nonExistingBindingRedirectsFilePath,
                false, null, true));

            Assert.AreEqual($"Found some binding redirects, but {nonExistingBindingRedirectsFilePath} does not exist.", exc.Message);
        }

        [TestCaseSource(nameof(ATestCase))]
        public void AssertFailMismatchingBindingRedirectsInTxtFile(string projectFilePath, string expectedDir, string outputDir, bool _1, bool _2)
        {
            if (s_updateExpectedResults)
            {
                Assert.Ignore($"The test is irrelevant when updating the expected results.");
                return;
            }

            var mismatchingBindingRedirectsFilePath = $"{outputDir}\\BindingRedirects.txt";
            string expectedBindingRedirectsFilePath = $"{expectedDir}\\BindingRedirects.txt";
            File.WriteAllLines(mismatchingBindingRedirectsFilePath, File.ReadAllLines(expectedBindingRedirectsFilePath).Skip(4));
            FileAssert.AreNotEqual(expectedBindingRedirectsFilePath, mismatchingBindingRedirectsFilePath);

            var exc = Assert.Throws<ApplicationException>(() => Program.Run(
                projectFilePath,
                $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                null,
                mismatchingBindingRedirectsFilePath,
                false, null, true));

            Assert.AreEqual($"Actual binding redirects in {mismatchingBindingRedirectsFilePath} do not match the expectation.", exc.Message);
        }

        [TestCaseSource(nameof(NewConfigFileTestCase))]
        public void AssertFailNoConfigFile(string projectFilePath, string _, string _1, bool _2, bool _3)
        {
            if (s_updateExpectedResults)
            {
                Assert.Ignore($"The test is irrelevant when updating the expected results.");
                return;
            }

            var configFilePath = Path.GetFullPath($"{projectFilePath}\\..\\app.config");
            FileAssert.DoesNotExist(configFilePath);

            var exc = Assert.Throws<ApplicationException>(() => Program.Run(
                projectFilePath,
                $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                null,
                null,
                false, null, true));

            Assert.AreEqual($"{configFilePath} is expected to have some assembly binding redirects, but it does not exist.", exc.Message);
        }

        [TestCaseSource(nameof(NewConfigFileTestCase))]
        public void AssertFailMismatchingBindingRedirectsInConfigFile(string projectFilePath, string _, string _1, bool _2, bool _3)
        {
            if (s_updateExpectedResults)
            {
                Assert.Ignore($"The test is irrelevant when updating the expected results.");
                return;
            }

            var configFilePath = Path.GetFullPath($"{projectFilePath}\\..\\app.config");
            FileAssert.DoesNotExist(configFilePath);
            File.Copy($"{projectFilePath}\\..\\MismatchingApp.config", configFilePath);

            var exc = Assert.Throws<ApplicationException>(() => Program.Run(
                projectFilePath,
                $"{GlobalContext.RootDir}\\Input\\Solutions.txt",
                null,
                null,
                false, null, true));

            Assert.AreEqual($"{configFilePath} does not have the expected set of binding redirects.", exc.Message);
        }
    }
}