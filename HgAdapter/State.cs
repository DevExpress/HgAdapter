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

        public void AddCheckpoint(DateTime date, string tip, string branch) {
            Checkpoints.Add(new Checkpoint { 
                Date = date,
                Tip = tip,
                Branch = branch
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
            var minIndex = -1;
            var maxIndex = -1;

            for(var i = 0; i < Checkpoints.Count; i++) {
                var c = Checkpoints[i];

                if(minIndex < 0 || c.Date <= startDate)
                    minIndex = i;

                if(c.Date <= endDate)
                    maxIndex = i;
            }

            if(maxIndex > -1) {
                while(minIndex > 0 && Checkpoints[minIndex].Branch != Checkpoints[maxIndex].Branch)
                    minIndex--;
            }

            return new Range {
                Start = Checkpoints[minIndex].Tip,
                End = Checkpoints[maxIndex].Tip
            };        
        }

        public class Checkpoint {
            [XmlAttribute]
            public DateTime Date;

            [XmlAttribute]
            public string Tip;

            [XmlAttribute]
            public string Branch;
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
