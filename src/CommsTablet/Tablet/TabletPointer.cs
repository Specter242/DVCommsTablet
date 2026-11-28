using System;
using System.Reflection;
using DV.CabControls;
using DV.Utils;
using DV.VR;
using HarmonyLib;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// The VR pointer on the tablet (tracker #46; Jiitensha on Nexus, "Small
    /// buttons are impossible to click on"): the game's own telegrab beam,
    /// held on the grip with an empty hand, presses the cab button it points
    /// at with the trigger, and every tablet button is a cab button. The beam
    /// is a sphere cast 30 mm across, wider than the smallest plates (16 mm),
    /// so it takes whichever plate the sphere touches first. On the tablet the
    /// pointed button is chosen again here by the beam's center line: the
    /// plate where it meets the screen, and the beam's dot put on that point.
    /// <para>
    /// The tablet's buttons take the beam whether or not the difficulty
    /// allows VR remote driving: it is the player's own handheld screen. Off
    /// the tablet the beam is the game's, unchanged.
    /// </para>
    /// </summary>
    [HarmonyPatch(typeof(TeleGrab), "UpdateTelegrabData")]
    internal static class TabletPointer
    {
        private static readonly MethodInfo SetData = AccessTools.PropertySetter(typeof(TeleGrab), nameof(TeleGrab.CurrentTelegrabData));
        private static readonly AccessTools.FieldRef<TelegrabbableInteractionTarget, TelegrabInteractionHandler> Handler =
            AccessTools.FieldRefAccess<TelegrabbableInteractionTarget, TelegrabInteractionHandler>("handler");
        private static readonly RaycastHit[] hits = new RaycastHit[128];
        private static readonly System.Collections.Generic.List<BoxCollider> boxes = new System.Collections.Generic.List<BoxCollider>();
        private static bool failed;

        private static void Postfix(TeleGrab __instance)
        {
            if (failed || SetData == null)
                return;
            try
            {
                Repoint(__instance);
            }
            catch (Exception e)
            {
                failed = true;
                Main.Mod?.Logger.LogException("The tablet's VR pointer failed; the beam is the game's alone for the rest of the session", e);
            }
        }

        private static void Repoint(TeleGrab grab)
        {
            var plate = PlateAt(new Ray(grab.transform.position, grab.transform.forward), grab.maxDistance, out var hit);
            var target = plate != null ? plate.GetComponent<TelegrabbableInteractionTarget>() : null;
            if (target == null || Handler(target) != null)
                return;
            var control = plate.GetComponent<ControlImplBase>();
            if (control != null && !control.InteractionAllowed)
                return;
            var data = grab.CurrentTelegrabData;
            // Something well nearer than the screen (a lever, a door) keeps the beam; the tablet's own frame and buttons are within the slack.
            if (data.PointedGameObject != null && data.PointedTeleinteractable != target && data.SphereCastHit.distance + Slack < hit.distance)
                return;
            if (data.PointedTeleinteractable != null && data.PointedTeleinteractable != target)
                data.PointedTeleinteractable.SetHighlight(on: false);
            if (data.PointedTelegrabbable != null)
                data.PointedTelegrabbable.SetHighlight(on: false);
            target.SetHighlight(on: true);
            // The tablet itself is not offered for a pull while one of its buttons is pointed: the trigger is for the button.
            SetData.Invoke(grab, new object[] { new TeleGrab.TelegrabData(null, target, target.gameObject, hit) });
        }

        /// <summary>How much nearer than the screen something else may be and still be passed over.</summary>
        private const float Slack = 0.03f;

        /// <summary>
        /// The tablet button whose plate a ray meets the screen on, and that
        /// point as a hit: the screen of the nearest tablet button along the
        /// ray, the point where the ray crosses that screen, and the shown
        /// button whose plate holds it. Null between plates and off the
        /// tablet. The button is the object with the game's control on it.
        /// </summary>
        internal static GameObject PlateAt(Ray ray, float maxDistance, out RaycastHitDV hit)
        {
            hit = default;
            var count = Physics.RaycastNonAlloc(ray, hits, maxDistance, 1 << TabletItem.ControlLayer, QueryTriggerInteraction.Collide);
            TabletScreen screen = null;
            var first = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var s = hits[i].collider.GetComponentInParent<TabletScreen>();
                if (s != null && hits[i].distance < first)
                {
                    first = hits[i].distance;
                    screen = s;
                }
            }
            if (screen == null || !screen.PointOnScreen(ray, out var point, out var distance))
                return null;
            // Every shown button of that screen, not only the boxes the line crossed: slanted, it can
            // reach the screen just inside a plate's edge while crossing only its neighbor's box.
            screen.GetComponentsInChildren(false, boxes);
            foreach (var box in boxes)
            {
                if (!box.enabled || box.gameObject.layer != TabletItem.ControlLayer || !Holds(box, point))
                    continue;
                var button = box.GetComponentInParent<TabletButton>();
                if (button == null || button.GetComponentInParent<TabletScreen>() != screen)
                    continue;
                hit = new RaycastHitDV(box, point, -screen.transform.forward, distance);
                return button.gameObject;
            }
            return null;
        }

        /// <summary>Whether a point lies over a button's plate: inside its box across, at any depth.</summary>
        internal static bool Holds(BoxCollider box, Vector3 world)
        {
            var local = box.transform.InverseTransformPoint(world) - box.center;
            return Mathf.Abs(local.x) <= box.size.x * 0.5f && Mathf.Abs(local.y) <= box.size.y * 0.5f;
        }
    }
}
