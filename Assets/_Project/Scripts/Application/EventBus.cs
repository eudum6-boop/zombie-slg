using System;
using System.Collections.Generic;

namespace ZombieSLG.Application
{
    /// <summary>
    /// Minimal typed publish/subscribe bus. Systems talk through messages instead of holding
    /// references to each other (e.g. the HUD reacts to an upgrade without knowing the base service).
    /// </summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();
        private readonly List<Delegate> _dispatchBuffer = new List<Delegate>();

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Delegate>();
                _handlers[typeof(T)] = list;
            }
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public void Publish<T>(T message)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
            // Copy so handlers can (un)subscribe while being dispatched.
            _dispatchBuffer.Clear();
            _dispatchBuffer.AddRange(list);
            var snapshot = _dispatchBuffer.ToArray();
            for (int i = 0; i < snapshot.Length; i++) ((Action<T>)snapshot[i])(message);
        }

        public int HandlerCount<T>() => _handlers.TryGetValue(typeof(T), out var list) ? list.Count : 0;

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;
            public Subscription(Action dispose) { _dispose = dispose; }
            public void Dispose() { _dispose?.Invoke(); _dispose = null; }
        }
    }
}
