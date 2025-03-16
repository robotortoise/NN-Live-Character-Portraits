using System;
using System.Collections.Generic;

namespace Naninovel.E2E
{
    /// <inheritdoc cref="ISequence"/>
    public class Sequence : ISequence
    {
        public object Current { get; private set; }

        private readonly List<Func<UniTask>> initial = new();
        private readonly Queue<Func<UniTask>> current = new();

        public bool MoveNext ()
        {
            if (current.Count == 0)
            {
                Reset();
                return false;
            }
            Current = current.Dequeue()().ToCoroutine();
            return true;
        }

        public void Reset ()
        {
            for (int i = initial.Count - 1; i >= 0; i--)
                current.Enqueue(initial[i]);
        }

        public ISequence Enqueue (Func<UniTask> task)
        {
            initial.Add(task);
            current.Enqueue(task);
            return this;
        }

        public ISequence Enqueue (Action task) => Enqueue(() => {
            task();
            return UniTask.CompletedTask;
        });
    }
}
