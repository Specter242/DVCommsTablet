using DV;
using DV.Utils;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// A tablet mode that works car first, as Skin Manager's switcher does:
    /// aim at a car and use the tablet to choose it; its app then lists what
    /// can be done to it. Used at the chosen car again, the mode applies the
    /// last choice; at another car that fits, chooses that one; at nothing,
    /// lets go. The chosen car's box is amber, a pointed one's the stock green.
    /// Paint (<see cref="TabletPaintMode"/>) and Rearrange
    /// (<see cref="TabletRearrangeMode"/>) are built on it.
    /// </summary>
    internal abstract class TabletCarMode : MonoBehaviour, ICommsRadioMode
    {
        /// <summary>The radio's reach: a car is chosen and worked within it.</summary>
        internal const float SignalRange = 100f;
        private static readonly Vector3 HighlightExtension = new Vector3(0.25f, 0.8f, 0f);
        private static readonly Color Chosen = new Color(1f, 0.62f, 0.1f, 1f);

        // From the stock Paint mode, set when the tablet is built (TabletItem).
        public Transform signalOrigin;
        public CommsRadioDisplay display;
        public Material validMaterial;
        public AudioClip hoverOverCar, confirmSound, cancelSound;
        public GameObject trainHighlighter;

        private MeshRenderer highlighterRender;
        private Material selectedMaterial;
        private LayerMask trainCarMask;
        private TrainCar pointed, highlighted;

        /// <summary>The chosen car; null until one is chosen.</summary>
        internal TrainCar Car { get; private set; }

        /// <summary>The car aimed at, if it fits.</summary>
        protected TrainCar Pointed => pointed;

        /// <summary>Raised when the chosen car changes, so the app can redraw.</summary>
        internal event System.Action CarChanged;

        public ButtonBehaviourType ButtonBehaviour => Car != null ? ButtonBehaviourType.Override : ButtonBehaviourType.Regular;

        /// <summary>Whether <paramref name="car"/> can be chosen; others are not pointed at.</summary>
        protected virtual bool Fits(TrainCar car) => true;

        /// <summary>The chosen car changed to <see cref="Car"/>: fill its lists.</summary>
        protected abstract void OnChosen();

        /// <summary>The car was let go: empty its lists.</summary>
        protected abstract void OnReleased();

        /// <summary>Used at the chosen car again: apply the last choice, if any.</summary>
        protected abstract void ApplyLast();

        /// <summary>The scroll wheel: steps the last choice through the chosen car's list without applying it.</summary>
        protected abstract bool Step(int by);

        /// <summary>Shows the mode's state on the radio display the tablet mirrors.</summary>
        protected abstract void ShowDisplay();

        public abstract Color GetLaserBeamColor();

        protected virtual void Awake()
        {
            if (signalOrigin == null) signalOrigin = transform;
            trainCarMask = LayerMask.GetMask("Train_Big_Collider");
            if (trainHighlighter != null)
            {
                highlighterRender = trainHighlighter.GetComponentInChildren<MeshRenderer>(true);
                trainHighlighter.SetActive(false);
                trainHighlighter.transform.SetParent(null);
            }
            // Deleted or returned to the pool: let the car go before the highlighter goes with it.
            SingletonBehaviour<CarSpawner>.Instance.CarAboutToBeDeleted += CarDeleted;
            if (validMaterial != null)
            {
                // The chosen car's box in amber. The highlight shader ("Teleport Marker")
                // multiplies its tint by a green texture, so the copy gets a colorless one.
                selectedMaterial = new Material(validMaterial) { name = "CommsTablet_CarChosen" };
                Tint(selectedMaterial, "_TintColor", Chosen);
                Tint(selectedMaterial, "_FadeColor", Chosen * 0.6f);
                if (selectedMaterial.HasProperty("_MainTex") && Colorless(selectedMaterial.GetTexture("_MainTex")) is Texture2D plain)
                    selectedMaterial.SetTexture("_MainTex", plain);
            }
        }

        private static void Tint(Material material, string property, Color color)
        {
            if (material.HasProperty(property))
                material.SetColor(property, new Color(color.r, color.g, color.b, material.GetColor(property).a));
        }

        private static Texture2D colorless;
        private static Texture colorlessSource;

        /// <summary>A gray copy of <paramref name="source"/> (each pixel its brightest channel, alpha kept), read back through the GPU since the game's textures are not readable.</summary>
        private static Texture2D Colorless(Texture source)
        {
            if (source == null)
                return null;
            if (colorless != null && colorlessSource == source)
                return colorless;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                    { name = "CommsTablet_CarChosenMask", wrapMode = source.wrapMode, filterMode = source.filterMode, hideFlags = HideFlags.HideAndDontSave };
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
                var pixels = copy.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var v = System.Math.Max(p.r, System.Math.Max(p.g, p.b));
                    pixels[i] = new Color32(v, v, v, p.a);
                }
                copy.SetPixels32(pixels);
                copy.Apply(false, true);
                if (colorless != null) Destroy(colorless);
                colorless = copy;
                colorlessSource = source;
                return copy;
            }
            catch (System.Exception e)
            {
                Main.Mod?.Logger.Warning("The chosen car's highlight stays the stock color: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        protected virtual void OnDestroy()
        {
            if (UnloadWatcher.isUnloading)
                return;
            SingletonBehaviour<CarSpawner>.Instance.CarAboutToBeDeleted -= CarDeleted;
            Release(false);
            if (trainHighlighter != null) Destroy(trainHighlighter);
            if (selectedMaterial != null) Destroy(selectedMaterial);
        }

        public void Enable() { }

        public void Disable() => Release(false);

        public void OverrideSignalOrigin(Transform origin) => signalOrigin = origin;

        public void SetStartingDisplay()
        {
            Redisplay();
            ShowDisplay();
        }

        /// <summary>Forgets what the display last showed, so the next <see cref="ShowDisplay"/> writes it.</summary>
        protected abstract void Redisplay();

        private void CarDeleted(TrainCar car)
        {
            if (car == null)
                return;
            if (car == highlighted)
                Highlight(null, null);
            if (car == pointed)
                pointed = null;
            if (car == Car)
                Release(true);
        }

        public void OnUpdate()
        {
            if (Car == null ? !ReferenceEquals(Car, null) : !Car.gameObject.activeInHierarchy)
                Release(true);   // the chosen car went without word: destroyed, or pooled
            TrainCar car = null;
            if (signalOrigin != null && Physics.Raycast(signalOrigin.position, signalOrigin.forward, out var hit, SignalRange, trainCarMask))
                car = TrainCar.Resolve(hit.transform.root);
            if (car != null && !Fits(car))
                car = null;
            if (car != pointed)
            {
                pointed = car;
                if (car != null && Car == null)
                    CommsRadioController.PlayAudioFromRadio(hoverOverCar, transform);
            }
            Highlight(Car != null ? Car : pointed, Car != null ? selectedMaterial : validMaterial);
            ShowDisplay();
        }

        public void OnUse()
        {
            if (pointed == null)
            {
                if (Car != null)
                    CommsRadioController.PlayAudioFromRadio(cancelSound, transform);
                Release(true);
                return;
            }
            if (pointed != Car)
            {
                Choose(pointed);
                CommsRadioController.PlayAudioFromRadio(confirmSound, transform);
                return;
            }
            ApplyLast();
        }

        public bool ButtonACustomAction() => Car != null && Step(-1);

        public bool ButtonBCustomAction() => Car != null && Step(1);

        /// <summary>Chooses <paramref name="car"/>, its lists filled for the app.</summary>
        internal void Choose(TrainCar car)
        {
            if (car == null || !Fits(car))
                return;
            Car = car;
            OnChosen();
            Redisplay();
            CarChanged?.Invoke();
        }

        private void Release(bool announce)
        {
            var had = Car != null || !ReferenceEquals(Car, null);
            Car = null;
            pointed = null;
            OnReleased();
            Highlight(null, null);
            Redisplay();
            if (display != null) ShowDisplay();
            if (had && announce) CarChanged?.Invoke();
        }

        /// <summary>
        /// Whether the chosen car is within the radio's 100 m signal range: a
        /// screen press acts without aiming, and a Multiplayer host undoes a
        /// client's change from much farther.
        /// </summary>
        internal bool InReach => Car != null && signalOrigin != null
            && (Car.transform.position - signalOrigin.position).sqrMagnitude <= SignalRange * SignalRange;

        /// <summary>The action word for the display: select a pointed car, cancel at nothing, confirm at the chosen one.</summary>
        protected string ActionWord(bool canApply) =>
            Car == null ? (pointed != null ? CommsRadioLocalization.SELECT : "")
            : pointed == null ? CommsRadioLocalization.CANCEL
            : pointed != Car ? CommsRadioLocalization.SELECT
            : canApply ? CommsRadioLocalization.CONFIRM : "";

        private void Highlight(TrainCar car, Material material)
        {
            if (trainHighlighter == null)
                return;
            if (car == null)
            {
                if (highlighted == null && !trainHighlighter.activeSelf)
                    return;
                highlighted = null;
                trainHighlighter.SetActive(false);
                trainHighlighter.transform.SetParent(null);
                return;
            }
            if (car == highlighted && trainHighlighter.transform.parent == car.transform
                && (highlighterRender == null || material == null || highlighterRender.sharedMaterial == material))
                return;
            highlighted = car;
            if (highlighterRender != null && material != null) highlighterRender.sharedMaterial = material;
            var t = trainHighlighter.transform;
            t.SetParent(null);
            t.localScale = car.Bounds.size + HighlightExtension;
            var position = car.transform.position + car.transform.up * (t.localScale.y / 2f) + car.transform.forward * car.Bounds.center.z;
            t.SetPositionAndRotation(position, car.transform.rotation);
            trainHighlighter.SetActive(true);
            t.SetParent(car.transform, true);
        }
    }
}
