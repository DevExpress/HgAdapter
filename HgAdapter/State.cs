using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace HgAdapter {

    [XmlRoot]
    public class State {
        public string RepoPath;
        public string RevSet;
        public string Include;

        [XmlArray]
        public List<Checkpoint> Checkpoints = new List<Checkpoint>();

        public void AddCheckpoint(DateTime date, string tip) {
            Checkpoints.Add(new Checkpoint {
                Date = date,
                Tip = tip
            });
        }

        public string GetTip() {
            var checkpoint = Checkpoints.LastOrDefault();
            if(checkpoint == null)
                return null;

            return checkpoint.Tip;
        }

        public string GetTip(DateTime maxDate) {
            var checkpoint = Checkpoints.LastOrDefault(c => c.Date <= maxDate);
            if(checkpoint == null)
                return null;

            return checkpoint.Tip;
        }

        public Range DateRangeToHashRange(DateTime startDate, DateTime endDate) {
            var minHash = "";
            var maxHash = "";

            foreach(var c in Checkpoints) {
                if(String.IsNullOrEmpty(minHash))
                    minHash = c.Tip;

                if(c.Date <= startDate)
                    minHash = c.Tip;
                if(c.Date <= endDate)
                    maxHash = c.Tip;
            }

            return new Range {
                Start = minHash,
                End = maxHash
            };
        }

        public class Checkpoint {
            [XmlAttribute]
            public DateTime Date;

            [XmlAttribute]
            public string Tip;
        }

        public class Range {
            public string Start;
            public string End;

            public bool IsGood() {
                return !String.IsNullOrEmpty(Start) && !String.IsNullOrEmpty(End) && Start != End;
            }

            public string ToRevSet() {
                return String.Format("{0}:{1} - {0}", Start, End);
            }

            public override string ToString() {
                return String.Format("({0}, {1}]", Start, End);
            }
        }
    }

}
