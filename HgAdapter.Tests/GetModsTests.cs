using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HgAdapter.Tests {

    [TestFixture, SetCulture("en-US")]
    public class GetModsTests : AdapterFixture {
        const string GETMODS = "GETMODS";

        [Test]
        public void InitialCheck() {
            CommitFile("file1", "user1", "message1");
            ExecAdapter(GETMODS, "(NOW)", DateTime.Now.AddMinutes(-1));

            CollectionAssert.Contains(LoggerOutput, "new checkpoint: c8143034b60fbc51883eb11cd20592b92f7d3dfb in branch 'default'");
            CollectionAssert.Contains(LoggerOutput, "range: (c8143034b60fbc51883eb11cd20592b92f7d3dfb, c8143034b60fbc51883eb11cd20592b92f7d3dfb]");
            CollectionAssert.Contains(LoggerOutput, "range is empty or not defined");

            Assert.IsTrue(LoggerOutput.Any(line => line.Contains("-r \"max(branch(default))\"")), "query for tip");

            Assert.AreEqual(1, State.Checkpoints.Count);
            Assert.AreEqual(0, ParseModifications().Count());

            // NOTE initial check should not yield any modifications
            // Otherwise CCNet would trigger repeated builds when \\CISERVER\LocalProjects\ are cleared 
        }

        [TestCase(true), TestCase(false)]
        public void HaveModifications(bool prevDateEqualsLastCheckpoint) {
            var thisDate = DateTime.Now;
            var prevDate = thisDate.AddMinutes(-1);

            var checkPointDate = prevDateEqualsLastCheckpoint
                // external source control case (prev date is prev integration date)
                ? prevDate
                // ISourceSafeDriver case (prev date is prev check date)
                : thisDate.AddHours(-1);           

            CommitFile("file1", "user1", "message1");
            State.AddCheckpoint(checkPointDate, "c8143034b60fbc51883eb11cd20592b92f7d3dfb", "default");

            CommitFile("file2", "user2", "message2");
            
            ExecAdapter(GETMODS, thisDate, prevDate);

            CollectionAssert.Contains(LoggerOutput, "new checkpoint: 003205be8d3be4da1db065404c7aa83eb35fe8dc in branch 'default'");
            CollectionAssert.Contains(LoggerOutput, "range: (c8143034b60fbc51883eb11cd20592b92f7d3dfb, 003205be8d3be4da1db065404c7aa83eb35fe8dc]");
            CollectionAssert.Contains(LoggerOutput, "hg log entries: 1");

            Assert.IsTrue(
                LoggerOutput.Any(line => line.Contains("max((branch(default)) and (c8143034b60fbc51883eb11cd20592b92f7d3dfb:))")), 
                "optimized query for tip"
            );

            Assert.IsTrue(
                LoggerOutput.Any(line => line.Contains("(branch(default)) and (c8143034b60fbc51883eb11cd20592b92f7d3dfb:003205be8d3be4da1db065404c7aa83eb35fe8dc - c8143034b60fbc51883eb11cd20592b92f7d3dfb)")), 
                "query for changes in range"
            );

            Assert.AreEqual(2, State.Checkpoints.Count);

            var modifications = ParseModifications();
            Assert.AreEqual(1, modifications.Count());
            AssertModificationElement(modifications.First(), "003205be8d3b (A)", thisDate, "file2", "user2", "message2");
        }


        [Test]
        public void RepoNotChangedOptimization() {
            CommitFile("file1", "user1", "message1");
            State.AddCheckpoint(DateTime.Now, "c8143034b60fbc51883eb11cd20592b92f7d3dfb", "default");

            Thread.Sleep(1000);
            ExecAdapter(GETMODS, DateTime.Now.AddMinutes(1), DateTime.Now);

            Assert.IsTrue(LoggerOutput.Any(line => line.StartsWith("repo not changed")));
        }

        [Test]
        public void RepoNotChangedOptimization_LockedRepo() {
            CommitFile("file1", "user1", "message1");
            State.AddCheckpoint(DateTime.Now.AddMinutes(-5), "c8143034b60fbc51883eb11cd20592b92f7d3dfb", "default");
            var waitCount = 0;

            var lockPath = Path.Combine(TempRepoDir, ".hg/store/lock");
            File.WriteAllText(lockPath, "user:123");                        
            new Thread(delegate() {
                while(true) {
                    waitCount = LoggerOutput.Count(line => line.Contains("transaction in progress"));
                    if(waitCount > 0)
                        break;
                }
                File.Delete(lockPath);
            }).Start();

            ExecAdapter(GETMODS, DateTime.Now, DateTime.Now.AddMinutes(-1));
            Assert.IsTrue(waitCount > 0);
        }


        [Test]
        public void NoChanges() {
            var thisDate = DateTime.Now;
            var prevDate = thisDate.AddMinutes(-1);

            CommitFile("file1", "user1", "message1");
            State.AddCheckpoint(prevDate, "c8143034b60fbc51883eb11cd20592b92f7d3dfb", "default");

            ExecAdapter(GETMODS, thisDate, prevDate);

            CollectionAssert.Contains(LoggerOutput, "no new changesets");
            CollectionAssert.Contains(LoggerOutput, "range: (c8143034b60fbc51883eb11cd20592b92f7d3dfb, c8143034b60fbc51883eb11cd20592b92f7d3dfb]");
            CollectionAssert.Contains(LoggerOutput, "range is empty or not defined");

            Assert.AreEqual(0, ParseModifications().Count());
        }

        [Test]
        public void MergesAndInclude() {
            var thisDate = DateTime.Now;
            var prevDate = thisDate.AddMinutes(-1);

            ExecHG("branch 15_1");
            CommitFile("README", "x", "x"); // 1f278868556749e0a4ec13b58e0cc7a26074b625
            
            ExecHG("branch new_feature");
            CommitFile("interesting_file", "x", "x");
            ExecHG("update -r branch(15_1)");
            ExecHG("merge new_feature");
            Commit("x", "Interesting merge"); // c78cc1f162b96f39ddfeabc35a19410352947b3c

            ExecHG("update -r branch(new_feature)");
            CommitFile("alien_file", "x", "x");
            ExecHG("update -r branch(15_1)");
            ExecHG("merge new_feature");
            Commit("x", "Alien merge"); // f0a8b33c84f190be329d616bb37d39513dbaf76b

            State.AddCheckpoint(prevDate, "1f278868556749e0a4ec13b58e0cc7a26074b625", "15_1");

            ExecAdapter(GETMODS, thisDate, prevDate, "--revset=branch(15_1)", "--include=interesting_file");

            CollectionAssert.Contains(LoggerOutput, "Merge f0a8b33c84f190be329d616bb37d39513dbaf76b does not affect interesting_file");

            var modifications = ParseModifications();
            Assert.AreEqual(1, modifications.Count());
            AssertModificationElement(modifications.First(), "c78cc1f162b9 (Merge)", thisDate, "-", "x", "Interesting merge");
        }

        [Test]
        public void MultiBranchScenario() {
            ExecHG("branch rel_f1");
            CommitFile("f1_c1", "x", "x"); // 67c9dd245a216b232a26cab7a1310210f03234f4

            ExecHG("branch rel_f2");
            CommitFile("f2_c1", "x", "x"); // 468d7d162a91f84068c5fe5956e83218b6ceca03

            ExecHG("update branch(rel_f1)");
            CommitFile("f1_c2", "x", "x"); // 76e8989517aaf36cb6761893526b6b559a45f670

            ExecHG("update branch(rel_f2)");
            CommitFile("f2_c2", "x", "x"); // 96c7171b3d37add9de09d9a5078f8d9593b5880b

            var revset = "branch('re:^rel(_.+)$')";
            var time1 = new DateTime(2016, 8, 7);
            var time2 = time1.AddMinutes(1);
            var time3 = time1.AddMinutes(2);

            // Pretend we already know branch rel_f1
            State.AddCheckpoint(time1, "67c9dd245a216b232a26cab7a1310210f03234f4", "rel_f1");

            ExecAdapter(GETMODS, time2, time1, "--revset=" + revset);

            Assert.AreEqual(
                "76e8989517aaf36cb6761893526b6b559a45f670", 
                State.Checkpoints.Last().Tip,
                "should detect lowest tip among satisfying branches"
            );

            CollectionAssert.AreEqual(
                new[] {
                    "76e8989517aa (A)"
                },
                ParseModifications().Select(x => x.Element("Type").Value),
                "should gather commits only from rel_f1 branch"
            );

            ExecAdapter(GETMODS, time3, time2, "--revset=" + revset);

            Assert.AreEqual(
                "96c7171b3d37add9de09d9a5078f8d9593b5880b", 
                State.Checkpoints.Last().Tip
            );

            CollectionAssert.AreEqual(
                new[] {
                    "468d7d162a91 (A)",
                    "96c7171b3d37 (A)"
                },
                ParseModifications().Select(x => x.Element("Type").Value),
                "should not skip commits in rel_f2 branch"
            );
        }

        [Test]
        public void SpecialCharsInBranchName() {
            ExecHG("branch \"(\' \')\"");
            CommitFile("any", "any", "any");
            ExecAdapter(GETMODS, DateTime.Now, DateTime.MinValue, "--revset=tag(tip)");
            Assert.AreEqual("(' ')", State.Checkpoints.Last().Branch);
        }

    }

}
