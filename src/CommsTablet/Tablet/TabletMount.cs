using System;
using DV;
using DV.Common;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Damage;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// How the tablet's two uses share one action (decision 18). The tablet
    /// is a stock gadget item, so its use places it on a mount; it is also
    /// the radio, whose use confirms an aim. Mounting needs a free mount (its
    /// gadget requires two mount points), so the use is a placement only
    /// while the tablet is aimed at a free mount the way the game's own
    /// placement aims, and the radio's everywhere else: a click at a car,
    /// a track, a switch, or the sky stays the radio's, with no placement
    /// preview or "requires a mount" text drawn over it.
    /// <para>
    /// Desktop: the game's placement ray (the camera's, 3 m, its layers, past
    /// broken windows), its first hit on a gadget with a <c>Mount</c> that
    /// holds nothing. VR: the tablet's back within 10 cm of a free mount (the
    /// game's proximity placement), or the hand's pointer on one where the
    /// game allows placing at a distance. Read once a frame.
    /// </para>
    /// </summary>
    internal static class TabletMount
    {
        /// <summary>The layers the game's gadget placement looks at: Default, Terrain, Interactable, Gadget_Mesh_Placing.</summary>
        internal const int PlacementMask = (1 << 0) | (1 << 8) | (1 << 13) | (1 << 29);
        private const float Reach = 3f, ProximityReach = 0.1f;

        private static int frame = -1;
        private static GadgetItem askedFor;
        private static bool aiming;

        /// <summary>Whether the tablet's use would place it now: held and aimed at a free mount.</summary>
        internal static bool AimingAtMount(GadgetItem item)
        {
            if (item == null)
                return false;
            if (frame == Time.frameCount && askedFor == item)
                return aiming;
            frame = Time.frameCount;
            askedFor = item;
            try
            {
                aiming = Compute(item);
            }
            catch (Exception e)
            {
                aiming = false;
                Main.Mod?.Logger.Warning("Could not tell whether the tablet is aimed at a mount; its use stays the radio's: " + e.GetBaseException().Message);
            }
            return aiming;
        }

        private static bool Compute(GadgetItem item)
        {
            if (!item.isActiveAndEnabled || item.GadgetPrefab == null || item.Item == null || !item.Item.IsGrabbed())
                return false;
            // Where the game would not place a gadget at all (mouse mode, an item being placed), the use is the radio's.
            if (!GadgetSystemUtility.AllowGadgetPlacement || !GameFeatureFlags.IsAllowed(GameFeatureFlags.Flag.MountingGadgets))
                return false;
            var context = GameMembers.GadgetContext?.Invoke(item);
            var reach = context != null && context.reachOverride != 0f ? context.reachOverride : Reach;
            if (VRManager.IsVREnabled())
            {
                if (Proximity(item))
                    return true;
                var pointer = context?.telegrab;
                return pointer != null && Globals.G.GameParams.VRRemoteDrivingAllowed && RayAtFreeMount(pointer.position, pointer.forward, reach);
            }
            var camera = PlayerManager.ActiveCamera;
            return camera != null && RayAtFreeMount(camera.transform.position, camera.transform.forward, reach);
        }

        private static bool RayAtFreeMount(Vector3 origin, Vector3 direction, float reach) =>
            PhysicsQueryBuilder.Raycast(origin, direction, reach, PlacementMask).Where(Placeable).TryGetFirst(out var hit) && FreeMount(hit.collider);

        // The game's proximity placement, along the one axis the tablet is placed on: its back.
        private static bool Proximity(GadgetItem item)
        {
            var bounds = item.GadgetPrefab.Bounds;
            var t = item.transform;
            var direction = t.TransformDirection(-item.PlacingAxis.normalized);
            foreach (var hit in PhysicsQueryBuilder.Boxcast(t.TransformPoint(bounds.center), bounds.extents, direction, t.rotation, ProximityReach, PlacementMask).Where(Placeable))
                if (hit.collider.GetComponentInParent<GadgetItem>() != item && FreeMount(hit.collider))
                    return true;
            return false;
        }

        /// <summary>The game's placement filter: a cab window counts as a surface until it is broken.</summary>
        private static bool Placeable(RaycastHitDV hit)
        {
            if (hit.collider.GetComponent<LocoWindowMesh>() == null)
                return true;
            var car = TrainCar.Resolve(hit.collider.gameObject);
            return car == null || !car.TryGetComponent<WindowsBreakingController>(out var windows) || !windows.windowsBroken;
        }

        private static bool FreeMount(Collider collider)
        {
            var gadget = collider != null ? collider.GetComponentInParent<GadgetBase>() : null;
            var mount = gadget != null ? gadget.GetComponent<Mount>() : null;
            return mount != null && !mount.IsInUse;
        }
    }

    /// <summary>
    /// The stock gadget item's placement, for the tablet only, while it is
    /// aimed at a free mount (<see cref="TabletMount"/>): a press elsewhere
    /// never reaches it, and its preview and texts are not drawn elsewhere.
    /// </summary>
    [HarmonyPatch(typeof(GadgetItem))]
    internal static class TabletPlacement
    {
        [HarmonyPrefix]
        [HarmonyPatch("OnUsed")]
        private static bool OnUsed(GadgetItem __instance) => !IsTablet(__instance) || TabletMount.AimingAtMount(__instance);

        [HarmonyPrefix]
        [HarmonyPatch("Update")]
        private static bool Update(GadgetItem __instance)
        {
            if (!IsTablet(__instance) || TabletMount.AimingAtMount(__instance))
                return true;
            // In VR a placing press ends at its release: let a press begun at a mount finish there.
            var context = GameMembers.GadgetContext?.Invoke(__instance);
            if (context != null && VRManager.IsVREnabled() && (context.isPressed || context.wasPressed))
                return true;
            if (GameMembers.GadgetPlace != null)
                GameMembers.GadgetPlace(__instance) = false;
            return false;
        }

        private static bool IsTablet(GadgetItem item) => item.TryGetComponent<TabletRadio>(out _);
    }
}

namespace CommsTablet.Tablet
{
    /// <summary>
    /// The game shows a mounted gadget it would take off with an empty hand
    /// in red wherever the player points at it. On the mounted tablet a click
    /// on one of its screen's buttons presses the button (the game's grabber
    /// takes it first) and never takes the tablet off, so while the pointer
    /// is on one of them the red is left off and the screen stays readable;
    /// anywhere else on the tablet it shows, as the click there takes it off.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(DV.Customization.Gadgets.GadgetHandBase), "OnUpdate")]
    internal static class MountedScreenHighlight
    {
        private static void Prefix(DV.Customization.Gadgets.GadgetHandBase __instance, ref DV.Customization.Gadgets.GadgetBase target)
        {
            if (!(target is TabletGadget) || !(__instance is DV.Customization.Gadgets.GadgetHandNonVR hand) || hand.grabber == null)
                return;
            var pointed = hand.grabber.Raycaster?.CurrentlyRaycasted;
            if (pointed != null && pointed.transform.IsChildOf(target.transform))
                target = null;
        }
    }
}
