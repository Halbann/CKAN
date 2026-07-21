using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CKAN.ConsoleUI.Toolkit {

    /// <summary>
    /// The console UI's input source.
    /// A background thread blocks in Console.ReadKey and queues each keystroke 
    /// (that thread can never be stopped because Console.ReadKey can't be interrupted).
    /// 
    /// Other threads can post actions onto the same queue to run them on the UI thread.
    /// NextKey runs posted actions inline while waiting for the next keystroke.
    ///
    /// Originally added so that we can respond to the URL handler pipe while waiting on input.
    /// </summary>
    public static class ConsoleInput {

        /// <summary>
        /// Start the background reader thread.
        /// </summary>
        public static void Start()
        {
            if (readerThread != null) {
                return;
            }

            readerThread = new Thread(ReadLoop) {
                IsBackground = true,
                Name = "ConsoleInputReader",
            };

            readerThread.Start();
        }

        /// <summary>
        /// Run an action on the UI thread. Safe to call from any thread.
        /// </summary>
        /// <param name="action">Work to run on the UI thread</param>
        public static void Post(Action action)
        {
            queue.Add(new Event(null, action));
        }

        /// <summary>
        /// Block until the next event and pump it. Runs a posted action or returns a keystroke.
        /// For loops that redraw between events.
        /// </summary>
        /// <returns>The keystroke, or null if a posted action ran instead</returns>
        public static ConsoleKeyInfo? PumpEvent()
        {
            var ev = queue.Take();
            if (ev.Key is ConsoleKeyInfo k) {
                return k;
            }

            ev.Action?.Invoke();
            return null;
        }

        /// <summary>
        /// Block until the next keystroke, running any posted actions in the meantime.
        /// For callers that just need a keystroke and have nothing to repaint.
        /// </summary>
        /// <returns>The next keystroke</returns>
        public static ConsoleKeyInfo NextKey()
        {
            while (true) {
                if (PumpEvent() is ConsoleKeyInfo k) {
                    return k;
                }
            }
        }

        private static void ReadLoop()
        {
            while (true) {
                queue.Add(new Event(Console.ReadKey(true), null));
            }
        }

        /// <summary>
        /// A queued keystroke or posted action.
        /// </summary>
        private sealed class Event {
            public readonly ConsoleKeyInfo? Key;
            public readonly Action? Action;

            public Event(ConsoleKeyInfo? key, Action? action)
            {
                Key = key;
                Action = action;
            }
        }

        private static readonly BlockingCollection<Event> queue
            = new BlockingCollection<Event>(new ConcurrentQueue<Event>());

        private static Thread? readerThread;
    }
}
