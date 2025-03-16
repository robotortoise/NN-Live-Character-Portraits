using UnityEngine;
using Naninovel;
using Naninovel.FX;
using Naninovel.Utilities;
using UnityEngine.Scripting;

namespace UserInterface.JordanCharacterImplementation
{
    public class JordanLayeredCharacterBehaviour : LayeredCharacterBehaviour
    {
        public void SyncWithOriginal(JordanLayeredCharacterBehaviour original)
        {
            if (original == null) return;

            // Sync composition and appearance
            ApplyComposition(original.Composition);
            NotifyAppearanceChanged(original.DefaultAppearance);
            NotifyPerceivedVisibilityChanged(original.gameObject.activeSelf);

            // Copy animation state
            var originalAnimator = original.GetComponent<Animator>();
            var cloneAnimator = GetComponent<Animator>();
            if (originalAnimator != null && cloneAnimator != null)
            {
                cloneAnimator.runtimeAnimatorController = originalAnimator.runtimeAnimatorController;
                var originalStateInfo = originalAnimator.GetCurrentAnimatorStateInfo(0);
                cloneAnimator.Play(originalStateInfo.fullPathHash, 0, originalStateInfo.normalizedTime);
            }
        }
    }
}
