using System.Collections.Generic;

using NUnit.Framework;

using CKAN.ConsoleUI.Toolkit;

namespace Tests.ConsoleUI
{
    // Start() is never called because it blocks in Console.ReadKey.
    [TestFixture]
    public class ConsoleInputTests
    {
        [Test]
        public void PumpEvent_PostedAction_RunsAndReturnsNull()
        {
            bool ran = false;
            ConsoleInput.Post(() => ran = true);

            var key = ConsoleInput.PumpEvent();

            Assert.IsNull(key);
            Assert.IsTrue(ran);
        }

        [Test]
        public void PumpEvent_MultiplePostedActions_RunInOrder()
        {
            var order = new List<int>();
            ConsoleInput.Post(() => order.Add(1));
            ConsoleInput.Post(() => order.Add(2));
            ConsoleInput.Post(() => order.Add(3));

            ConsoleInput.PumpEvent();
            ConsoleInput.PumpEvent();
            ConsoleInput.PumpEvent();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, order);
        }
    }
}
