using System;

namespace Naninovel.E2E
{
    /// <summary>
    /// A function with a boolean result and associated assert message.
    /// </summary>
    public class Condition
    {
        public Func<bool> Result { get; }
        public Func<string> Message { get; }

        public Condition (Func<(bool result, string msg)> fn)
        {
            Result = () => fn().result;
            Message = () => fn().msg;
        }

        public static implicit operator Func<bool> (Condition d) => d.Result;
    }
}
