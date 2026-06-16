// Routes callbacks from background WebSocket thread to Unity main thread.
// Required because all UnityEngine/UnityEditor API calls must run on the main thread.
using System;
using System.Collections.Concurrent;
using UnityEditor;

namespace BurgerMonster.ClaudeAgent
{
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        static MainThreadDispatcher()
        {
            EditorApplication.update += Flush;
        }

        // Call from any thread; the action runs on the next editor update tick.
        public static void Enqueue(Action action) => _queue.Enqueue(action);

        static void Flush()
        {
            while (_queue.TryDequeue(out var action))
                action();
        }
    }
}
