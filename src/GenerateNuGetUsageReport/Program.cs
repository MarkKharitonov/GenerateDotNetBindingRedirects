using Dayforce.CSharp.ProjectAssets;
using Mono.Options;
using System;
using System.IO;

namespace GenerateNuGetUsageReport
{
    public class Program
    {
        public static string LogFilePath { get; private set; }

        public static int Main(string[] args)
        {
            var verbose = false;
            string logPath = VerboseLog.DefaultLogDirectory;
            string projectFilePath = null;
            string solutionsListFile = null;
            var help = false;
            string nuGetUsageReport = null;
            var options = new OptionSet()
                .Add("h|help|?", "Show help", _ => help = true)
                .Add("v|verbose:", $"Produces verbose output. May be given a custom directory path where to collect extended information. Defaults to {logPath}", v => { logPath = v ?? logPath; verbose = true; })
                .Add("f|projectFile=", "[Required] The project file.", v => projectFilePath = v)
                .Add("s|solutions=", "[Required] A file listing all the relevant solutions.", v => solutionsListFile = v)
                .Add("u|nuGetUsageReport=", "[Required] Generate a report listing all the nuget packages on which the given project depends and save it under the given file path.", v => nuGetUsageReport = v)
            ;

            var extraArgs = options.Parse(args);

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
            if (nuGetUsageReport == null)
            {
                LogErrorMessage($"--nuGetUsageReport is required.");
                return 2;
            }

            try
            {
                if (verbose)
                {
                    var baseDir = Path.GetFullPath(solutionsListFile + "\\..");
                    VerboseLog verboseLog;
                    Log.Instance = verboseLog = new VerboseLog("GenerateNuGetUsageReport", logPath, baseDir, projectFilePath, false);
                    LogFilePath = verboseLog.LogFilePath;
                }
                var sc = new SolutionsContext(solutionsListFile, new SimpleSolutionsListFileReader());
                var focus = sc.GetProjectContext(projectFilePath);
                if (focus == null)
                {
                    throw new ApplicationException($"The project {projectFilePath} cannot be processed, because it does not seem to exist in any solution.");
                }

                var projectAssets = new ProjectAssets(sc, focus);

                if (projectAssets.PackageFolders == null)
                {
                    throw new ApplicationException($"No project.assets.json is associated with {projectFilePath} and {solutionsListFile}.");
                }

                projectAssets.GenerateNuGetUsageReport(focus.ProjectName, nuGetUsageReport);
            }
            catch (ApplicationException exc)
            {
                LogErrorMessage(exc.Message);
                Log.Instance.WriteVerbose(exc);
                return 3;
            }
            catch (Exception exc)
            {
                LogErrorMessage(exc.ToString());
                LogErrorMessage(exc.Message);
                Log.Instance.WriteVerbose(exc);
                return 3;
            }
            finally
            {
                using (Log.Instance as IDisposable) { }
            }
            return 0;
        }
        private static void LogErrorMessage(string msg) => Console.WriteLine("GenerateNuGetUsageReport: ERROR: " + msg);
    }
}
