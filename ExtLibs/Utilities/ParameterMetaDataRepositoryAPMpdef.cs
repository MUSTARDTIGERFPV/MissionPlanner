using System;
using System.Configuration;
using System.IO;
using System.Xml.Linq;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Threading.Tasks;
using log4net;
using SharpCompress.Compressors.Xz;

namespace MissionPlanner.Utilities
{
    public static class ParameterMetaDataRepositoryAPMpdef
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static Dictionary<string,XDocument> _parameterMetaDataXML = new Dictionary<string, XDocument>();

        private static string[] vehicles = new[]
        {
             "SITL", "AP_Periph", "ArduSub", "Rover", "ArduCopter",
            "ArduPlane", "AntennaTracker", "Blimp", "Heli"      
        };

        private static string[] vehicles_versioned = new[] 
        {
            "Copter", "Plane", "Rover", "Sub", "Tracker"
        };

        static string url = "https://autotest.ardupilot.org/Parameters/{0}/apm.pdef.xml.gz";

        static string urlversioned = "https://autotest.ardupilot.org/Parameters/versioned/{0}/stable-{1}/apm.pdef.xml";

        static ParameterMetaDataRepositoryAPMpdef()
        {
            GetMetaData();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ParameterMetaDataRepository"/> class.
        /// </summary>
        public static void CheckLoad(string vehicle = "")
        {
            if (!_parameterMetaDataXML.ContainsKey(vehicle))
                Reload(vehicle);
        }

        public static async Task GetMetaDataVersioned(Version version)
        {
            List<Task> tlist = new List<Task>();

            vehicles_versioned.ForEach(a =>
            {
                try
                {
                    var newurl = String.Format(urlversioned, a, version.ToString());
                    var file = Path.Combine(Settings.GetDataDirectory(), a + version.ToString() + ".apm.pdef.xml");
                    if (File.Exists(file))
                        if (new FileInfo(file).LastWriteTime.AddDays(7) > DateTime.Now)
                            return;
                    var dltask = Download.getFilefromNetAsync(newurl, file);
                    tlist.Add(dltask);
                }
                catch (Exception ex) { log.Error(ex); }
            });

            await Task.WhenAll(tlist);

            vehicles_versioned.ForEach(a =>
            {
                try
                {
                    Reload(a + version.ToString());

                    var veh = vehicles.First(b => b.Contains(a));

                    if(_parameterMetaDataXML.ContainsKey(a + version.ToString()))
                        _parameterMetaDataXML[veh] = _parameterMetaDataXML[a + version.ToString()];
                }
                catch (Exception ex) { log.Error(ex); }
            });
        }

        public static async Task GetMetaData(bool force = false)
        {
            List<Task> tlist = new List<Task>();

            vehicles.ForEach(a =>
            {
                try
                {
                    var newurl = String.Format(url, a);
                    // try the gzipped version first
                    var file = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml.gz");
                    if(File.Exists(file))
                        if (new FileInfo(file).LastWriteTime.AddDays(7) > DateTime.Now && !force)
                            return;
                    // try just the xml
                    var file2 = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml");
                    if (File.Exists(file2))
                        if (new FileInfo(file2).LastWriteTime.AddDays(7) > DateTime.Now && !force)
                            return;
                    var dltask = Download.getFilefromNetAsync(newurl, file);
                    tlist.Add(dltask);
                }
                catch (Exception ex) { log.Error(ex); }
            });

            await Task.WhenAll(tlist);

            vehicles.ForEach(a =>
            {
                try
                {
                    var fileout = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml");
                    var fileouttemp = Path.Combine(Path.GetTempFileName());
                    var file = Path.Combine(Settings.GetDataDirectory(), a + ".apm.pdef.xml.gz");
                    if (File.Exists(file))
                    {
                        // drop out to prevent unnessary fileio at startup
                        if (File.Exists(fileout) && new FileInfo(fileout).LastWriteTime.AddDays(7) > DateTime.Now && !force)
                            return;
                        using (var read = File.OpenRead(file))
                        {
                            //if (XZStream.IsXZStream(read))
                            {
                                read.Position = 0;
                                var stream = new GZipStream(read, CompressionMode.Decompress);
                                //var stream = new XZStream(read);
                                using (var outst = File.Open(fileouttemp, FileMode.Create))
                                {
                                    stream.CopyTo(outst);
                                }
                                // move after good decompress
                                File.Delete(fileout);
                                File.Move(fileouttemp, fileout);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            });

            Reset();
        }

        public static void Reset()
        {
            _parameterMetaDataXML.Clear();
        }

        public static void Reload(string vehicle = "")
        {
            string paramMetaDataXMLFileName =
                String.Format("{0}{1}", Settings.GetDataDirectory(), vehicle + ".apm.pdef.xml");

            try
            {
                if (File.Exists(paramMetaDataXMLFileName))
                {
                    _parameterMetaDataXML[vehicle] = XDocument.Load(paramMetaDataXMLFileName);
                }

            }
            catch (System.Xml.XmlException ex) 
            {
                try
                {
                    if (File.Exists(paramMetaDataXMLFileName))
                        File.Delete(paramMetaDataXMLFileName);
                }
                catch { }
                log.Error(paramMetaDataXMLFileName);
                log.Error(ex);
            }
            catch (Exception ex)
            {
                log.Error(paramMetaDataXMLFileName);
                log.Error(ex);
            }
        }

        /// <summary>
        /// Gets the parameter meta data.
        /// </summary>
        /// <param name="nodeKey">The node key.</param>
        /// <param name="metaKey">The meta key.</param>
        /// <returns></returns>
        public static string GetParameterMetaData(string nodeKey, string metaKey, string vechileType)
        {
            // remap names
            if (vechileType == "ArduCopter2")
                vechileType = "ArduCopter";
            if (vechileType == "ArduRover")
                vechileType = "Rover";
            if (vechileType == "ArduTracker")
                vechileType = "AntennaTracker";

            CheckLoad(vechileType);

            // remap keys
            if (metaKey == ParameterMetaDataConstants.DisplayName)
                metaKey = "humanName";
            if (metaKey == ParameterMetaDataConstants.Description)
                metaKey = "documentation";
            if (metaKey == ParameterMetaDataConstants.User)
                metaKey = "user";

            if (_parameterMetaDataXML.ContainsKey(vechileType))
            {
                try
                {
                    var index = _index.GetValue(_parameterMetaDataXML[vechileType], BuildIndex);

                    // walk the params named vechileKey or nodeKey in document order, as the
                    // full document scan did, and return the first one that has an answer
                    var vechileKey = vechileType + ":" + nodeKey;
                    List<int> a, b = null;
                    index.ByName.TryGetValue(vechileKey, out a);
                    if (nodeKey != null)
                        index.ByName.TryGetValue(nodeKey, out b);
                    int ia = 0, ib = 0;
                    while (true)
                    {
                        int next;
                        if (a != null && ia < a.Count && (b == null || ib >= b.Count || a[ia] < b[ib]))
                            next = a[ia++];
                        else if (b != null && ib < b.Count)
                            next = b[ib++];
                        else
                            break;

                        var answer = GetParamMetaData(index.Params[next], metaKey);
                        if (answer != null)
                            return answer;
                    }

                    if (index.Incomplete)
                        log.Error("param metadata for " + vechileType + " is malformed, " + nodeKey + " not found");
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                } 
            }

            return string.Empty;
        }

        private class ParamIndex
        {
            /// <summary>every param in document order</summary>
            public List<XElement> Params = new List<XElement>();
            /// <summary>param name to positions in Params, ascending</summary>
            public Dictionary<string, List<int>> ByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            /// <summary>the document could not be scanned to the end</summary>
            public bool Incomplete;
        }

        // built on first use for each loaded XDocument, so it follows every (re)assignment of _parameterMetaDataXML
        private static readonly ConditionalWeakTable<XDocument, ParamIndex> _index =
            new ConditionalWeakTable<XDocument, ParamIndex>();

        private static ParamIndex BuildIndex(XDocument doc)
        {
            var index = new ParamIndex();
            var root = doc.Element("paramfile");
            if (root == null)
            {
                index.Incomplete = true;
                return index;
            }

            foreach (var paramfile in root.Elements())
            {
                foreach (var parameters in paramfile.Elements())
                {
                    if (parameters.HasAttributes)
                    {
                        foreach (var param in parameters.Elements())
                        {
                            var name = param.Attribute("name");
                            if (name == null)
                            {
                                // a linear scan stops (throws) here, so nothing after this can match
                                index.Incomplete = true;
                                return index;
                            }

                            List<int> list;
                            if (!index.ByName.TryGetValue(name.Value, out list))
                                index.ByName[name.Value] = list = new List<int>(1);
                            list.Add(index.Params.Count);
                            index.Params.Add(param);
                        }
                    }
                }
            }

            return index;
        }

        /// <summary>
        /// The metaKey value of one param, or null if this param does not have it
        /// </summary>
        private static string GetParamMetaData(XElement param, string metaKey)
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

            return null;
        }
    }
}