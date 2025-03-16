using System;

namespace Naninovel
{
    /// <summary>
    /// Holder of the resources associated with a <see cref="LocalizableText"/> reference,
    /// compositing both the holder object and the text-specific context.
    /// </summary>
    /// <remarks>
    /// Using just the holder object to track resource dependencies is not sufficient,
    /// as multiple texts may reference single scenario script or localization document,
    /// so we have to also use unique text ID (when stable) or playback spot (when volatile).
    /// </remarks>
    public class LocalizableTextHolder : IEquatable<LocalizableTextHolder>
    {
        /// <summary>
        /// The held text discriminating the holder by text ID and playback spot.
        /// </summary>
        public LocalizableText Text { get; }
        /// <summary>
        /// The actual object that was specified as holder, discriminating by the
        /// instance that's depending on the held text.
        /// </summary>
        public object Holder { get; }

        public LocalizableTextHolder (LocalizableText text, object holder)
        {
            Text = text;
            Holder = holder;
        }

        public bool Equals (LocalizableTextHolder other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Text.Equals(other.Text) && Equals(Holder, other.Holder);
        }

        public override bool Equals (object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((LocalizableTextHolder)obj);
        }

        public override int GetHashCode ()
        {
            return HashCode.Combine(Text, Holder);
        }

        public override string ToString ()
        {
            return $"{Holder} -> {Text}";
        }
    }
}
