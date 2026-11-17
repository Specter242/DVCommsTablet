using System;
using System.Collections.Generic;
using CommsTablet.Tablet;
using DV;
using DV.Utils;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>
    /// An app for one of the radio's point-and-press modes (decision 11): no
    /// list to choose from. Its screen shows the mode's display large, how to
    /// use it, and a few buttons for what the radio does with its scroll
    /// buttons. Aiming, confirming, and paying stay on the tablet's use, where
    /// the stock mode checks them each frame.
    /// </summary>
    internal abstract class ToolPage : TabletPage
    {
        protected static readonly string[] None = Array.Empty<string>();
        protected const string StartOver = "START OVER";

        protected ToolPage(TabletRadio radio) : base(radio) { }

        internal override bool Readout => true;

        internal override int Selected => -1;

        internal override string EmptyText => "";

        internal override void Refresh() => tiles.Clear();

        internal override void Press(int tile) { }

        internal override void Capture() { }

        internal override void Reapply() { }

        /// <summary>Whether the mode is in a step where the radio's scroll buttons act.</summary>
        protected bool Stepping => Mode != null && Mode.ButtonBehaviour == ButtonBehaviourType.Override;
    }

    /// <summary>The radio's Switch mode: point at a switch and use the tablet to throw it.</summary>
    internal sealed class SwitchPage : ToolPage
    {
        internal SwitchPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "SWITCH";


        internal override ICommsRadioMode Mode => Radio.Switch;

        internal override bool Available =>
            Radio.Switch != null && Globals.G.GameParams.SwitchModeAllowed && Radios.Allow(r => r.switchControl);

        internal override string Guide => "Point at a switch and use the tablet to throw it.";
    }

    /// <summary>
    /// The radio's Rerail mode: use the tablet at a derailed car, aim at the
    /// track to put it on, and use it at the marker to pay and rerail.
    /// </summary>
    internal sealed class RerailPage : ToolPage
    {
        private static readonly string[] Aiming = { "FLIP", StartOver };
        private static readonly string[] Started = { StartOver };

        internal RerailPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "RERAIL";


        internal override ICommsRadioMode Mode => Radio.Rerail;

        // As the radio: always offered, but the tutorial's limits are set on the radio's own mode only.
        internal override bool Available =>
            Radio.Rerail != null && !TutorialHelper.InRestrictedMode && Radios.Allow(r => r.rerailControl);

        internal override string Guide =>
            "Point at a derailed car and use the tablet. Aim at the track to put it on, then use the tablet at the marker to pay and rerail.";

        internal override IReadOnlyList<string> Actions => Stepping ? Aiming : Started;

        internal override void Act(int action)
        {
            if (Stepping && action == 0)
                Radio.Step(true);
            else
                Radio.Restart();
        }
    }

    /// <summary>The radio's Clear mode: use the tablet at a car, then at it again to pay and clear it.</summary>
    internal sealed class ClearPage : ToolPage
    {
        private static readonly string[] Started = { StartOver };

        internal ClearPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "CLEAR";


        internal override ICommsRadioMode Mode => Radio.Clear;

        internal override bool Available =>
            Radio.Clear != null && !TutorialHelper.InRestrictedMode && Radios.Allow(r => r.deleteControl);

        internal override string Guide => "Point at a car and use the tablet; use it at the car again to pay and clear it.";

        internal override IReadOnlyList<string> Actions => Started;

        internal override void Act(int action) => Radio.Restart();
    }

    /// <summary>The radio's Startup mode: point at a locomotive and use the tablet to start it.</summary>
    internal sealed class StartupPage : ToolPage
    {
        internal StartupPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "STARTUP";


        internal override ICommsRadioMode Mode => Radio.Startup;

        internal override bool Available =>
            Radio.Startup != null && Session.CheatMode && Radios.Allow(r => r.carStartupControl);

        internal override string Guide => "Point at a locomotive and use the tablet to start it up.";
    }

    /// <summary>
    /// The radio's Damage mode: arm it, set the damage (below 10 percent it
    /// derails instead), then use the tablet at a car.
    /// </summary>
    internal sealed class DamagePage : ToolPage
    {
        private static readonly string[] Idle = { "ARM" };
        private static readonly string[] Armed = { "LESS", "MORE", StartOver };

        internal DamagePage(TabletRadio radio) : base(radio) { }

        internal override string Name => "DAMAGE";


        internal override ICommsRadioMode Mode => Radio.Damage;

        internal override bool Available =>
            Radio.Damage != null && Session.CheatMode && Radios.Allow(r => r.carDamageControl);

        internal override string Guide =>
            "Press ARM, set the damage with LESS and MORE (below 10%, it derails instead), then point at a car and use the tablet.";

        internal override IReadOnlyList<string> Actions => Stepping ? Armed : Idle;

        internal override void Act(int action)
        {
            if (!Stepping)
                Radio.Use();
            else if (action == 0)
                Radio.Step(false);
            else if (action == 1)
                Radio.Step(true);
            else
                Radio.Restart();
        }
    }
}
