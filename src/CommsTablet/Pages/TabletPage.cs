using System.Collections.Generic;
using CommsTablet.Tablet;
using DV;
using DV.Customization.Paint;
using UnityEngine;

namespace CommsTablet.Pages
{
    /// <summary>One choice on a page's grid.</summary>
    internal readonly struct Tile
    {
        public readonly string Label;
        /// <summary>The picture until a side view of <see cref="Livery"/> is drawn: the game's icon.</summary>
        public readonly Sprite Picture;
        /// <summary>The vehicle to show a side view of, if any.</summary>
        public readonly DV.ThingTypes.TrainCarLivery Livery;
        /// <summary>A tile that leads back up a level instead of choosing.</summary>
        public readonly bool Back;
        /// <summary>The paint the side view shows <see cref="Livery"/> in, if any.</summary>
        public readonly PaintTheme Theme;
        /// <summary>The car a themed view is for: Skin Manager paints by the car's own data.</summary>
        public readonly TrainCar Car;

        public Tile(string label, Sprite picture, DV.ThingTypes.TrainCarLivery livery = null, bool back = false,
            PaintTheme theme = null, TrainCar car = null)
        {
            Label = label;
            Picture = picture;
            Livery = livery;
            Back = back;
            Theme = theme;
            Car = car;
        }
    }

    /// <summary>
    /// An app of the tablet, opened from the home screen: one stock radio
    /// mode's list as a grid. The page
    /// reads the mode's own list, so modded vehicles appear as the radio sees
    /// them, and drives the mode to its aiming step through
    /// <see cref="TabletRadio.Drive"/>. Pages sit behind this class so they
    /// can later move onto a shared tablet (decision 2).
    /// </summary>
    internal abstract class TabletPage
    {
        protected readonly TabletRadio Radio;
        protected readonly List<Tile> tiles = new List<Tile>();

        protected TabletPage(TabletRadio radio) => Radio = radio;

        /// <summary>The app's name, as its screen's heading.</summary>
        internal abstract string Name { get; }

        /// <summary>The page's tab in its app (<see cref="TabletApp"/>).</summary>
        internal virtual string Tab => Name;

        /// <summary>The app's name under its icon on the home screen.</summary>
        internal virtual string Label => Name.Length > 1 ? Name.Substring(0, 1) + Name.Substring(1).ToLowerInvariant() : Name;


        /// <summary>The tablet's copy of the stock mode this page drives.</summary>
        internal abstract ICommsRadioMode Mode { get; }

        /// <summary>Whether the radio would offer this mode now, and the members it needs resolved.</summary>
        internal abstract bool Available { get; }

        /// <summary>Sub-lists the page switches between ("LOCOS", "CARS"), or none.</summary>
        internal virtual string[] Sections => null;

        protected int section;

        /// <summary>The section shown, marked on the screen.</summary>
        internal virtual int Section => section;

        /// <summary>The first tile shown, for the screen's paging.</summary>
        internal int Scroll;

        internal IReadOnlyList<Tile> Tiles => tiles;

        /// <summary>The tile of the choice the mode is aiming with, or -1.</summary>
        internal abstract int Selected { get; }

        /// <summary>Whether the screen lights <paramref name="tile"/>: the selected one, or (Change) each one that is on.</summary>
        internal virtual bool IsOn(int tile) => tile == Selected;

        /// <summary>A short message about the last choice, shown until the next one.</summary>
        internal string Notice;

        /// <summary>A line under the grid about how the page works, if any.</summary>
        internal virtual string Hint => null;

        /// <summary>What the screen says when the page has no tiles.</summary>
        internal virtual string EmptyText => "Nothing to show.";

        internal virtual void SelectSection(int index)
        {
            section = index;
            Scroll = 0;
            Notice = null;
        }

        /// <summary>
        /// An app without a list (the radio's point-and-press modes): its
        /// screen shows the mode's display large, <see cref="Guide"/>, and
        /// <see cref="Actions"/>, instead of a grid.
        /// </summary>
        internal virtual bool Readout => false;

        /// <summary>How to use a <see cref="Readout"/> app.</summary>
        internal virtual string Guide => null;

        /// <summary>A <see cref="Readout"/> app's buttons, as they stand now.</summary>
        internal virtual IReadOnlyList<string> Actions => null;

        /// <summary>Acts on a pressed button of <see cref="Actions"/>.</summary>
        internal virtual void Act(int action) { }

        /// <summary>
        /// A page that draws its own screen (the map) on the screen's
        /// immediate-mode layer (<see cref="TabletSurface"/>), in place of
        /// sections, a grid, and buttons: <see cref="Draw"/> is its pass.
        /// </summary>
        internal virtual bool DrawsItself => false;

        /// <summary>A pass of a page that <see cref="DrawsItself"/>, its plates drawn with <see cref="TabletSkin"/>.</summary>
        internal virtual void Draw() { }

        /// <summary>The part of the canvas (pixels) where a press goes to <see cref="PressAt"/> at the point pressed, not to a plate; empty for none.</summary>
        internal virtual Rect PressArea => Rect.zero;

        /// <summary>A press in <see cref="PressArea"/>, at the canvas point the player pointed at.</summary>
        internal virtual void PressAt(Vector2 canvas) { }

        /// <summary>The status line of an app with no radio mode.</summary>
        internal virtual string Status => null;

        /// <summary>After the tablet is used (a click or trigger) on this page's mode.</summary>
        internal virtual void Used() { }

        /// <summary>Rebuilds <see cref="Tiles"/> from the mode's lists.</summary>
        internal abstract void Refresh();

        /// <summary>Acts on a pressed tile: goes into or out of a level, or chooses and steps the mode to aiming.</summary>
        internal abstract void Press(int tile);

        /// <summary>Notes the mode's current choice before it resets, when the tablet is put away.</summary>
        internal abstract void Capture();

        /// <summary>Steps the mode back to aiming with the captured choice, when the tablet is taken up again.</summary>
        internal abstract void Reapply();
    }
}
