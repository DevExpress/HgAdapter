using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace HgAdapter.XmlLogFormat {

    [XmlRoot("log")]
    public class Log {

        [XmlElement("logentry", typeof(LogEntry))]
        public List<LogEntry> Entries = new List<LogEntry>();

    }

    public class LogEntry {

        [XmlAttribute("node")]
        public string Hash;

        [XmlElement("author")]
        public Author Author;

        [XmlElement("msg")]
        public string Msg;

        [XmlArray("paths")]
        [XmlArrayItem("path")]
        public List<PathInfo> PathItems;

    }

    public class Author {

        [XmlText]
        public string Name;

    }

    public class PathInfo {

        [XmlAttribute("action")]
        public string Action;

        [XmlText]
        public string Path;

    }

}
