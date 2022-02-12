using Dayforce.CSharp.ProjectAssets;
using LibGit2Sharp;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace GenerateBindingRedirects
{
    public class BindingRedirectsWriter
    {
        [Flags]
        private enum ActualAppConfigStatus
        {
            Normal = 0,
            NotSpecified = 1,
            FileNotFound = 2,
            Linked = 4
        }

        private delegate bool IsInAssertModeDelegate();

        private const string ASSEMBLY_BINDING_XMLNS = "urn:schemas-microsoft-com:asm.v1";
        private static readonly XmlWriterSettings s_xmlWriterSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false)
        };

        private readonly ProjectContext m_pc;
        private string ExpectedConfigFilePath => m_pc.ExpectedConfigFilePath;
        private string ActualConfigFilePath => m_pc.ActualConfigFilePath;
        private string ProjectFilePath => m_pc.ProjectFilePath;

        public BindingRedirectsWriter(ProjectContext pc)
        {
            m_pc = pc;
        }

        public void WriteBindingRedirects(string bindingRedirects, bool assert, bool forceAssert)
        {
            var actualAppConfigStatus = ActualAppConfigStatus.Normal;
            if (ExpectedConfigFilePath.EndsWith("app.config", C.IGNORE_CASE))
            {
                if (ActualConfigFilePath == null)
                {
                    actualAppConfigStatus |= ActualAppConfigStatus.NotSpecified;
                }
                else
                {
                    if (!File.Exists(ActualConfigFilePath))
                    {
                        actualAppConfigStatus |= ActualAppConfigStatus.FileNotFound;
                    }
                    if (!ActualConfigFilePath.Equals(ExpectedConfigFilePath, C.IGNORE_CASE))
                    {
                        actualAppConfigStatus |= ActualAppConfigStatus.Linked;
                        if ((actualAppConfigStatus & ActualAppConfigStatus.FileNotFound) == 0)
                        {
                            File.Copy(ActualConfigFilePath, ExpectedConfigFilePath, true);
                        }
                    }
                }
            }

            if (!WriteConfigFile(bindingRedirects, IsInAssertMode) ||
                actualAppConfigStatus == ActualAppConfigStatus.Normal ||
                actualAppConfigStatus == ActualAppConfigStatus.FileNotFound)
            {
                return;
            }

            if (!m_pc.SDKStyle)
            {
                AddAppConfigToProjectFile(actualAppConfigStatus);
            }

            bool IsInAssertMode()
            {
                if (forceAssert)
                {
                    return true;
                }

                if (!assert)
                {
                    if (!File.Exists(ExpectedConfigFilePath))
                    {
                        UpdateGitIgnore();
                    }
                    return false;
                }

                var res = IsTrackedByGit();
                if (res)
                {
                    forceAssert = true;
                }
                else
                {
                    assert = false;
                }
                return forceAssert;
            }

            bool IsTrackedByGit()
            {
                var wsPath = ProjectFilePath;
                while (wsPath.Length > 3 && !Directory.Exists(wsPath + "\\.git"))
                {
                    wsPath = Path.GetDirectoryName(wsPath);
                }
                if (wsPath.Length <= 3)
                {
                    return true;
                }
                var repo = new Repository(wsPath);
                var objectish = "HEAD:" + ExpectedConfigFilePath[(wsPath.Length + 1)..].Replace('\\', '/');
                return repo.Lookup(objectish, ObjectType.Blob) != null;
            }

            void UpdateGitIgnore()
            {
                var gitIgnoreFilePath = ExpectedConfigFilePath + "\\..\\.gitignore";
                if (File.Exists(gitIgnoreFilePath))
                {
                    var gitIgnoreLines = File.ReadAllLines(gitIgnoreFilePath);
                    if (!gitIgnoreLines.Contains("app.config", C.IgnoreCase))
                    {
                        File.AppendAllText(gitIgnoreFilePath, "app.config\r\n");
                    }
                }
                else
                {
                    File.WriteAllText(gitIgnoreFilePath, "app.config\r\n");
                }
            }
        }

        private void AddAppConfigToProjectFile(ActualAppConfigStatus actualAppConfigStatus)
        {
            var (doc, nsmgr) = GetProjectXmlDocument(ProjectFilePath);
            var node = doc.CreateElement("None", doc.DocumentElement.Attributes["xmlns"].Value);
            var attr = doc.CreateAttribute("Include");
            attr.Value = "app.config";
            node.Attributes.Append(attr);
            if (actualAppConfigStatus == ActualAppConfigStatus.NotSpecified)
            {
                var itemGroup = doc.SelectSingleNode("/p:Project/p:ItemGroup[count(./*) > 0]", nsmgr);
                itemGroup.AppendChild(doc.CreateWhitespace("  "));
                itemGroup.AppendChild(node);
                itemGroup.AppendChild(doc.CreateWhitespace(Environment.NewLine + "  "));
            }
            else
            {
                var oldAttr = ProjectContext.LocateAppConfigInProjectXml(doc.CreateNavigator(), nsmgr);
                oldAttr.MoveToParent();
                var oldNode = (XmlNode)oldAttr.UnderlyingObject;
                oldNode.ParentNode.ReplaceChild(node, oldNode);
            }
            doc.Save(ProjectFilePath);
        }

        private bool WriteConfigFile(string bindingRedirects, IsInAssertModeDelegate isInAssertMode)
        {
            if (!File.Exists(ExpectedConfigFilePath))
            {
                if (string.IsNullOrEmpty(bindingRedirects))
                {
                    return false;
                }
                if (isInAssertMode())
                {
                    throw new ApplicationException($"{ExpectedConfigFilePath} is expected to have some assembly binding redirects, but it does not exist.");
                }
                WriteNewConfigFile(bindingRedirects, isInAssertMode);
                return true;
            }

            var doc = new XmlDocument
            {
                PreserveWhitespace = true
            };
            doc.Load(ExpectedConfigFilePath);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("b", ASSEMBLY_BINDING_XMLNS);
            var assemblyBinding = doc.SelectSingleNode("/configuration/runtime/b:assemblyBinding", nsmgr);
            if (assemblyBinding == null)
            {
                if (bindingRedirects.Length == 0)
                {
                    return false;
                }
                if (isInAssertMode())
                {
                    throw new ApplicationException($"{ExpectedConfigFilePath} is expected to have some assembly binding redirects, but it has none.");
                }

                var cfg = doc.SelectSingleNode("/configuration");
                if (cfg == null)
                {
                    WriteNewConfigFile(bindingRedirects, isInAssertMode);
                    return true;
                }

                var runtime = cfg.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.LocalName == "runtime");
                if (runtime == null)
                {
                    cfg.AppendChild(doc.CreateWhitespace("  "));
                    runtime = cfg.AppendChild(doc.CreateElement("runtime"));
                    runtime.AppendChild(doc.CreateWhitespace(Environment.NewLine + "  "));
                    cfg.AppendChild(doc.CreateWhitespace(Environment.NewLine));
                }
                assemblyBinding = runtime.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.LocalName == "assemblyBinding");
                if (assemblyBinding == null)
                {
                    runtime.AppendChild(doc.CreateWhitespace("  "));
                    assemblyBinding = runtime.AppendChild(doc.CreateElement("assemblyBinding", ASSEMBLY_BINDING_XMLNS));
                    var attr = doc.CreateAttribute("xmlns", "http://www.w3.org/2000/xmlns/");
                    attr.Value = ASSEMBLY_BINDING_XMLNS;
                    assemblyBinding.Attributes.Append(attr);
                    runtime.AppendChild(doc.CreateWhitespace(Environment.NewLine + "  "));
                }
            }

            var newInnerXml = Environment.NewLine +
                bindingRedirects +
                Environment.NewLine + "    ";
            var curInnerXml = assemblyBinding.OuterXml
                .Replace(@"<assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">", "")
                .Replace(@"</assemblyBinding>", "");

            if (isInAssertMode())
            {
                if (curInnerXml == newInnerXml)
                {
                    return false;
                }
                throw new ApplicationException($"{ExpectedConfigFilePath} does not have the expected set of binding redirects.");
            }

            if (curInnerXml != newInnerXml)
            {
                assemblyBinding.InnerXml = newInnerXml;
                using var writer = XmlWriter.Create(ExpectedConfigFilePath, s_xmlWriterSettings);
                doc.Save(writer);
            }

            return true;
        }

        private void WriteNewConfigFile(string bindingRedirects, IsInAssertModeDelegate isInAssertMode)
        {
            if (bindingRedirects.Length == 0)
            {
                return;
            }
            if (isInAssertMode())
            {
                throw new ApplicationException($"{ExpectedConfigFilePath} is expected to have some assembly binding redirects, but it is empty or does not exist.");
            }

            const string newConfigFileFormat = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <runtime>
    <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
{0}
    </assemblyBinding>
  </runtime>
</configuration>
";
            File.WriteAllText(ExpectedConfigFilePath, string.Format(newConfigFileFormat, bindingRedirects));
        }

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
        private static (XmlDocument, XmlNamespaceManager) GetProjectXmlDocument(string projectFile)
        {
            var doc = new XmlDocument
            {
                PreserveWhitespace = true
            };
            doc.Load(projectFile);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            var ns = doc.DocumentElement.Attributes["xmlns"].Value;
            nsmgr.AddNamespace("p", ns);
            return (doc, nsmgr);
        }
    }
}
