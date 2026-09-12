using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
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

        [Tooltip("Panel the HUD is drawn into. Without it the pools and " +
                 "cooldowns still run in the simulation and cannot be seen.")]
        [SerializeField] private UIDocument _document;

        private EntityManager _entityManager;
        private EntityQuery _characterQuery;
        private EntityQuery _itemDatabaseQuery;
        private EntityQuery _skillDatabaseQuery;
        private bool _hasWorld;

        private int _playerId;

        private VisualElement _root;
        private Orb _health;
        private Orb _mana;

        private VisualElement _bar;
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

            if (_document == null)
            {
                Debug.LogWarning(
                    $"[{nameof(PlayerHud)}] No UIDocument assigned — the HUD will not be " +
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
                _root.style.display = DisplayStyle.None;
                return;
            }

            _root.style.display = DisplayStyle.Flex;

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
                _boxes[i].WriteCooldown(bar[i].CooldownRemaining);
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
            _bar.Clear();
            _boxes.Clear();

            for (int i = 0; i < bar.Length && i < BarSlotCount; i++)
            {
                var box = new SkillBox(i);

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

                _bar.Add(box.Root);
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
        /// a UIDocument fills its root in OnEnable, and the bootstrap that calls
        /// Initialize deliberately runs before every other component.
        /// </summary>
        private bool EnsureLayout()
        {
            if (_root != null)
                return true;

            if (_document == null)
                return false;

            VisualElement root = _document.rootVisualElement;
            if (root == null)
                return false;

            _root = new VisualElement();
            _root.style.position = Position.Absolute;
            _root.style.left = 0f;
            _root.style.right = 0f;
            _root.style.bottom = 12f;
            _root.style.flexDirection = FlexDirection.Row;
            _root.style.alignItems = Align.FlexEnd;
            _root.style.justifyContent = Justify.SpaceBetween;
            _root.style.paddingLeft = 18f;
            _root.style.paddingRight = 18f;

            // The HUD is a readout, not a surface. Every click on it belongs to
            // the world underneath — and the one click that would not, casting,
            // is read from the input system rather than from an element anyway.
            _root.pickingMode = PickingMode.Ignore;

            _health = new Orb(new Color(0.68f, 0.16f, 0.18f), new Color(0.30f, 0.08f, 0.09f));
            _mana = new Orb(new Color(0.22f, 0.40f, 0.78f), new Color(0.09f, 0.15f, 0.32f));

            _bar = new VisualElement();
            _bar.style.flexDirection = FlexDirection.Row;
            _bar.style.alignItems = Align.FlexEnd;
            _bar.style.marginBottom = 8f;
            _bar.pickingMode = PickingMode.Ignore;

            _root.Add(_health.Root);
            _root.Add(_bar);
            _root.Add(_mana.Root);

            root.Add(_root);
            return true;
        }

        private void OnDestroy()
        {
            if (!_hasWorld)
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
            public readonly VisualElement Root;

            private readonly VisualElement _fill;
            private readonly Label _label;

            private int _lastCurrent = int.MinValue;
            private int _lastMax = int.MinValue;

            public Orb(Color fill, Color empty)
            {
                Root = new VisualElement();
                Root.style.width = OrbSize;
                Root.style.height = OrbSize;
                Root.style.backgroundColor = empty;
                Root.style.justifyContent = Justify.Center;
                Root.style.alignItems = Align.Center;
                Root.pickingMode = PickingMode.Ignore;

                SetRadius(Root, OrbSize * 0.5f);
                SetBorder(Root, new Color(0.08f, 0.09f, 0.11f), 2f);

                _fill = new VisualElement();
                _fill.style.position = Position.Absolute;
                _fill.style.left = 0f;
                _fill.style.right = 0f;
                _fill.style.bottom = 0f;
                _fill.style.backgroundColor = fill;
                _fill.pickingMode = PickingMode.Ignore;

                // Rounded at the bottom only. A fill rounded at the top would
                // pull away from the orb's edge as it drains and stop reading as
                // liquid; square at the top is what a surface looks like.
                _fill.style.borderBottomLeftRadius = OrbSize * 0.5f;
                _fill.style.borderBottomRightRadius = OrbSize * 0.5f;

                _label = new Label();
                _label.style.fontSize = 14;
                _label.style.color = Color.white;
                _label.style.unityFontStyleAndWeight = FontStyle.Bold;
                _label.style.unityTextAlign = TextAnchor.MiddleCenter;
                _label.pickingMode = PickingMode.Ignore;

                Root.Add(_fill);
                Root.Add(_label);
            }

            public void Write(float current, float max)
            {
                float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
                _fill.style.height = Length.Percent(fraction * 100f);

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
            public readonly VisualElement Root;

            private readonly VisualElement _shade;
            private readonly Label _timer;

            private readonly Label _icon;
            private readonly Label _cost;

            private float _lastCooldown = -1f;

            public SkillBox(int slot)
            {
                Root = new VisualElement();
                Root.style.width = SkillBoxSize;
                Root.style.height = SkillBoxSize;
                Root.style.marginLeft = 4f;
                Root.style.marginRight = 4f;
                Root.style.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 0.92f);
                Root.pickingMode = PickingMode.Ignore;

                SetRadius(Root, 6f);
                SetBorder(Root, new Color(0.24f, 0.26f, 0.32f), 1f);

                // The placeholder icon: the skill's initials, large and centred.
                // A real icon is a sprite per gem, which is art rather than
                // pipeline — and a box that says "FB" tells the player which of
                // two fire skills this is, which is the whole job an icon has.
                _icon = new Label();
                _icon.style.position = Position.Absolute;
                _icon.style.left = 0f;
                _icon.style.right = 0f;
                _icon.style.top = 14f;
                _icon.style.fontSize = 18;
                _icon.style.unityFontStyleAndWeight = FontStyle.Bold;
                _icon.style.unityTextAlign = TextAnchor.MiddleCenter;
                _icon.style.color = new Color(0.38f, 0.41f, 0.48f);
                _icon.pickingMode = PickingMode.Ignore;

                var key = new Label(PlayerInputReader.CastSlotName(slot));
                key.style.position = Position.Absolute;
                key.style.left = 4f;
                key.style.top = 2f;
                key.style.fontSize = 9;
                key.style.color = new Color(0.72f, 0.76f, 0.84f);
                key.style.unityFontStyleAndWeight = FontStyle.Bold;
                key.pickingMode = PickingMode.Ignore;

                _cost = new Label();
                _cost.style.position = Position.Absolute;
                _cost.style.right = 4f;
                _cost.style.bottom = 2f;
                _cost.style.fontSize = 10;
                _cost.style.color = new Color(0.44f, 0.62f, 0.94f);
                _cost.pickingMode = PickingMode.Ignore;

                // Above everything else in the box, because it covers them.
                _shade = new VisualElement();
                _shade.style.position = Position.Absolute;
                _shade.style.left = 0f;
                _shade.style.right = 0f;
                _shade.style.top = 0f;
                _shade.style.backgroundColor = new Color(0f, 0f, 0f, 0.62f);
                _shade.style.display = DisplayStyle.None;
                _shade.pickingMode = PickingMode.Ignore;

                _timer = new Label();
                _timer.style.position = Position.Absolute;
                _timer.style.left = 0f;
                _timer.style.right = 0f;
                _timer.style.bottom = 12f;
                _timer.style.fontSize = 13;
                _timer.style.unityFontStyleAndWeight = FontStyle.Bold;
                _timer.style.unityTextAlign = TextAnchor.MiddleCenter;
                _timer.style.color = Color.white;
                _timer.style.display = DisplayStyle.None;
                _timer.pickingMode = PickingMode.Ignore;

                Root.Add(_icon);
                Root.Add(key);
                Root.Add(_cost);
                Root.Add(_shade);
                Root.Add(_timer);
            }

            /// <summary>What this key is bound to right now. Empty leaves it dim.</summary>
            public void Fill(string skillName, float manaCost, bool automatic)
            {
                _icon.text = Initials(skillName);
                _icon.style.color = automatic
                    ? new Color(0.85f, 0.66f, 0.38f)
                    : new Color(0.80f, 0.84f, 0.90f);

                SetBorder(
                    Root,
                    automatic
                        ? new Color(0.85f, 0.66f, 0.38f)
                        : new Color(0.45f, 0.62f, 0.85f),
                    1f);

                // A trigger gem in the group means the key is not the thing that
                // fires this, so there is no press to price. Showing a cost the
                // player cannot choose to pay would be worse than showing none.
                _cost.text = automatic
                    ? "auto"
                    : manaCost > 0f ? Mathf.RoundToInt(manaCost).ToString() : string.Empty;

                _cost.style.color = automatic
                    ? new Color(0.85f, 0.66f, 0.38f)
                    : new Color(0.44f, 0.62f, 0.94f);
            }

            public void WriteCooldown(float remaining)
            {
                // Compared against the last value so a bar sitting idle — which
                // is most of the time, for most keys — writes no styles at all.
                if (Mathf.Approximately(remaining, _lastCooldown))
                    return;

                _lastCooldown = remaining;

                bool active = remaining > 0.01f;
                _shade.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                _timer.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;

                if (!active)
                    return;

                // The shade's height is the time left, but against WHAT? The
                // slot does not carry the cooldown it started from, and adding
                // that would be a second number to keep in step with the first.
                // So the sweep is against two seconds of travel and clamps —
                // a long cooldown simply starts full and a short one drains fast,
                // and the seconds written on it are exact either way.
                _shade.style.height =
                    Length.Percent(Mathf.Clamp01(remaining / 2f) * 100f);

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

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        private static void SetBorder(VisualElement element, Color colour, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;

            element.style.borderTopColor = colour;
            element.style.borderBottomColor = colour;
            element.style.borderLeftColor = colour;
            element.style.borderRightColor = colour;
        }
    }
}
