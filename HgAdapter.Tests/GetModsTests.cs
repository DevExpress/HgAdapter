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

            CollectionAssert.Contains(LoggerOutput, "new checkpoint: c8143034b60fbc51883eb11cd20592b92f7d3dfb");
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
            State.AddCheckpoint(checkPointDate, "c8143034b60fbc51883eb11cd20592b92f7d3dfb");

            CommitFile("file2", "user2", "message2");

            ExecAdapter(GETMODS, thisDate, prevDate);

            CollectionAssert.Contains(LoggerOutput, "new checkpoint: 003205be8d3be4da1db065404c7aa83eb35fe8dc");
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
            State.AddCheckpoint(DateTime.Now, "c8143034b60fbc51883eb11cd20592b92f7d3dfb");

            var prevIntegrationDate = DateTime.Now.AddDays(1);
            ExecAdapter(GETMODS, prevIntegrationDate.AddMinutes(1), prevIntegrationDate);

            Assert.IsTrue(LoggerOutput.Any(line => line.StartsWith("repo not changed")));
        }

        [Test]
        public void RepoNotChangedOptimization_LockedRepo() {
            CommitFile("file1", "user1", "message1");
            State.AddCheckpoint(DateTime.Now.AddMinutes(-5), "c8143034b60fbc51883eb11cd20592b92f7d3dfb");
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
        public void RepoNotChangedOptimization_LockedRepo_Timeout() {
            CommitFile("file1", "user1", "message1");
            State.AddCheckpoint(DateTime.Now.AddMinutes(-5), "c8143034b60fbc51883eb11cd20592b92f7d3dfb");
            var waitCount = 0;

            var lockPath = Path.Combine(TempRepoDir, ".hg/store/lock");
            File.WriteAllText(lockPath, "user:123");
            try {
                ExecAdapter(GETMODS, DateTime.Now, DateTime.Now.AddMinutes(-1), $"--timeout={TimeSpan.FromSeconds(3).TotalMilliseconds}");
            } finally {
                File.Delete(lockPath);
            }
            waitCount = LoggerOutput.Count(line => line.Contains("transaction in progress"));
            Assert.IsTrue(waitCount < 4);
        }

        [Test]
        public void NoChanges() {
            var thisDate = DateTime.Now;
            var prevDate = thisDate.AddMinutes(-1);

            CommitFile("file1", "user1", "message1");
            State.AddCheckpoint(prevDate, "c8143034b60fbc51883eb11cd20592b92f7d3dfb");

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

            State.AddCheckpoint(prevDate, "1f278868556749e0a4ec13b58e0cc7a26074b625");

            ExecAdapter(GETMODS, thisDate, prevDate, "--revset=branch(15_1)", "--include=interesting_file");

            CollectionAssert.Contains(LoggerOutput, "Merge f0a8b33c84f190be329d616bb37d39513dbaf76b does not affect interesting_file");

            var modifications = ParseModifications();
            Assert.AreEqual(1, modifications.Count());
            AssertModificationElement(modifications.First(), "c78cc1f162b9 (Merge)", thisDate, "-", "x", "Interesting merge");
        }



    }

}
