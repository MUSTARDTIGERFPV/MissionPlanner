using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.ArduPilot;

namespace MissionPlannerTests.ArduPilot
{
    [TestClass]
    public class RetryTimeoutTests
    {
        [TestMethod]
        public void CompletesPromptlyWhenCompleteIsSetFromAnotherThread()
        {
            var timeout = new RetryTimeout(3, 5000);
            int calls = 0;
            timeout.WorkToDo = () =>
            {
                calls++;
                // the reply arrives on the reader thread shortly after the request
                Task.Run(async () =>
                {
                    await Task.Delay(20);
                    timeout.Complete = true;
                });
            };

            var sw = Stopwatch.StartNew();
            Assert.IsTrue(timeout.DoWork());
            sw.Stop();

            Assert.AreEqual(1, calls);
            // the old 100 ms poll could not return before ~100 ms
            Assert.IsTrue(sw.ElapsedMilliseconds < 90, "took " + sw.ElapsedMilliseconds + " ms");
        }

        [TestMethod]
        public void CompleteSetInsideWorkToDoReturnsTrue()
        {
            var timeout = new RetryTimeout(3, 5000);
            timeout.WorkToDo = () => timeout.Complete = true;
            Assert.IsTrue(timeout.DoWork());
        }

        [TestMethod]
        public void RetriesThenFailsWhenNeverComplete()
        {
            var timeout = new RetryTimeout(3, 50);
            int calls = 0;
            timeout.WorkToDo = () => calls++;

            var sw = Stopwatch.StartNew();
            Assert.IsFalse(timeout.DoWork());
            sw.Stop();

            Assert.AreEqual(3, calls);
            Assert.IsTrue(sw.ElapsedMilliseconds >= 140, "took " + sw.ElapsedMilliseconds + " ms");
        }

        [TestMethod]
        public void ResetTimeoutExtendsTheCurrentAttempt()
        {
            var timeout = new RetryTimeout(1, 100);
            timeout.WorkToDo = () =>
            {
                Task.Run(() =>
                {
                    // keep the attempt alive past its first deadline, then complete
                    for (int i = 0; i < 4; i++)
                    {
                        Thread.Sleep(50);
                        timeout.ResetTimeout();
                    }
                    timeout.Complete = true;
                });
            };

            Assert.IsTrue(timeout.DoWork());
        }

        [TestMethod]
        public void GivingUpFromACallbackEndsTheWaitPromptly()
        {
            // MAVFtp NAK handlers set Retries = 0, cancel sets RetriesCurrent = 999
            foreach (var giveUp in new System.Action<RetryTimeout>[] { t => t.Retries = 0, t => t.RetriesCurrent = 999 })
            {
                var timeout = new RetryTimeout(5, 5000);
                int calls = 0;
                timeout.WorkToDo = () =>
                {
                    calls++;
                    Task.Run(async () =>
                    {
                        await Task.Delay(20);
                        giveUp(timeout);
                    });
                };

                var sw = Stopwatch.StartNew();
                Assert.IsFalse(timeout.DoWork());
                sw.Stop();

                Assert.AreEqual(1, calls);
                Assert.IsTrue(sw.ElapsedMilliseconds < 1000, "took " + sw.ElapsedMilliseconds + " ms");
            }
        }

        [TestMethod]
        public void CompleteFromAPreviousRunDoesNotLeakIntoTheNext()
        {
            var timeout = new RetryTimeout(1, 50);
            timeout.WorkToDo = () => timeout.Complete = true;
            Assert.IsTrue(timeout.DoWork());

            timeout.WorkToDo = () => { };
            Assert.IsFalse(timeout.DoWork());
        }
    }
}
