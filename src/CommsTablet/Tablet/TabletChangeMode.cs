using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.Localization;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// The tablet's Change mode, for DV Car Changer, car first
    /// (<see cref="TabletCarMode"/>): use the tablet at a car Car Changer has
    /// modifications for, and its app lists them; a tile switches one on or
    /// off. Used at the chosen car again, the mode switches the one last
    /// stepped to with the scroll wheel, as Car Changer's radio mode does.
    /// <para>
    /// Car Changer's rules, step by step as its radio mode
    /// (<c>CommsRadioCarChanger</c>) takes them: a change that is on goes
    /// off; one that goes with every change on goes on; one that clashes asks
    /// first ("Override?"), and asked again, the changes it clashes with go
    /// off and it goes on the next frame. Car Changer saves the changes on
    /// each car with the game (<see cref="CarChangerLink"/>).
    /// </para>
    /// </summary>
    internal sealed class TabletChangeMode : TabletCarMode
    {
        private const float PollInterval = 0.25f;

        private readonly List<CarChangerLink.Modification> modifications = new List<CarChangerLink.Modification>();
        // Destroyed this frame: off already, though Car Changer still finds them until the frame ends.
        private readonly HashSet<Object> leaving = new HashSet<Object>();
        // Exact validated components awaiting confirmation for Pending.
        private List<Object> pendingRemovals;
        // Put on the frame after an override took the clashing ones off.
        private CarChangerLink.Modification adding;
        private (string content, string action) shown;
        private (TrainCar car, TrainCar pointed, CarChangerLink.Modification last, CarChangerLink.Modification pending, CarChangerLink.Modification adding, int version) shownKey;
        private bool displayValid;
        private float nextPollAt;
        private int stateVersion;
        private TrainCar onCar;
        private List<object> cachedOn;
        private float onPollAt = -1f;

        /// <summary>The chosen car's modifications, by name.</summary>
        internal IReadOnlyList<CarChangerLink.Modification> Modifications => modifications;

        /// <summary>The modification last stepped to with the wheel or pressed, if any.</summary>
        internal CarChangerLink.Modification Last { get; private set; }

        /// <summary>A modification that clashes with changes on the car, waiting to be asked again to replace them.</summary>
        internal CarChangerLink.Modification Pending { get; private set; }

        /// <summary>What the last request needs, or why it was refused.</summary>
        internal string Reason { get; private set; }

        /// <summary>Car Changer's own beam: every color in turn.</summary>
        public override Color GetLaserBeamColor() => Color.HSVToRGB(Time.time % 1f, 1f, 1f);

        protected override bool Fits(TrainCar car) => CarChangerLink.Has(car);

        protected override void OnChosen()
        {
            modifications.Clear();
            modifications.AddRange(CarChangerLink.For(Car));
            Last = Pending = adding = null;
            pendingRemovals = null;
            Reason = null;
            leaving.Clear();
            onCar = null;
            cachedOn = null;
            onPollAt = -1f;
            stateVersion++;
        }

        protected override void OnReleased()
        {
            modifications.Clear();
            Last = Pending = adding = null;
            pendingRemovals = null;
            Reason = null;
            leaving.Clear();
            onCar = null;
            cachedOn = null;
            onPollAt = -1f;
            stateVersion++;
        }

        protected override void ApplyLast()
        {
            if (Last != null)
                Toggle(Last);
        }

        protected override bool Step(int by)
        {
            if (modifications.Count == 0)
                return false;
            var index = Last != null ? modifications.IndexOf(Last) : -1;
            index = index < 0 ? (by > 0 ? 0 : modifications.Count - 1) : (index + by + modifications.Count) % modifications.Count;
            Last = modifications[index];
            // As the radio's override question, stepping away answers no.
            Pending = null;
            pendingRemovals = null;
            Reason = null;
            Redisplay();
            return true;
        }

        /// <summary>The configs on the chosen car now: Car Changer's list, less those going this frame, and one about to go on.</summary>
        private List<object> OnNow()
        {
            if (Car == null)
                return new List<object>();
            leaving.RemoveWhere(c => c == null);
            var on = CarChangerLink.On(Car, leaving);
            if (adding != null && !on.Contains(adding.Config))
                on.Add(adding.Config);
            return on;
        }

        private List<object> CachedOnNow()
        {
            if (Car == null)
                return null;
            var now = Time.unscaledTime;
            if (onCar == Car && cachedOn != null && now < onPollAt)
                return cachedOn;
            cachedOn = OnNow();
            onCar = Car;
            onPollAt = now + PollInterval;
            return cachedOn;
        }

        /// <summary>Whether <paramref name="modification"/> is on the chosen car (or going on the next frame).</summary>
        internal bool IsOn(CarChangerLink.Modification modification)
        {
            if (modification == null)
                return false;
            var on = CachedOnNow();
            return on != null && on.Contains(modification.Config);
        }

        /// <summary>
        /// Switches <paramref name="modification"/> on the chosen car, by Car
        /// Changer's rules. False, with <see cref="Reason"/>, when it clashes
        /// and waits to be asked again, or cannot be done.
        /// </summary>
        internal bool Toggle(CarChangerLink.Modification modification)
        {
            if (Car == null || modification == null || !modifications.Contains(modification))
                return false;
            if (!InReach)
            {
                Reason = "Too far; come within 100 m";
                return false;
            }
            Last = modification;
            var pending = Pending;
            var confirmedRemovals = pendingRemovals;
            Pending = null;
            pendingRemovals = null;
            Reason = null;
            Redisplay();
            if (adding != null)
            {
                Reason = "Car Changer is still putting a change on";
                return false;
            }
            leaving.RemoveWhere(c => c == null);
            if (!CarChangerLink.TryAppliedOn(Car, leaving, out var applied))
            {
                Reason = "Car Changer could not read the car's changes; see the log";
                Done(false);
                return false;
            }
            var matching = applied.Where(a => Equals(a.Config, modification.Config)).Select(a => a.Component).ToList();
            if (matching.Count > 0)
            {
                if (pending == modification)
                {
                    // Already installed (e.g. via another tablet) while awaiting swap confirmation: keep it installed.
                    Done(true);
                    return true;
                }
                leaving.UnionWith(CarChangerLink.Remove(matching));
                Done(true);
                return true;
            }
            if (!CarChangerLink.TryClashes(modification.Config, applied, out var clashes))
            {
                Reason = "Car Changer could not check conflicts; see the log";
                Done(false);
                return false;
            }
            if (clashes.Count == 0)
            {
                var added = CarChangerLink.Add(Car, modification.Config);
                if (!added)
                    Reason = "Car Changer could not add it; see the log";
                Done(added);
                return added;
            }
            var removalSet = clashes.Select(c => c.Component).ToList();
            var sameRemovals = pending == modification
                && confirmedRemovals != null
                && confirmedRemovals.Count == removalSet.Count
                && removalSet.All(confirmedRemovals.Contains);
            if (!sameRemovals)
            {
                // The radio's "Modification is incompatible with others" and "Override?".
                Pending = modification;
                pendingRemovals = removalSet;
                var names = clashes.Select(c => modifications.FirstOrDefault(m => Equals(m.Config, c.Config))?.Name ?? "another change");
                Reason = $"Clashes with {string.Join(", ", names)}; press again to swap";
                Done(false);
                return false;
            }
            // Confirmed: remove the exact validated clashing set, and add the new change next frame (the radio's same-frame caution).
            leaving.UnionWith(CarChangerLink.Remove(removalSet));
            adding = modification;
            Car.StartCoroutine(AddNextFrame(Car, modification));
            Done(true);
            return true;
        }

        private IEnumerator AddNextFrame(TrainCar car, CarChangerLink.Modification modification)
        {
            yield return null;
            if (adding == modification)
                adding = null;
            if (car != null)
                CarChangerLink.Add(car, modification.Config);
            if (this != null)
                Redisplay();
        }

        private void Done(bool ok)
        {
            DV.CommsRadioController.PlayAudioFromRadio(ok ? confirmSound : cancelSound, transform);
            Redisplay();
        }

        protected override void Redisplay()
        {
            shown = default;
            displayValid = false;
            onPollAt = -1f;
            stateVersion++;
        }

        // Car Changer's own words, in the game's language, else English.
        private static string Word(string key, string english)
        {
            var word = LocalizationAPI.L(key);
            return string.IsNullOrWhiteSpace(word) || word.StartsWith("[ MISSING", System.StringComparison.Ordinal) ? english : word;
        }

        protected override void ShowDisplay()
        {
            if (display == null)
                return;
            var key = (Car, Pointed, Last, Pending, adding, stateVersion);
            var now = Time.unscaledTime;
            var pollDue = now >= nextPollAt;
            if (displayValid && key == shownKey && !pollDue)
                return;
            shownKey = key;
            displayValid = true;
            if (pollDue && Car != null)
                onPollAt = -1f;
            nextPollAt = now + PollInterval;
            string content, action;
            if (Car == null)
            {
                content = Word("carchanger/radio_begin", "Point at a car to begin");
                action = ActionWord(false);
            }
            else if (Last == null)
            {
                var on = CachedOnNow();
                content = $"{Car.ID}: {(on != null ? on.Count : 0)} of {modifications.Count} on";
                action = ActionWord(false);
            }
            else
            {
                // One line, as the status line shows: the car and the modification stepped to.
                content = Pending == Last ? $"{Car.ID}: {Word("carchanger/incomp_mod", "Modification is incompatible with others")}" : $"{Car.ID}: {Last.Name}";
                action = Pointed != Car ? ActionWord(false)
                    : Pending == Last ? Word("carchanger/override", "Override?")
                    : IsOn(Last) ? Word("carchanger/disable", "Disable?") : Word("carchanger/enable", "Enable?");
            }
            var next = (content, action);
            if (next == shown)
                return;
            shown = next;
            display.SetDisplay("CHANGE", content, action);
        }
    }
}
