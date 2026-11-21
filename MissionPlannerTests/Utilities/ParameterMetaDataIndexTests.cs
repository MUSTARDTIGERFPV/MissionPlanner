using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MissionPlanner.Utilities.Tests
{
    /// <summary>
    /// The indexed apm.pdef.xml lookup must give exactly what the old linear document scan gave, for
    /// every param and every key in the downloaded metadata.
    /// </summary>
    [TestClass]
    public class ParameterMetaDataIndexTests
    {
        private static readonly string[] Vehicles =
        {
            "SITL", "AP_Periph", "ArduSub", "Rover", "ArduCopter",
            "ArduPlane", "AntennaTracker", "Blimp", "Heli"
        };

        /// <summary>
        /// Vehicles whose pdef is on disk and fresh enough that ParameterMetaDataRepositoryAPMpdef will not
        /// re-download or re-extract it while the test runs. Checked before that class is touched, so a
        /// machine without metadata does not go to the network.
        /// </summary>
        private static List<string> AvailableVehicles()
        {
            return Vehicles.Where(v =>
            {
                var file = PdefFile(v);
                return File.Exists(file) && new FileInfo(file).LastWriteTime.AddDays(6) > DateTime.Now;
            }).ToList();
        }

        private static string PdefFile(string vehicle)
        {
            return Settings.GetDataDirectory() + vehicle + ".apm.pdef.xml";
        }

        private static IEnumerable<XElement> AllParams(XDocument doc)
        {
            return doc.Element("paramfile").Elements().Elements().Where(p => p.HasAttributes).Elements();
        }

        private static List<string> Names(XDocument doc, string vehicle)
        {
            var names = new HashSet<string>();
            foreach (var param in AllParams(doc))
            {
                var name = param.Attribute("name").Value;
                names.Add(name);
                // the GCS asks for the bare name; vehicle params are stored as "Vehicle:NAME"
                if (name.StartsWith(vehicle + ":"))
                    names.Add(name.Substring(vehicle.Length + 1));
            }
            names.Add("NO_SUCH_PARAM");
            return names.OrderBy(a => a, StringComparer.Ordinal).ToList();
        }

        private static List<string> Keys(XDocument doc)
        {
            var keys = new HashSet<string>
            {
                ParameterMetaDataConstants.DisplayName, ParameterMetaDataConstants.Description,
                ParameterMetaDataConstants.User, ParameterMetaDataConstants.Values,
                ParameterMetaDataConstants.Range, ParameterMetaDataConstants.Units,
                ParameterMetaDataConstants.Bitmask, ParameterMetaDataConstants.Increment,
                ParameterMetaDataConstants.RebootRequired, ParameterMetaDataConstants.ReadOnly,
                "NoSuchKey"
            };
            foreach (var param in AllParams(doc))
            {
                foreach (var attr in param.Attributes())
                    keys.Add(attr.Name.LocalName);
                foreach (var field in param.Elements("field"))
                    keys.Add(field.Attribute("name").Value);
            }
            return keys.OrderBy(a => a, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// ParameterMetaDataRepositoryAPMpdef.GetParameterMetaData as it was before the index (41298695e),
        /// working on a document the test loaded itself.
        /// </summary>
        private static string OldLookup(XDocument doc, string nodeKey, string metaKey, string vechileType)
        {
            // remap keys
            if (metaKey == ParameterMetaDataConstants.DisplayName)
                metaKey = "humanName";
            if (metaKey == ParameterMetaDataConstants.Description)
                metaKey = "documentation";
            if (metaKey == ParameterMetaDataConstants.User)
                metaKey = "user";

            try
            {
                var vechileKey = vechileType + ":" + nodeKey;
                foreach (var paramfile in doc.Element("paramfile").Elements())
                {
                    foreach (var parameters in paramfile.Elements())
                    {
                        if (parameters.HasAttributes)
                        {
                            foreach (var param in parameters.Elements())
                            {
                                if (param.Attribute("name").Value == vechileKey ||
                                    param.Attribute("name").Value == nodeKey)
                                {
                                    if (param.Attribute(metaKey) != null)
                                    {
                                        return param.Attribute(metaKey).Value;
                                    }
                                    if (metaKey == ParameterMetaDataConstants.Values)
                                    {
                                        var ans = "";
                                        param.Elements("values").Elements().ForEach(a =>
                                        {
                                            if (a.Name == "value")
                                            {
                                                var code = a.Attribute("code");
                                                var value = a.Value.ToString();
                                                ans += String.Format("{0}:{1},", code.Value, value);
                                            }
                                        });
                                        return ans;
                                    }
                                    foreach (var xElement in param.Elements())
                                    {
                                        if (xElement.Name == "field")
                                        {
                                            var name = xElement.Attribute("name");
                                            if (name != null && name.Value == metaKey)
                                            {
                                                return xElement.Value;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }

        /// <summary>
        /// Every name x key of every vehicle takes the linear scan ~8 minutes, so by default every 13th name
        /// is checked. Set PARAM_METADATA_FULL=1 to check them all.
        /// </summary>
        [TestMethod]
        public void IndexedLookupMatchesLinearScan()
        {
            var vehicles = AvailableVehicles();
            if (vehicles.Count == 0)
                Assert.Inconclusive("no fresh *.apm.pdef.xml in " + Settings.GetDataDirectory());

            var stride = Environment.GetEnvironmentVariable("PARAM_METADATA_FULL") == "1" ? 1 : 13;

            long lookups = 0, misses = 0;
            var oldTime = new Stopwatch();
            var newTime = new Stopwatch();
            var mismatches = new List<string>();

            foreach (var vehicle in vehicles)
            {
                var doc = XDocument.Load(PdefFile(vehicle));
                var names = Names(doc, vehicle).Where((n, idx) => idx % stride == 0).ToList();
                var keys = Keys(doc);

                // load and index outside the timed loop, as the old code had the document loaded too
                newTime.Start();
                ParameterMetaDataRepositoryAPMpdef.GetParameterMetaData("NO_SUCH_PARAM", "NoSuchKey", vehicle);
                newTime.Stop();

                var expected = new string[names.Count * keys.Count];
                oldTime.Start();
                int i = 0;
                foreach (var name in names)
                    foreach (var key in keys)
                        expected[i++] = OldLookup(doc, name, key, vehicle);
                oldTime.Stop();

                var actual = new string[expected.Length];
                newTime.Start();
                i = 0;
                foreach (var name in names)
                    foreach (var key in keys)
                        actual[i++] = ParameterMetaDataRepositoryAPMpdef.GetParameterMetaData(name, key, vehicle);
                newTime.Stop();

                i = 0;
                foreach (var name in names)
                    foreach (var key in keys)
                    {
                        if (expected[i] == "")
                            misses++;
                        if (expected[i] != actual[i] && mismatches.Count < 20)
                            mismatches.Add(vehicle + " " + name + " " + key + ": '" + expected[i] + "' != '" + actual[i] + "'");
                        i++;
                    }

                lookups += expected.Length;
                Console.WriteLine("{0}: {1} names x {2} keys", vehicle, names.Count, keys.Count);
            }

            Console.WriteLine("{0} lookups ({1} misses) over {2} vehicles: linear scan {3} ms, index {4} ms (incl. index build)",
                lookups, misses, vehicles.Count, oldTime.ElapsedMilliseconds, newTime.ElapsedMilliseconds);

            Assert.AreEqual(0, mismatches.Count, string.Join("\n", mismatches));
        }

        /// <summary>
        /// ParameterMetaDataRepository (vehicle, then SITL, then AP_Periph, then the legacy xml) must give what
        /// the old fallback chain gave, also the second time, when the answer (or miss) comes from its cache.
        /// </summary>
        [TestMethod]
        public void RepositoryFallbackAndMissCacheMatchLinearScan()
        {
            var vehicles = AvailableVehicles();
            var vehicle = vehicles.Contains("ArduCopter") ? "ArduCopter" : vehicles.FirstOrDefault();
            if (vehicle == null || !vehicles.Contains("SITL") || !vehicles.Contains("AP_Periph"))
                Assert.Inconclusive("need fresh SITL, AP_Periph and a vehicle *.apm.pdef.xml in " + Settings.GetDataDirectory());

            var doc = XDocument.Load(PdefFile(vehicle));
            var sitl = XDocument.Load(PdefFile("SITL"));
            var periph = XDocument.Load(PdefFile("AP_Periph"));

            var names = Names(doc, vehicle);
            // a few AP_Periph-only and SITL-only names exercise the fallbacks
            names.AddRange(AllParams(periph).Select(p => p.Attribute("name").Value).Take(50));
            names.AddRange(AllParams(sitl).Select(p => p.Attribute("name").Value).Take(50));
            var keys = Keys(doc);
            // the old chain is slow; every 20th name keeps this to a few seconds
            names = names.Where((n, idx) => idx % 20 == 0).ToList();

            var mismatches = new List<string>();
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var name in names)
                {
                    foreach (var key in keys)
                    {
                        var expected = OldLookup(doc, name, key, vehicle);
                        if (expected == string.Empty)
                            expected = OldLookup(sitl, name, key, "SITL");
                        if (expected == string.Empty)
                            expected = OldLookup(periph, name, key, "AP_Periph");
                        if (expected == string.Empty)
                            expected = ParameterMetaDataRepositoryAPM.GetParameterMetaData(name, key, vehicle);

                        var actual = ParameterMetaDataRepository.GetParameterMetaData(name, key, vehicle);
                        if (expected != actual && mismatches.Count < 20)
                            mismatches.Add(pass + " " + name + " " + key + ": '" + expected + "' != '" + actual + "'");
                    }
                }
            }

            Assert.AreEqual(0, mismatches.Count, string.Join("\n", mismatches));
        }
    }
}
