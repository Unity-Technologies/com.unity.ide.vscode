using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace com.unity.ide.vscode.tests
{
    public static class XMLUtilities
    {
        public static void AssertCompileItemsMatchExactly(XmlDocument projectXml, IEnumerable<string> expectedCompileItems)
        {
            var compileItems = projectXml.SelectAttributeValues("/msb:Project/msb:ItemGroup/msb:Compile/@Include").ToArray();
            CollectionAssert.AreEquivalent(RelativeAssetPathsFor(expectedCompileItems), compileItems);
        }

        public static void AssertNonCompileItemsMatchExactly(XmlDocument projectXml, IEnumerable<string> expectedNoncompileItems)
        {
            var nonCompileItems = projectXml.SelectAttributeValues("/msb:Project/msb:ItemGroup/msb:None/@Include").ToArray();
            CollectionAssert.AreEquivalent(RelativeAssetPathsFor(expectedNoncompileItems), nonCompileItems);
        }

        static XmlNamespaceManager GetModifiedXmlNamespaceManager(XmlDocument projectXml)
        {
            var xmlNamespaces = new XmlNamespaceManager(projectXml.NameTable);
            xmlNamespaces.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003");
            return xmlNamespaces;
        }

        static IEnumerable<string> RelativeAssetPathsFor(IEnumerable<string> fileNames)
        {
            return fileNames.Select(fileName => fileName.Replace('/', '\\')).ToArray();
        }

        static IEnumerable<string> SelectAttributeValues(this XmlDocument xmlDocument, string xpathQuery)
        {
            var result = xmlDocument.SelectNodes(xpathQuery, GetModifiedXmlNamespaceManager(xmlDocument));
            foreach (XmlAttribute attribute in result)
                yield return attribute.Value;
        }

        public static XmlDocument FromText(string textContent)
        {
            var xmlDocument = new XmlDocument();
            xmlDocument.LoadXml(textContent);
            return xmlDocument;
        }

        public static string GetInnerText(XmlDocument xmlDocument, string xpathQuery)
        {
            return xmlDocument.SelectSingleNode(xpathQuery, GetModifiedXmlNamespaceManager(xmlDocument)).InnerText;
        }
    }
}
