using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;

namespace Dayforce.CSharp.ProjectAssets
{
    public class ProjectContext
    {
        internal static int Count;

        private readonly SolutionsContext m_sc;
        public readonly int Index;
        public readonly string Solution;
        public readonly string ProjectFilePath;
        public readonly string AssemblyName;
        public readonly List<string> DllReferences;
        public readonly string ExpectedConfigFilePath;
        public readonly string ActualConfigFilePath;
        public readonly string ProjectName;
        public readonly string RelativeProjectFilePath;
        private readonly IReadOnlyList<string> m_referencedProjectNames;
        private readonly Lazy<IReadOnlyList<ProjectContext>> m_referencedProjects;

        public IReadOnlyList<ProjectContext> ReferencedProjects => m_referencedProjects.Value;

        public static ProjectContext Create(SolutionsContext sc, string solution, string projectFilePath)
        {
            try
            {
                var (nav, nsmgr) = GetProjectXPathNavigator(projectFilePath);
                var assemblyName =
                    nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:AssemblyName/text()", nsmgr)?.Value ??
                    Path.GetFileNameWithoutExtension(projectFilePath);

                var projectTypeGuids = nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:ProjectTypeGuids/text()", nsmgr)?.Value;
                if (projectTypeGuids?.Contains("{A1591282-1198-4647-A2B1-27E5FF5F6F3B}", C.IGNORE_CASE) == true)
                {
                    // Silverlight
                    return null;
                }

                var dllReferences = nav.Select("/p:Project/p:ItemGroup/p:Reference/@Include", nsmgr)
                    .Cast<XPathNavigator>()
                    .Select(o => o.Value)
                    .Where(o => o != "System" &&
                        !o.StartsWith("System.") &&
                        !o.StartsWith("Microsoft.") &&
                        !o.Contains(","))
                    .Select(o => o.IsExecutable() ? o.Substring(0, o.Length - 4) : o)
                    .ToList();

                var projectReferences = nav.Select("/p:Project/p:ItemGroup/p:ProjectReference/@Include", nsmgr)
                    .Cast<XPathNavigator>()
                    .Select(o => Path.GetFileNameWithoutExtension(o.Value))
                    .ToList();

                bool isWebApplication = projectTypeGuids?.Contains("{349c5851-65df-11da-9384-00065b846f21}", C.IGNORE_CASE) == true;
                var configFileName = isWebApplication ? "web.config" : "app.config";
                var expectedConfigFilePath = Path.GetFullPath($"{projectFilePath}\\..\\{configFileName}");
                string actualConfigFilePath = null;

                if (!isWebApplication)
                {
                    var attr = LocateAppConfigInProjectXml(nav, nsmgr);
                    if (attr != null)
                    {
                        actualConfigFilePath = Path.GetFullPath(projectFilePath + "\\..\\" + attr.Value);
                    }
                }
                return new ProjectContext(sc, solution, projectFilePath, assemblyName, dllReferences, projectReferences, expectedConfigFilePath, actualConfigFilePath);
            }
            catch (Exception exc)
            {
                throw new ApplicationException("Failed to process " + projectFilePath, exc);
            }
        }

        public static XPathNavigator LocateAppConfigInProjectXml(XPathNavigator nav, XmlNamespaceManager nsmgr) =>
            nav.Select("/p:Project/p:ItemGroup/p:None[contains(translate(@Include, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'),'app.config')]/@Include", nsmgr)
            .Cast<XPathNavigator>()
            .FirstOrDefault(o => o.Value.Equals("app.config", C.IGNORE_CASE) || o.Value.EndsWith("\\app.config", C.IGNORE_CASE));

        private ProjectContext(SolutionsContext sc, string solution, string projectFilePath, string assemblyName, List<string> dllReferences, List<string> projectReferences,
            string expectedConfigFilePath, string actualConfigFilePath)
        {
            m_sc = sc;
            Index = Count++;
            Solution = solution;
            ProjectFilePath = projectFilePath;
            AssemblyName = assemblyName;
            DllReferences = dllReferences;
            m_referencedProjectNames = projectReferences;
            ExpectedConfigFilePath = expectedConfigFilePath;
            ActualConfigFilePath = actualConfigFilePath;
            ProjectName = Path.GetFileNameWithoutExtension(projectFilePath);
            RelativeProjectFilePath = Log.Instance.GetRelativeFilePath(ProjectFilePath);

            Log.Instance.WriteVerbose("ProjectContext({0}) : {1}", RelativeProjectFilePath, AssemblyName);

            m_referencedProjects = new Lazy<IReadOnlyList<ProjectContext>>(() => m_referencedProjectNames.Select(projectName => m_sc.GetProjectByName(Solution, projectName)).ToList());
        }

        public override string ToString() => $"{AssemblyName} ({ProjectName} @ {Solution})";

        private static (XPathNavigator, XmlNamespaceManager) GetProjectXPathNavigator(string projectFile)
        {
            var doc = new XPathDocument(projectFile);
            var nav = doc.CreateNavigator();
            var nsmgr = new XmlNamespaceManager(nav.NameTable);
            nav.MoveToFollowing(XPathNodeType.Element);
            var ns = nav.GetNamespacesInScope(XmlNamespaceScope.Local).FirstOrDefault();
            var nsValue = ns.Value ?? "";
            nsmgr.AddNamespace("p", nsValue);
            return (nav, nsmgr);
        }
    }
}
