using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Xml.Serialization;

namespace MissionPlanner.Utilities.Tests
{
    /// <summary>
    /// ThemeManager.ReadThemeFile must read every theme exactly as XmlSerializer did.
    /// </summary>
    [TestClass]
    public class ThemeFileTests
    {
        private static string tempdir;

        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            tempdir = Path.Combine(Path.GetTempPath(), "mptheme" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempdir);
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            try
            {
                Directory.Delete(tempdir, true);
            }
            catch
            {
            }
        }

        // the old reader
        private static ThemeColorTable ReadWithSerializer(string file)
        {
            try
            {
                using (var reader = new StreamReader(file))
                    return (ThemeColorTable) new XmlSerializer(typeof(ThemeColorTable)).Deserialize(reader);
            }
            catch
            {
                return null;
            }
        }

        private static void AssertSame(string file)
        {
            var expected = ReadWithSerializer(file);
            var actual = ThemeManager.ReadThemeFile(file);

            if (expected == null)
            {
                Assert.IsNull(actual, file);
                return;
            }

            Assert.IsNotNull(actual, file);
            Assert.AreEqual(expected.strThemeName, actual.strThemeName, file);
            Assert.AreEqual(expected.iconSet, actual.iconSet, file);
            Assert.AreEqual(expected.terminalTheming, actual.terminalTheming, file);
            Assert.AreEqual(expected.colors.Count, actual.colors.Count, file);
            for (int i = 0; i < expected.colors.Count; i++)
            {
                var e = expected.colors[i];
                var a = actual.colors[i];
                if (e == null)
                {
                    Assert.IsNull(a, file + " " + i);
                    continue;
                }

                Assert.IsNotNull(a, file + " " + i);
                Assert.AreEqual(e.strColorItemName, a.strColorItemName, file + " " + i);
                Assert.AreEqual(e.strVariableName, a.strVariableName, file + " " + i);
                // Color.Equals compares the name and known-color state too, not just ARGB
                Assert.AreEqual(e.clrColor, a.clrColor, file + " " + e.strColorItemName);
                Assert.AreEqual(e.clrColor.IsNamedColor, a.clrColor.IsNamedColor, file + " " + e.strColorItemName);
            }
        }

        private static string WriteTemp(string name, string xml)
        {
            var file = Path.Combine(tempdir, name);
            File.WriteAllText(file, xml);
            return file;
        }

        private static string FindRepoFile(string name)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var file = Path.Combine(dir.FullName, name);
                if (File.Exists(file) && File.Exists(Path.Combine(dir.FullName, "MissionPlanner.csproj")))
                    return file;
                dir = dir.Parent;
            }

            Assert.Inconclusive("cannot find " + name);
            return null;
        }

        [TestMethod]
        public void ShippedThemesMatchSerializer()
        {
            var repo = Path.GetDirectoryName(FindRepoFile("BurntKermit.mpsystheme"));
            var themes = Directory.GetFiles(repo, "*.mpsystheme");
            Assert.IsTrue(themes.Length >= 2);
            foreach (var theme in themes)
            {
                Assert.IsNotNull(ThemeManager.ReadThemeFile(theme), theme);
                AssertSame(theme);
            }
        }

        [TestMethod]
        public void SavedThemesMatchSerializer()
        {
            // what the theme editor writes for a user's custom theme
            var table = new ThemeColorTable();
            table.InitColors();
            table.iconSet = ThemeColorTable.IconSet.HighContrastIconSet;
            table.terminalTheming = false;
            table.colors.Add("translucent", Color.FromArgb(10, 20, 30, 40), "ColorMouseOver");
            table.colors.Add("translucent named", Color.FromArgb(128, Color.Red), "ColorMouseDown");
            table.colors.Add("system", SystemColors.Control, "BGColor");
            table.colors.Add("empty", Color.Empty, "TextColor");
            table.colors.Add("transparent", Color.Transparent, "TextColor");
            table.colors.Add(null, Color.Black, null);
            var file = Path.Combine(tempdir, "custom.mpusertheme");
            ThemeManager.WriteToXmlFile(file, table);
            AssertSame(file);
        }

        [TestMethod]
        public void HandWrittenThemesMatchSerializer()
        {
            const string head = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                                "<ThemeColorTable xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\n";
            var cases = new[]
            {
                // Alpha before Web, Alpha alone, Web alone, no clrColor, bad colour name
                head + "<colors><ThemeColor><strColorItemName>a</strColorItemName><clrColor Alpha=\"73\" Web=\"#2B3A03\" /><strVariableName>X</strVariableName></ThemeColor>" +
                "<ThemeColor><clrColor Alpha=\"10\" /></ThemeColor><ThemeColor><clrColor Web=\"Red\" /></ThemeColor>" +
                "<ThemeColor><strColorItemName>none</strColorItemName></ThemeColor>" +
                "<ThemeColor><clrColor Web=\"notacolour\" /></ThemeColor><ThemeColor/></colors></ThemeColorTable>",
                // members out of order, unknown elements and attributes, comments, duplicates
                head + "<terminalTheming> 1 </terminalTheming><unknown><colors><ThemeColor/></colors></unknown>" +
                "<!-- c --><iconSet>HighContrastIconSet</iconSet><iconSet>BurnKermitIconSet</iconSet>" +
                "<colors extra=\"1\"><foo/><ThemeColor><strVariableName>A</strVariableName><strVariableName>B</strVariableName>" +
                "<clrColor Web=\"Blue\" other=\"x\"><child/></clrColor><clrColor Web=\"Green\" /></ThemeColor>text</colors>" +
                "<colors><ThemeColor><strColorItemName>second list</strColorItemName></ThemeColor></colors>" +
                "<strThemeName>n1</strThemeName><strThemeName>n2</strThemeName></ThemeColorTable>",
                // empty
                "<?xml version=\"1.0\"?><ThemeColorTable/>",
                head + "<colors/></ThemeColorTable>",
                head + "<strColorItemName/><colors><ThemeColor><strColorItemName/><strVariableName></strVariableName></ThemeColor></colors></ThemeColorTable>",
                // whitespace and CDATA in strings
                head + "<colors><ThemeColor><strColorItemName>  spaced  </strColorItemName><strVariableName><![CDATA[c]]>d</strVariableName></ThemeColor></colors></ThemeColorTable>",
                // nil: a list item is read as null, clrColor ignores it
                head + "<colors><ThemeColor xsi:nil=\"true\"><clrColor Web=\"Red\" /></ThemeColor><ThemeColor xsi:nil=\"false\" /></colors></ThemeColorTable>",
                head + "<colors><ThemeColor><clrColor xsi:nil=\"true\" Web=\"Red\" /></ThemeColor></colors></ThemeColorTable>",
                // failures: bad enum, bad bool, bad alpha, wrong root, namespaced root, not xml, empty file
                head + "<iconSet>burnkermiticonset</iconSet></ThemeColorTable>",
                head + "<terminalTheming>yes</terminalTheming></ThemeColorTable>",
                head + "<colors><ThemeColor><clrColor Alpha=\"300\" /></ThemeColor></colors></ThemeColorTable>",
                "<?xml version=\"1.0\"?><Other/>",
                "<?xml version=\"1.0\"?><ThemeColorTable xmlns=\"urn:x\"/>",
                "not xml",
                "",
                // namespaced child elements are unknown
                "<?xml version=\"1.0\"?><ThemeColorTable xmlns:p=\"urn:x\"><p:iconSet>bad</p:iconSet><p:colors><ThemeColor/></p:colors></ThemeColorTable>",
            };

            for (int i = 0; i < cases.Length; i++)
                AssertSame(WriteTemp("case" + i + ".mpusertheme", cases[i]));
        }

        [TestMethod]
        public void MissingFileGivesNull()
        {
            var file = Path.Combine(tempdir, "missing.mpusertheme");
            Assert.IsNull(ReadWithSerializer(file));
            Assert.IsNull(ThemeManager.ReadThemeFile(file));
        }

        [TestMethod]
        // Reports timings only: if another test in this process already built the serializer, its
        // first-use cost is gone, so a speed assertion here would be flaky
        public void ReportFirstThemeReadTimes()
        {
            var theme = FindRepoFile("BurntKermit.mpsystheme");

            // first use in this process: XmlSerializer generates its reader here
            var sw = Stopwatch.StartNew();
            ReadWithSerializer(theme);
            var serializer = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            ThemeManager.ReadThemeFile(theme);
            var parser = sw.Elapsed.TotalMilliseconds;

            Console.WriteLine("first theme read: XmlSerializer {0:0.0} ms, ReadThemeFile {1:0.0} ms", serializer, parser);
        }
    }
}
