using System;
using System.Collections;

namespace Naninovel.E2E
{
    /// <summary>
    /// A queue of tasks to complete in order during test playback.
    /// </summary>
    public interface ISequence : IEnumerator
    {
        /// <summary>
        /// Adds specified task to the sequence playback queue.
        /// </summary>
        ISequence Enqueue (Action task);
        /// <summary>
        /// Adds specified async task to the sequence playback queue.
        /// </summary>
        ISequence Enqueue (Func<UniTask> task);
    }
}
