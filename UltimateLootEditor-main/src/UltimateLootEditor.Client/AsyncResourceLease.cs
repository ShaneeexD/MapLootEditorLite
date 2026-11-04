using System;
using System.Threading.Tasks;

namespace ULE.SpawnEditor
{
    // Create, cancel and take on the owning (Unity) thread. Await preserves that context.
    // Cancellation releases a result even if its consumer coroutine has been destroyed.
    internal sealed class AsyncResourceLease<T> where T : class
    {
        private readonly Action<T> _release;
        private bool _cancelled;
        private T _result;
        internal Task Completion { get; }
        internal AsyncResourceLease(Func<Task<T>> create, Action<T> release)
        {
            _release = release;
            Completion = Load(create);
        }
        private async Task Load(Func<Task<T>> create)
        {
            var result = await create();
            if (_cancelled) { if (result != null) _release(result); }
            else _result = result;
        }
        internal T Take()
        {
            var result = _result;
            _result = null;
            return result;
        }
        internal void Cancel()
        {
            _cancelled = true;
            var result = Take();
            if (result != null) _release(result);
            // Observe failure even when no coroutine remains to consume Completion.
            Completion.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
