using UnityEngine;
using System.Collections.Generic;
using Naninovel;
using Naninovel.FX;
using Naninovel.Utilities;
using Naninovel.Commands;
using UnityEngine.Scripting;

namespace UserInterface.JordanCharacterImplementation
{
    [ActorResources(typeof(JordanLayeredCharacterBehaviour), false)]
    public class JordanLayeredCharacter
        : LayeredActor<JordanLayeredCharacterBehaviour, CharacterMetadata>,
          ICharacterActor,
          Naninovel.Commands.LipSync.IReceiver
    {
        private static readonly string[] RenderLayers = { "Naninovel 1", "Naninovel 2", "Naninovel 3", "Naninovel 4" };
        private static int currentLayerIndex = 0;

        private JordanLayeredCharacterMetadata customData;
        private GameObject characterClone;
        private CharacterLipSyncer lipSyncer;

        public CharacterLookDirection LookDirection
        {
            get => TransitionalRenderer.GetLookDirection(ActorMeta.BakedLookDirection);
            set => TransitionalRenderer.SetLookDirection(value, ActorMeta.BakedLookDirection);
        }

        public JordanLayeredCharacter(
            string id,
            CharacterMetadata meta,
            EmbeddedAppearanceLoader<GameObject> loader
        ) : base(id, meta, loader)
        {
            // Fetch custom data from the actor metadata
            customData = meta.GetCustomData<JordanLayeredCharacterMetadata>();
        }

        public override async global::Naninovel.UniTask Initialize()
        {
            await base.Initialize();
            lipSyncer = new(Id, Behaviour.NotifyIsSpeakingChanged);

            // If the custom data says to enable the clone system, create the clone
            if (customData?.EnableCloneSystem == true)
                CreateOrUpdateClone();
        }

        public override async global::Naninovel.UniTask ChangeVisibility(
            bool visible, Tween tween, AsyncToken token = default)
        {
            await base.ChangeVisibility(visible, tween, token);

            if (!(customData?.EnableCloneSystem ?? false))
                return;

            if (visible) CreateOrUpdateClone();
            else DestroyClone();
        }

        public void CreateOrUpdateClone()
        {
            if (characterClone != null)
                DestroyClone();

            // Clone the character prefab
            characterClone = Object.Instantiate(Behaviour.gameObject);
            characterClone.name = $"{Id}_Clone";

            // If transitional sprite rendering is needed
            CopyComponent<TransitionalSpriteRenderer>(Behaviour.gameObject, characterClone);

            // Either use custom data layer or cycle through array
            string assignedLayer = customData?.DefaultCloneLayer ?? RenderLayers[currentLayerIndex];
            currentLayerIndex = (currentLayerIndex + 1) % RenderLayers.Length;
            SetLayerRecursively(characterClone, LayerMask.NameToLayer(assignedLayer));

            // Detach so it doesn’t mirror position
            characterClone.transform.SetParent(null);
            characterClone.transform.position = Behaviour.transform.position;
            characterClone.transform.localScale = Behaviour.transform.localScale;

            // Sync other properties
            if (characterClone.TryGetComponent(out JordanLayeredCharacterBehaviour cloneBehaviour))
                cloneBehaviour.SyncWithOriginal(Behaviour);
        }

        // Utility to copy a component from original to target
        private void CopyComponent<T>(GameObject original, GameObject target) where T : Component
        {
            T originalComponent = original.GetComponent<T>();
            if (originalComponent != null)
            {
                T copiedComponent = target.AddComponent<T>();
                var fields = typeof(T).GetFields();
                foreach (var field in fields)
                    field.SetValue(copiedComponent, field.GetValue(originalComponent));
            }
        }

        public void DestroyClone()
        {
            if (characterClone)
            {
                Object.Destroy(characterClone);
                characterClone = null;
            }
        }

        public async global::Naninovel.UniTask ChangeLookDirection(
            CharacterLookDirection lookDirection,
            Tween tween,
            AsyncToken token = default
        )
        {
            await TransitionalRenderer.ChangeLookDirection(lookDirection, ActorMeta.BakedLookDirection, tween, token);
        }

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        public override void Dispose()
        {
            base.Dispose();
            lipSyncer?.Dispose();
            DestroyClone();
        }

        public void AllowLipSync(bool active)
        {
            if (lipSyncer != null)
                lipSyncer.SyncAllowed = active;
        }
    }
}
