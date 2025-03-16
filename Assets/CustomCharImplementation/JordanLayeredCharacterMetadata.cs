using Naninovel;
using UnityEngine;

namespace UserInterface.JordanCharacterImplementation
{
    /// <summary>
    /// Custom metadata for JordanLayeredCharacter.
    /// This ensures the actor appears in the Naninovel implementation dropdown.
    /// </summary>
    [ActorResources(typeof(JordanLayeredCharacter), false)] // Ensures metadata maps correctly
    public class JordanLayeredCharacterMetadata : CustomMetadata<JordanLayeredCharacter>
    {
        [Tooltip("If true, the clone system will be enabled.")]
        public bool EnableCloneSystem = true;

        [Tooltip("Defines the default render layer for clones.")]
        public string DefaultCloneLayer = "Naninovel 1";
    }
}
