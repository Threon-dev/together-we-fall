using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TogetherWeFall.Combat;
using TogetherWeFall.Equipment;
using TogetherWeFall.Player;
using TogetherWeFall.Skills;

namespace TogetherWeFall.UI
{
    /// <summary>
    /// The always-on readout: two orbs and the skill bar.
    ///
    /// The same bridge the inventory panel is, and it owns exactly as much game
    /// state: none. Every frame it reads the character out of ECS — the two
    /// pools, the sheet behind them, and what is in the socket each key points
    /// at — and draws what it found. It writes nothing back at all, not even a
    /// request, which makes it strictly simpler than the inventory: a build
    /// without it plays identically and the player simply cannot see their own
    /// health.
    ///
    /// What a key casts is asked of GemSockets, which is the same thing
    /// SkillCastSystem asks. That matters more here than anywhere: a bar that
    /// advertises a skill the host will refuse to cast, or a mana price it will
    /// not charge, is worse than a bar with nothing on it — the player would
    /// trust it and build around it.
    ///
    /// Structure is rebuilt only when it changed; the numbers are written every
    /// frame. Those are genuinely different jobs — a cooldown sweeping is not a
    /// reason to throw away and re-create four boxes sixty times a second, and a
    /// gem being pulled out of a socket is.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class PlayerHud : MonoBehaviour
    {
        /// <summary>How many keys the bar shows. Matches SkillLoadoutSystem.</summary>
        private const int BarSlotCount = 4;

        /// <summary>
        /// Diameter of an orb, in logical pixels.
        ///
        /// A constant rather than a share of the screen, unlike the inventory
        /// grid: an orb holds one number, so it needs to be legible rather than
        /// to fit anything. The panel is anchored to the bottom corners, so on a
        /// 32:9 screen the two simply end up very far apart.
        /// </summary>
        private const float OrbSize = 96f;

        private const float SkillBoxSize = 54f;

        [Tooltip("Canvas the HUD is drawn on. Without it the pools and " +
                 "cooldowns still run in the simulation and cannot be seen.")]
        [SerializeField] private Canvas _canvas;

        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private EntityQuery _itemDatabaseQuery;
        private EntityQuery _skillDatabaseQuery;
        private bool _hasWorld;

        private int _playerId;

        private RectTransform _root;
        private Orb _health;
        private Orb _mana;

        private RectTransform _bar;
        private readonly List<SkillBox> _boxes = new List<SkillBox>();

        /// <summary>
        /// What the bar was last built from. When this changes the boxes are
        /// rebuilt; when it does not, only their numbers move.
        /// </summary>
        private int _barSignature = int.MinValue;

        public void Initialize(int playerId)
        {
            _playerId = playerId;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError(
                    $"[{nameof(PlayerHud)}] ECS world is unavailable — the HUD will stay " +
                    "empty.", this);
                return;
            }

            _entityManager = world.EntityManager;

            _characterQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerCharacter>(),
                ComponentType.ReadOnly<PlayerStats>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<Mana>(),
                ComponentType.ReadOnly<SkillSlot>());

            _itemDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ItemDatabase>());

            _skillDatabaseQuery = _entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SkillDatabase>());

            if (_canvas == null)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerHud)}] No Canvas assigned — the HUD will not be " +
                    "drawn. Everything else is unaffected.", this);
            }

            _hasWorld = true;
        }

        /// <summary>
        /// LateUpdate, like the curtain: what is painted is this frame's answer
        /// rather than last frame's. A cooldown one frame stale is invisible; a
        /// mana bar one frame stale is visible exactly when the player is
        /// watching it, which is the frame they pressed the key.
        /// </summary>
        private void LateUpdate()
        {
            if (!_hasWorld || !EnsureLayout())
                return;

            if (!TryGetCharacter(out Entity character))
            {
                _root.gameObject.SetActive(false);
                return;
            }

            _root.gameObject.SetActive(true);

            Health health = _entityManager.GetComponentData<Health>(character);
            Mana mana = _entityManager.GetComponentData<Mana>(character);
            StatBlock stats = _entityManager.GetComponentData<PlayerStats>(character).Final;

            _health.Write(health.Current, health.Max);

            // The ceiling from the sheet rather than from a copy on the mana
            // component, because there is no copy — see the component.
            _mana.Write(mana.Current, stats.Get(StatKind.MaxMana));

            RefreshBar(character);
        }

        // ─────────────────────────────────────────────────────────────────
        // The bar
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds the boxes if what they show changed, then writes the numbers
        /// into them either way.
        ///
        /// The split is the point. A cooldown moves every frame and a gem moves
        /// when somebody drags one, so treating both as "the bar changed" would
        /// rebuild four elements sixty times a second to animate a sweep that a
        /// single style write already does.
        /// </summary>
        private void RefreshBar(Entity character)
        {
            if (_itemDatabaseQuery.IsEmptyIgnoreFilter || _skillDatabaseQuery.IsEmptyIgnoreFilter)
                return;

            ItemDatabase items = _itemDatabaseQuery.GetSingleton<ItemDatabase>();
            SkillDatabase skills = _skillDatabaseQuery.GetSingleton<SkillDatabase>();

            DynamicBuffer<SkillSlot> bar =
                _entityManager.GetBuffer<SkillSlot>(character, isReadOnly: true);
            DynamicBuffer<EquippedItem> worn =
                _entityManager.GetBuffer<EquippedItem>(character, isReadOnly: true);

            int signature = Signature(bar, worn, items, skills);

            if (signature != _barSignature)
            {
                _barSignature = signature;
                Rebuild(bar, worn, items, skills);
            }

            for (int i = 0; i < _boxes.Count && i < bar.Length; i++)
                _boxes[i].WriteCooldown(bar[i]);
        }

        /// <summary>
        /// Everything the boxes are drawn from, in one number.
        ///
        /// The cooldown is deliberately NOT in it — it is the one thing that
        /// changes every frame and the one thing a rebuild is not needed for.
        /// Copied in spirit from the inventory panel's own change check, for the
        /// same reason: comparing a hash is cheaper than diffing four boxes, and
        /// far cheaper than rebuilding them.
        /// </summary>
        private int Signature(
            DynamicBuffer<SkillSlot> bar,
            DynamicBuffer<EquippedItem> worn,
            ItemDatabase items,
            SkillDatabase skills)
        {
            int hash = 17;

            for (int i = 0; i < bar.Length && i < BarSlotCount; i++)
            {
                hash = hash * 31 + bar[i].Gear.Index;
                hash = hash * 31 + bar[i].SocketIndex;

                // The resolved skill, not just the socket: pulling a gem out of
                // a bound hole changes nothing about the binding and everything
                // about what the box should say. Minus one when there is none,
                // which is a value no index takes.
                hash = hash * 31 +
                       (Resolve(bar[i], worn, items, skills, out int skillIndex, out _)
                           ? skillIndex
                           : -1);
            }

            return hash;
        }

        private bool Resolve(
            in SkillSlot slot,
            DynamicBuffer<EquippedItem> worn,
            ItemDatabase items,
            SkillDatabase skills,
            out int skillIndex,
            out int linkGroup)
        {
            skillIndex = -1;
            linkGroup = -1;

            // Worn first, exactly as the cast system checks it: a key pointing
            // at a sword now sitting in the bag casts nothing, so it must read
            // as empty here too.
            if (!GemSockets.IsWorn(worn, slot.Gear))
                return false;

            if (!GemSockets.TryResolveActive(
                    _entityManager, items, skills, slot.Gear, slot.SocketIndex,
                    out skillIndex, out linkGroup))
            {
                return false;
            }

            return true;
        }

        private void Rebuild(
            DynamicBuffer<SkillSlot> bar,
            DynamicBuffer<EquippedItem> worn,
            ItemDatabase items,
            SkillDatabase skills)
        {
            Ugui.Clear(_bar);
            _boxes.Clear();

            for (int i = 0; i < bar.Length && i < BarSlotCount; i++)
            {
                var box = new SkillBox(_bar, i);

                if (Resolve(bar[i], worn, items, skills, out int skillIndex, out int linkGroup))
                {
                    FixedList512Bytes<SkillModifierBlob> supports =
                        GemSockets.GatherSupports(_entityManager, items, bar[i].Gear, linkGroup);

                    // A key bound to a passive: the socket answers its trigger
                    // now, not the button. The binding can only be this old —
                    // one made before the trigger gem arrived — because the
                    // host refuses new ones, but the player still has to see it
                    // rather than discover it by pressing twenty times.
                    //
                    // Asked of the same GemSockets the cast system asks, so the
                    // bar cannot claim a key works when the host has already
                    // decided it does not.
                    bool automatic = GemSockets.IsPassiveActive(
                        _entityManager, items, bar[i].Gear, bar[i].SocketIndex);

                    // Folded rather than read off the asset, because a support
                    // can now change what a press costs. The supports are
                    // already in hand for the trigger check above, so this is
                    // the same fold the host will run — and the bar cannot
                    // advertise a price the cast does not charge.
                    //
                    // Zeroed stats on purpose: nothing on the sheet touches the
                    // cost, and the character's own numbers are not this box's
                    // business.
                    box.Fill(
                        skills.NameOf(skillIndex).ToString(),
                        skills.Resolve(
                            skillIndex, StatBlock.Zero(), supports,
                            CastConditions.Unknown(default)).ManaCost,
                        automatic);
                }

                _boxes.Add(box);
            }
        }

        private bool TryGetCharacter(out Entity character)
        {
            character = Entity.Null;

            if (_characterQuery.IsEmptyIgnoreFilter)
                return false;

            using NativeArray<Entity> entities =
                _characterQuery.ToEntityArray(Allocator.Temp);
            using NativeArray<PlayerCharacter> characters =
                _characterQuery.ToComponentDataArray<PlayerCharacter>(Allocator.Temp);

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i].PlayerId != _playerId)
                    continue;

                character = entities[i];
                return true;
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // Layout
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the chrome on first use.
        ///
        /// Lazily rather than in Initialize, for the reason the curtain gives:
        /// the bootstrap that calls Initialize deliberately runs before every
        /// other component, and building on first paint depends on nothing.
        ///
        /// The three pieces are anchored to the strip's corners rather than laid
        /// out by a group. UI Toolkit said "a row, spaced apart, bottom
        /// aligned"; uGUI has no space-between, and with exactly three children
        /// that never change the anchors say the same thing in the same number
        /// of lines — left orb, bar in the middle, right orb — without a
        /// layout pass. The bar is the one piece whose width depends on its
        /// contents, and that is the one place a layout group earns itself.
        ///
        /// The HUD is a readout, not a surface. Every click on it belongs to the
        /// world underneath, which is why its canvas carries no raycaster at
        /// all — see the scene builder. The one click that would not, casting, is
        /// read from the input system rather than from an element anyway.
        /// </summary>
        private bool EnsureLayout()
        {
            if (_root != null)
                return true;

            if (_canvas == null)
                return false;

            _root = Ugui.Node(_canvas.transform, "Hud");
            Ugui.Place(_root, left: 18f, right: 18f, bottom: 12f, height: OrbSize);

            _health = new Orb(
                _root, "Health",
                new Color(0.68f, 0.16f, 0.18f), new Color(0.30f, 0.08f, 0.09f));
            Ugui.Place(_health.Root, left: 0f, bottom: 0f, width: OrbSize, height: OrbSize);

            _mana = new Orb(
                _root, "Mana",
                new Color(0.22f, 0.40f, 0.78f), new Color(0.09f, 0.15f, 0.32f));
            Ugui.Place(_mana.Root, right: 0f, bottom: 0f, width: OrbSize, height: OrbSize);

            _bar = Ugui.Node(_root, "SkillBar");
            Ugui.Place(_bar, bottom: 8f, height: SkillBoxSize);

            // The boxes keep their own size — the group only spaces them and
            // measures the result, which the fitter then makes the bar's width.
            // Eight of spacing is the four-a-side margin each box used to carry.
            var row = _bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.LowerCenter;
            row.spacing = 8f;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var fitter = _bar.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            return true;
        }

        private void OnDestroy()
        {
            // Netcode disposes its worlds before the scene is torn down when play mode ends.
            if (!_hasWorld || World.DefaultGameObjectInjectionWorld is { IsCreated: true } == false)
                return;

            _characterQuery.Dispose();
            _itemDatabaseQuery.Dispose();
            _skillDatabaseQuery.Dispose();
        }

        // ─────────────────────────────────────────────────────────────────
        // Pieces
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// One pool: a round well that fills from the bottom, with the numbers
        /// written across it.
        ///
        /// The fill is a child whose HEIGHT is the fraction, anchored to the
        /// bottom — a liquid level rather than a bar on its side, which is what
        /// makes it read as an orb at a glance even though it is three
        /// rectangles with a large border radius.
        ///
        /// The number is on it rather than beside it because this is the one
        /// place in the game the player looks under pressure, and a glance that
        /// has to travel between a shape and a label is a glance that missed.
        /// </summary>
        private sealed class Orb
        {
            public readonly RectTransform Root;

            private readonly Image _fill;
            private readonly TextMeshProUGUI _label;

            private int _lastCurrent = int.MinValue;
            private int _lastMax = int.MinValue;

            public Orb(Transform parent, string name, Color fill, Color empty)
            {
                const float Radius = OrbSize * 0.5f;

                Root = Ugui.Box(parent, name, empty, Radius).rectTransform;

                // A filled image rather than a child whose height is the
                // fraction, which is what UI Toolkit needed. uGUI fills a
                // sprite from an edge natively, so the liquid level is one
                // number on one component — and it is the circle itself being
                // cut, so the surface stays flat and the bottom stays round
                // however low it gets. That took a separately-rounded child
                // before.
                _fill = Ugui.Box(Root, "Fill", fill, Radius);
                _fill.type = Image.Type.Filled;
                _fill.fillMethod = Image.FillMethod.Vertical;
                _fill.fillOrigin = (int)Image.OriginVertical.Bottom;
                _fill.fillAmount = 0f;

                _label = Ugui.Text(Root, "Label", 14f, Color.white, bold: true);

                // Last, so the ring draws over the fill instead of under it.
                Ugui.Border(Root, new Color(0.08f, 0.09f, 0.11f), 2f, Radius);
            }

            public void Write(float current, float max)
            {
                float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
                _fill.fillAmount = fraction;

                // Rounded before comparing, so the label is rewritten when the
                // number it shows changes rather than when the float behind it
                // does. Mana regenerates continuously; without this the text is
                // rebuilt every frame to say the same thing.
                int shownCurrent = Mathf.CeilToInt(current);
                int shownMax = Mathf.RoundToInt(max);

                if (shownCurrent == _lastCurrent && shownMax == _lastMax)
                    return;

                _lastCurrent = shownCurrent;
                _lastMax = shownMax;
                _label.text = $"{shownCurrent} / {shownMax}";
            }
        }

        /// <summary>
        /// One key on the bar: what it is bound to, what it costs and how long
        /// until it answers again.
        ///
        /// The cooldown is a shade that drains downward over the box rather than
        /// a ring or a number alone. It is readable without being read — the
        /// player sees how much dark is left out of the corner of their eye —
        /// and the seconds are written on it for when they do look.
        /// </summary>
        private sealed class SkillBox
        {
            public readonly RectTransform Root;

            private readonly Image _border;
            private readonly Image _shade;
            private readonly TextMeshProUGUI _timer;

            private readonly TextMeshProUGUI _icon;
            private readonly TextMeshProUGUI _cost;
            private readonly TextMeshProUGUI _charges;

            private float _lastCooldown = -1f;
            private int _lastCharges = -1;

            public SkillBox(Transform parent, int slot)
            {
                Root = Ugui.Box(
                    parent, $"Slot{slot}",
                    new Color(0.10f, 0.11f, 0.14f, 0.92f), radius: 6f).rectTransform;

                Ugui.Place(Root, width: SkillBoxSize, height: SkillBoxSize);

                // The placeholder icon: the skill's initials, large and centred.
                // A real icon is a sprite per gem, which is art rather than
                // pipeline — and a box that says "FB" tells the player which of
                // two fire skills this is, which is the whole job an icon has.
                _icon = Ugui.Text(
                    Root, "Icon", 18f, new Color(0.38f, 0.41f, 0.48f), bold: true);
                Ugui.Place(_icon.rectTransform, left: 0f, right: 0f, top: 14f, height: 24f);

                TextMeshProUGUI key = Ugui.Text(
                    Root, "Key", 9f, new Color(0.72f, 0.76f, 0.84f),
                    TextAlignmentOptions.TopLeft, bold: true);
                key.text = PlayerInputReader.CastSlotName(slot);
                Ugui.Place(key.rectTransform, left: 4f, top: 2f, width: 20f, height: 12f);

                _cost = Ugui.Text(
                    Root, "Cost", 10f, new Color(0.44f, 0.62f, 0.94f),
                    TextAlignmentOptions.BottomRight);
                Ugui.Place(_cost.rectTransform, right: 4f, bottom: 2f, width: 30f, height: 14f);

                // Over everything else in the box, because it covers them —
                // which in uGUI means later in the child order rather than
                // anything said about depth.
                //
                // Square, unlike the box under it: a filled image maps its whole
                // sprite onto the rect, so a rounded one would have its corners
                // scaled up with it and read as a much larger radius than the
                // box's own. What it pokes into is three pixels of near-black
                // over near-black.
                _shade = Ugui.Box(Root, "Shade", new Color(0f, 0f, 0f, 0.62f));
                _shade.type = Image.Type.Filled;
                _shade.fillMethod = Image.FillMethod.Vertical;
                _shade.fillOrigin = (int)Image.OriginVertical.Top;
                _shade.gameObject.SetActive(false);

                _timer = Ugui.Text(Root, "Timer", 13f, Color.white, bold: true);
                Ugui.Place(_timer.rectTransform, left: 0f, right: 0f, bottom: 12f, height: 18f);
                _timer.gameObject.SetActive(false);

                // Presses in hand, for a key that stores more than one. Over the
                // shade, because it is the number that says the shade is not the
                // whole story.
                _charges = Ugui.Text(
                    Root, "Charges", 10f, new Color(0.95f, 0.85f, 0.55f),
                    TextAlignmentOptions.TopRight, bold: true);
                Ugui.Place(_charges.rectTransform, right: 4f, top: 2f, width: 20f, height: 12f);

                _border = Ugui.Border(Root, new Color(0.24f, 0.26f, 0.32f), 1f, radius: 6f);
            }

            /// <summary>What this key is bound to right now. Empty leaves it dim.</summary>
            public void Fill(string skillName, float manaCost, bool automatic)
            {
                _icon.text = Initials(skillName);
                _icon.color = automatic
                    ? new Color(0.85f, 0.66f, 0.38f)
                    : new Color(0.80f, 0.84f, 0.90f);

                _border.color = automatic
                    ? new Color(0.85f, 0.66f, 0.38f)
                    : new Color(0.45f, 0.62f, 0.85f);

                // A trigger gem in the group means the key is not the thing that
                // fires this, so there is no press to price. Showing a cost the
                // player cannot choose to pay would be worse than showing none.
                _cost.text = automatic
                    ? "auto"
                    : manaCost > 0f ? Mathf.RoundToInt(manaCost).ToString() : string.Empty;

                _cost.color = automatic
                    ? new Color(0.85f, 0.66f, 0.38f)
                    : new Color(0.44f, 0.62f, 0.94f);
            }

            public void WriteCooldown(in SkillSlot slot)
            {
                int charges = Mathf.Max(1, slot.Charges);
                int ready = Mathf.Max(0, charges - slot.ChargesSpent);

                int chargeKey = charges * 1000 + ready;
                if (chargeKey != _lastCharges)
                {
                    _lastCharges = chargeKey;
                    _charges.text = charges > 1 ? ready.ToString() : string.Empty;
                }

                // Shaded only when nothing is left to press. A charge still in
                // hand is a key that works, whatever the timer on the next says.
                float remaining = ready > 0 ? 0f : slot.CooldownRemaining;


                // Compared against the last value so a bar sitting idle — which
                // is most of the time, for most keys — writes no styles at all.
                if (Mathf.Approximately(remaining, _lastCooldown))
                    return;

                _lastCooldown = remaining;

                bool active = remaining > 0.01f;
                _shade.gameObject.SetActive(active);
                _timer.gameObject.SetActive(active);

                if (!active)
                    return;

                // The shade's share of the box is the time left, but against
                // WHAT? The slot does not carry the cooldown it started from, and
                // adding that would be a second number to keep in step with the
                // first. So the sweep is against two seconds of travel and
                // clamps — a long cooldown simply starts full and a short one
                // drains fast, and the seconds written on it are exact either
                // way.
                _shade.fillAmount = Mathf.Clamp01(remaining / 2f);

                _timer.text = remaining >= 1f
                    ? remaining.ToString("0.0")
                    : remaining.ToString(".0");
            }

            /// <summary>
            /// Up to two letters, taken from the start of each word.
            ///
            /// "Frost Lance" is FL and "Spark" is SP, which is enough to tell
            /// four boxes apart at a glance — and that, rather than decoration,
            /// is what the real icons will be replacing.
            /// </summary>
            private static string Initials(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;

                string[] words = name.Split(' ');

                if (words.Length >= 2 && words[1].Length > 0)
                    return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";

                return name.Length >= 2
                    ? name.Substring(0, 2).ToUpperInvariant()
                    : name.ToUpperInvariant();
            }
        }
    }
}
