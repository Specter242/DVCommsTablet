using System;
using DV.CabControls;
using DV.Interaction;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// A screen button: the game's own cab button, so a mouse and VR hands
    /// press it alike. Realistic Firing's <c>FiringTabletPageButton</c> and
    /// <c>FiringTabletSetup.ConfigurePageButton</c> (d0f162e9).
    /// </summary>
    internal sealed class TabletButton : MonoBehaviour
    {
        internal Action Pressed;
        private ControlImplBase control;

        internal static void Configure(DV.CabControls.Spec.Button spec, GameObject collider)
        {
            // Stock VR controls read this during Awake before completing their
            // touch setup; runtime-added specs do not deserialize it.
            spec.handPosesOverride = new InteractionHandPoses();
            spec.colliderGameObjects = new[] { collider };
            spec.createRigidbody = false;
            spec.useJoints = false;
        }

        private void Start()
        {
            control = GetComponent<ControlImplBase>();
            if (control != null)
                control.ValueChanged += Changed;
        }

        private void Changed(ValueChangedEventArgs change)
        {
            if (change.newValue >= 0.5f && change.oldValue < 0.5f)
                Pressed?.Invoke();
        }

        // The stock button springs back from a coroutine on the next frame. A
        // press that hides its own button (an app icon opening its app) stops
        // that coroutine and leaves the button held down, deaf to the next
        // press, so it is let up whenever it is hidden or shown again.
        private void OnDisable() => LetUp();

        private void OnEnable() => LetUp();

        private void LetUp()
        {
            if (control != null && control.Value >= 0.5f)
                control.SetValue(0f);
        }

        private void OnDestroy()
        {
            if (control != null)
                control.ValueChanged -= Changed;
        }
    }
}
