using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TogetherWeFall.Skills;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// The player's body: the Fina model from its store pack, and an animator
    /// controller built from the RPG Character pack's clips.
    ///
    /// Both packs are humanoid, which is the whole reason the clips fit a model
    /// they were not made for — the retargeting happens in the avatars.
    ///
    /// Materials follow the other factories: one that exists is returned, not
    /// rebuilt. The controller does not — it is rebuilt in place on every scene
    /// build, keeping its GUID, so a change here reaches every scene that
    /// already points at it.
    /// </summary>
    public static class CharacterContentFactory
    {
        public const string ModelPrefabPath = "Assets/smoky_fox/FinaAnimeGirl/Prefabs/Fina.prefab";

        private const string Pack = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack";
        private const string UpperBodyMaskPath = Pack + "/Avatars/RPG-Character-Upperbody-AvatarMask.mask";

        private const string CharacterFolder = SceneBuildUtility.ArtFolder + "/Characters";
        private const string ControllerPath = CharacterFolder + "/PlayerLocomotion.controller";

        // What is in the hand. The same numbers PlayerAnimationPresenter writes
        // into Weapon, WeaponBlend and Grip.
        private const int Unarmed = 0;
        private const int OneHanded = 1;
        private const int TwoHanded = 2;
        private const int Bow = 3;

        /// <summary>
        /// Standing easy, out of a fight. Only ever a locomotion blend: Weapon
        /// stays Unarmed underneath it, so a flinch or a cast from a stroll plays
        /// the bare-handed one.
        /// </summary>
        private const int Relaxed = -1;

        /// <summary>
        /// The pack's draw and sheath take a full second, which is a second of
        /// standing in a crowd with nothing in the hand. PlayerAnimationPresenter's
        /// swap delays are timed against this speed.
        /// </summary>
        private const float DrawSpeed = 2.5f;

        /// <summary>
        /// How far into a swing it starts. The pack's swings open on a slow
        /// wind-up, and the blow has already landed by the time the key is
        /// read — skipping into the swing puts the blade where the hit is.
        /// </summary>
        private const float SwingOffset = 0.15f;

        /// <summary>A stance: which folder of the pack, and what its files start with.</summary>
        private static readonly (int held, string folder, string prefix)[] Stances =
        {
            (Unarmed, "Unarmed", "Unarmed-"),
            (OneHanded, "Armed", "Armed-"),
            (TwoHanded, "2Hand-Sword", "2Hand-Sword-"),
            (Bow, "2Hand-Bow", "2Hand-Bow-")
        };

        /// <summary>
        /// Two layers.
        ///
        /// Base: locomotion, and death over it. One blend per stance — idle in
        /// the middle, a ring of strafing walks at half a stick, a ring of runs
        /// at the edge — and the three blended by WeaponBlend, so drawing a sword
        /// eases the arms into holding it rather than snapping.
        ///
        /// Upper body, masked to the waist up so the legs keep running: casts
        /// and swings picked by what is held and what was cast, flinches, and
        /// the draw and sheath.
        /// </summary>
        public static RuntimeAnimatorController CreateLocomotionController()
        {
            SceneBuildUtility.EnsureAssetFolder(CharacterFolder);

            AnimatorController controller = LoadEmptiedOrCreate();

            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("WeaponBlend", AnimatorControllerParameterType.Float);
            controller.AddParameter("Weapon", AnimatorControllerParameterType.Int);
            controller.AddParameter("Grip", AnimatorControllerParameterType.Int);
            controller.AddParameter("Draw", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Sheath", AnimatorControllerParameterType.Trigger);

            // A bow held drawn between shots, for as long as the presenter says
            // the fight is still going.
            controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);

            // SkillEffectKind as a number: which kind of skill decides the pose.
            controller.AddParameter("CastKind", AnimatorControllerParameterType.Int);
            controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);

            // Swings per second of clip. One unless told: a zero would freeze
            // every swing on its first frame.
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "SwingRate",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f
            });

            AddBaseLayer(controller);
            AddUpperBodyLayer(controller);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static AnimatorController LoadEmptiedOrCreate()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                controller.RemoveLayer(0);
                return controller;
            }

            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];

            // State machines, states, transitions and blend trees are all
            // sub-assets. Left behind, every rebuild would add another copy.
            foreach (Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(ControllerPath))
                Object.DestroyImmediate(sub, allowDestroyingAssets: true);

            return controller;
        }

        private static void AddBaseLayer(AnimatorController controller)
        {
            controller.AddLayer("Base Layer");
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree stances, 0);
            stances.blendType = BlendTreeType.Simple1D;
            stances.blendParameter = "WeaponBlend";
            stances.useAutomaticThresholds = false;

            // Out of a fight: the pack's Relax set, just below bare-handed guard
            // on the line so the two ease into each other. Walks rather than
            // strafes at half a stick — nobody strafes on a stroll.
            AddStanceTree(stances, Relaxed, "Relax", "Relax-Idle", "Relax-Walk", "Relax-Run", "Forward");

            foreach ((int held, string folder, string prefix) in Stances)
            {
                // Spelled as the pack spells it: the bow's forward runs are
                // "Foward", and a clip that fails to load is a hole in the ring.
                AddStanceTree(stances, held, folder, prefix + "Idle", prefix + "Strafe", prefix + "Run",
                    held == Bow ? "Foward" : "Forward");
            }

            AnimatorState death = machine.AddState("Death");
            death.motion = LoadClip("Unarmed", "Unarmed-Death1");

            AnimatorStateTransition die = machine.AddAnyStateTransition(death);
            die.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            die.canTransitionToSelf = false;
            die.duration = 0.1f;

            // Revive, for the day there is one: nothing lowers Dead yet.
            AnimatorStateTransition rise = death.AddTransition(locomotion);
            rise.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            rise.duration = 0.3f;
        }

        /// <summary>
        /// One stance's locomotion: idle in the middle, a ring at half a stick,
        /// a ring of runs at the edge — the three blended by WeaponBlend at
        /// this threshold.
        /// </summary>
        private static void AddStanceTree(
            BlendTree stances, float threshold, string folder,
            string idle, string halfGait, string runGait, string runForward)
        {
            BlendTree tree = stances.CreateBlendTreeChild(threshold);
            tree.name = folder;
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";

            tree.AddChild(LoadClip(folder, idle), Vector2.zero);
            AddRing(tree, folder, halfGait, 0.5f);
            AddRing(tree, folder, runGait, 1f, runForward);
        }

        private static void AddUpperBodyLayer(AnimatorController controller)
        {
            var machine = new AnimatorStateMachine { name = "Upper Body", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);

            controller.AddLayer(new AnimatorControllerLayer
            {
                name = "Upper Body",
                stateMachine = machine,
                avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath),
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 1f
            });

            // No motion: the layer lets the base layer through until something plays.
            AnimatorState empty = machine.AddState("Empty");
            machine.defaultState = empty;

            // Bare hands: the pack's casts as they are. The pack cuts each into
            // start, loop and end; a skill here is instant, so the loop is
            // skipped — wind up, release, back.
            AddCast(machine, empty, Unarmed, SkillEffectKind.Projectile, "Unarmed", "Unarmed-Cast-R-Attack1");
            AddCast(machine, empty, Unarmed, SkillEffectKind.ChainBolt, "Unarmed", "Unarmed-Cast-L-Attack1");
            AddCast(machine, empty, Unarmed, SkillEffectKind.AreaBurst, "Unarmed", "Unarmed-Cast-Dual-AOE1");
            AddCast(machine, empty, Unarmed, SkillEffectKind.PersistentZone, "Unarmed", "Unarmed-Cast-Dual-Summon1");
            AddSwings(machine, empty, Unarmed, "Unarmed",
                "Unarmed-Attack-R1", "Unarmed-Attack-R2", "Unarmed-Attack-R3",
                "Unarmed-Attack-L1", "Unarmed-Attack-L2", "Unarmed-Attack-L3");

            // A sword in the right hand: spells come out of the left one.
            foreach (int held in new[] { OneHanded, TwoHanded })
            {
                AddCast(machine, empty, held, SkillEffectKind.Projectile, "Armed", "Armed-Cast-L-Attack1");
                AddCast(machine, empty, held, SkillEffectKind.ChainBolt, "Armed", "Armed-Cast-L-Attack2");
                AddCast(machine, empty, held, SkillEffectKind.AreaBurst, "Armed", "Armed-Cast-L-AOE1");
                AddCast(machine, empty, held, SkillEffectKind.PersistentZone, "Armed", "Armed-Cast-L-Summon1");
            }

            // The right hand's swings only: the sword is in that one.
            AddSwings(machine, empty, OneHanded, "1Hand-Sword",
                "Sword-Attack-R1", "Sword-Attack-R2", "Sword-Attack-R3", "Sword-Attack-R4",
                "Sword-Attack-R5", "Sword-Attack-R6", "Sword-Attack-R7");
            AddSwings(machine, empty, TwoHanded, "2Hand-Sword",
                "2Hand-Sword-Attack1", "2Hand-Sword-Attack2", "2Hand-Sword-Attack3", "2Hand-Sword-Attack4",
                "2Hand-Sword-Attack5", "2Hand-Sword-Attack6", "2Hand-Sword-Attack7", "2Hand-Sword-Attack8");

            // A bow in the left hand: spells come out of the right one, a
            // projectile is the bow itself, and a melee arc is the pack's bow
            // bash. The chain bolt is a spell, not a shot — it jumps, it does
            // not fly.
            AddCast(machine, empty, Bow, SkillEffectKind.ChainBolt, "Armed", "Armed-Cast-R-Attack1");
            AddCast(machine, empty, Bow, SkillEffectKind.AreaBurst, "Armed", "Armed-Cast-R-AOE1");
            AddCast(machine, empty, Bow, SkillEffectKind.PersistentZone, "Armed", "Armed-Cast-R-Summon1");
            AddShot(machine, empty);

            AddSwings(machine, empty, Bow, "2Hand-Bow",
                "2Hand-Bow-Attack1", "2Hand-Bow-Attack2", "2Hand-Bow-Attack3",
                "2Hand-Bow-Attack4", "2Hand-Bow-Attack5", "2Hand-Bow-Attack6");

            foreach ((int held, string folder, string prefix) in Stances)
            {
                AddOneShot(machine, empty, $"{StanceName(held)} Hit", LoadClip(folder, prefix + "GetHit-F1"),
                    Is("Hit"), EqualTo("Weapon", held));
            }

            // From the back, over the right shoulder, into the right hand — and
            // the same way back. "Unarmed" in the names is where the pack's
            // character ends up, which is where ours does too.
            AddOneShot(machine, empty, "Draw One-Handed", LoadClip("Unarmed", "Unarmed-Unsheath-R-Back"),
                Is("Draw"), EqualTo("Grip", OneHanded)).speed = DrawSpeed;
            AddOneShot(machine, empty, "Sheath One-Handed", LoadClip("Armed", "Armed-Sheath-R-Back-Unarmed"),
                Is("Sheath"), EqualTo("Grip", OneHanded)).speed = DrawSpeed;
            AddOneShot(machine, empty, "Draw Two-Handed", LoadClip("2Hand-Sword", "2Hand-Sword-Unsheath-Back-Unarmed"),
                Is("Draw"), EqualTo("Grip", TwoHanded)).speed = DrawSpeed;
            AddOneShot(machine, empty, "Sheath Two-Handed", LoadClip("2Hand-Sword", "2Hand-Sword-Sheath-Back-Unarmed"),
                Is("Sheath"), EqualTo("Grip", TwoHanded)).speed = DrawSpeed;
            AddOneShot(machine, empty, "Draw Bow", LoadClip("2Hand-Bow", "2Hand-Bow-Unsheath-Back-Unarmed"),
                Is("Draw"), EqualTo("Grip", Bow)).speed = DrawSpeed;
            AddOneShot(machine, empty, "Sheath Bow", LoadClip("2Hand-Bow", "2Hand-Bow-Sheath-Back-Unarmed"),
                Is("Sheath"), EqualTo("Grip", Bow)).speed = DrawSpeed;
        }

        /// <summary>
        /// A bow shot: the string let go, then held drawn until the fight
        /// pauses.
        ///
        /// The arrow has already left at the press, so the release plays at
        /// once, with no pull first. The drawn pose between shots is what makes
        /// the next press read as letting go again rather than as a bow jerking
        /// from rest; Aiming, from the presenter, is what finally lowers it.
        ///
        /// The pack aims in nine directions. The middle one, because the
        /// character already turns to face the aim.
        /// </summary>
        private static void AddShot(AnimatorStateMachine machine, AnimatorState empty)
        {
            AnimatorState release = machine.AddState("Bow Release");
            release.motion = LoadClip("2Hand-Bow", "2Hand-Bow-Aiming-Fire", "2Hand-Bow-Aiming-Fire-CM");

            Enter(machine, release,
                Is("Cast"), EqualTo("CastKind", (int)SkillEffectKind.Projectile), EqualTo("Weapon", Bow));

            AnimatorState hold = machine.AddState("Bow Hold");
            hold.motion = LoadClip("2Hand-Bow", "2Hand-Bow-Aiming-Pull", "2Hand-Bow-Aiming-Pull-CM");

            AnimatorStateTransition nock = release.AddTransition(hold);
            nock.hasExitTime = true;
            nock.exitTime = 0.9f;
            nock.duration = 0.1f;

            AnimatorStateTransition lower = hold.AddTransition(empty);
            lower.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");
            lower.duration = 0.25f;
        }

        private static void AddCast(
            AnimatorStateMachine machine, AnimatorState empty, int held, SkillEffectKind kind, string folder, string file)
        {
            string name = $"{StanceName(held)} {kind}";

            AnimatorState start = machine.AddState(name + " Start");
            start.motion = LoadClip(folder, file, file + "_start");

            AnimatorState end = machine.AddState(name + " End");
            end.motion = LoadClip(folder, file, file + "_end");

            Enter(machine, start, Is("Cast"), EqualTo("CastKind", (int)kind), EqualTo("Weapon", held));

            AnimatorStateTransition release = start.AddTransition(end);
            release.hasExitTime = true;
            release.exitTime = 1f;
            release.duration = 0.05f;

            ReturnTo(end, empty);
        }

        /// <summary>
        /// A melee arc, one clip per AttackIndex. The presenter counts it round,
        /// so a held button reads as a combo instead of the same swing forever.
        ///
        /// Timed by the cooldown, not by the clip. The state's own speed is the
        /// clip's length, so SwingRate — one over the seconds the swing may take
        /// — plays the whole clip in exactly that long, whatever clip it is. A
        /// held button then lands every swing before the next one starts.
        ///
        /// Four swings, alternating the way they sweep: even AttackIndex to the
        /// caster's right, odd back to the left — the parity CastCue.SweepsRight
        /// gives the slash effect, so the two cannot disagree. Which way a clip
        /// sweeps is read off the clip itself and logged, because the pack's
        /// names do not say.
        /// </summary>
        private static void AddSwings(
            AnimatorStateMachine machine, AnimatorState empty, int held, string folder, params string[] files)
        {
            AnimationClip[] clips = files.Select(f => LoadClip(folder, f)).Where(c => c != null).ToArray();
            AnimationClip[] right = clips.Where(c => SweepOf(c) > 0f).ToArray();
            AnimationClip[] left = clips.Where(c => SweepOf(c) <= 0f).ToArray();

            // A stance whose swings all go one way still swings; the slash just
            // mirrors against the arm every other time, and the log says why.
            if (right.Length == 0)
                right = left;
            if (left.Length == 0)
                left = right;
            if (clips.Length == 0)
                return;

            Debug.Log(
                $"[CharacterContentFactory] {StanceName(held)} swings sweeping right: " +
                $"{string.Join(", ", right.Select(c => c.name))}; left: {string.Join(", ", left.Select(c => c.name))}");

            for (int i = 0; i < SwingSlots; i++)
            {
                AnimationClip[] side = i % 2 == 0 ? right : left;
                AnimationClip clip = side[(i / 2) % side.Length];

                AnimatorState state = machine.AddState(
                    $"{StanceName(held)} Swing {i + 1} {(i % 2 == 0 ? "Right" : "Left")}");
                state.motion = clip;
                state.speed = clip != null ? clip.length : 1f;
                state.speedParameter = "SwingRate";
                state.speedParameterActive = true;

                AnimatorStateTransition enter = Enter(machine, state,
                    Is("Cast"), EqualTo("CastKind", (int)SkillEffectKind.MeleeArc),
                    EqualTo("Weapon", held), EqualTo("AttackIndex", i));
                enter.offset = SwingOffset;

                ReturnTo(state, empty);
            }
        }

        /// <summary>Swings per stance. PlayerAnimationPresenter.SwingCount is the same number.</summary>
        private const int SwingSlots = 4;

        /// <summary>
        /// Which way a swing sweeps: the sign of the right hand's fastest
        /// sideways movement, positive to the caster's right. Humanoid clips
        /// carry the hand's goal position as curves, so no model has to be
        /// posed to find out. Zero when the curve is missing.
        /// </summary>
        private static float SweepOf(AnimationClip clip)
        {
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip)
                .FirstOrDefault(b => b.propertyName == "RightHandT.x");

            AnimationCurve curve = binding.propertyName == null
                ? null
                : AnimationUtility.GetEditorCurve(clip, binding);

            if (curve == null)
                return 0f;

            const int Samples = 60;
            float previous = curve.Evaluate(0f);
            float fastest = 0f;

            for (int i = 1; i <= Samples; i++)
            {
                float x = curve.Evaluate(clip.length * i / Samples);

                if (Mathf.Abs(x - previous) > Mathf.Abs(fastest))
                    fastest = x - previous;

                previous = x;
            }

            return fastest;
        }

        private static AnimatorState AddOneShot(
            AnimatorStateMachine machine, AnimatorState empty, string name, AnimationClip clip,
            params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = clip;

            Enter(machine, state, conditions);
            ReturnTo(state, empty);
            return state;
        }

        /// <summary>
        /// From any state, so a new swing or cast interrupts whatever is still
        /// playing rather than queueing behind it.
        /// </summary>
        private static AnimatorStateTransition Enter(
            AnimatorStateMachine machine, AnimatorState state,
            params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);

            foreach ((AnimatorConditionMode mode, float threshold, string parameter) in conditions)
                enter.AddCondition(mode, threshold, parameter);

            enter.canTransitionToSelf = true;
            enter.duration = 0.05f;
            return enter;
        }

        private static void ReturnTo(AnimatorState from, AnimatorState empty)
        {
            AnimatorStateTransition back = from.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.15f;
        }

        private static (AnimatorConditionMode, float, string) Is(string trigger)
            => (AnimatorConditionMode.If, 0f, trigger);

        private static (AnimatorConditionMode, float, string) EqualTo(string parameter, int value)
            => (AnimatorConditionMode.Equals, value, parameter);

        private static string StanceName(int held) => held switch
        {
            OneHanded => "One-Handed",
            TwoHanded => "Two-Handed",
            Bow => "Bow",
            _ => "Unarmed"
        };

        /// <summary>
        /// Packs made for the built-in pipeline — Unity-chan shaders on Fina,
        /// Standard on the swords — which URP draws pink. Each material gets a
        /// URP Lit twin with the same texture and colour, kept in our art folder
        /// so the packs stay untouched.
        /// </summary>
        public static void ConvertMaterials(GameObject model, string folder = null)
        {
            folder ??= CharacterFolder + "/" + model.name;
            SceneBuildUtility.EnsureAssetFolder(folder);

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");

            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                Material[] materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null)
                        materials[i] = ToUrp(materials[i], folder, lit);
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static Material ToUrp(Material source, string folder, Shader lit)
        {
            if (source.shader == lit)
                return source;

            string path = $"{folder}/{source.name}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var material = new Material(lit) { name = source.name };

            if (source.HasProperty("_MainTex"))
                material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));

            material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            material.SetFloat("_Smoothness", 0.15f);

            string shaderName = source.shader.name;

            // Hair and clothes are single sheets of polygons: culled, they show
            // holes from behind.
            if (shaderName.EndsWith("_ds") || source.name.Contains("Hair") || source.name.Contains("Cloth"))
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            // The "blend" shaders are eyes and lashes, cut out by alpha. Clip
            // rather than blend: sorting transparent eyes against a face is a
            // fight not worth having on a top-down camera.
            if (shaderName.Contains("blend"))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.5f);
                material.EnableKeyword("_ALPHATEST_ON");
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void AddRing(
            BlendTree tree, string folder, string gait, float radius, string forward = "Forward")
        {
            float d = radius * Mathf.Sqrt(0.5f);

            tree.AddChild(LoadClip(folder, $"{gait}-{forward}"), new Vector2(0f, radius));
            tree.AddChild(LoadClip(folder, $"{gait}-{forward}-Right"), new Vector2(d, d));
            tree.AddChild(LoadClip(folder, $"{gait}-Right"), new Vector2(radius, 0f));
            tree.AddChild(LoadClip(folder, $"{gait}-Backward-Right"), new Vector2(d, -d));
            tree.AddChild(LoadClip(folder, $"{gait}-Backward"), new Vector2(0f, -radius));
            tree.AddChild(LoadClip(folder, $"{gait}-Backward-Left"), new Vector2(-d, -d));
            tree.AddChild(LoadClip(folder, $"{gait}-Left"), new Vector2(-radius, 0f));
            tree.AddChild(LoadClip(folder, $"{gait}-{forward}-Left"), new Vector2(-d, d));
        }

        /// <summary>
        /// A clip out of one of the pack's FBX files. Most hold one; the casts
        /// hold three, and then the take has to be named.
        /// </summary>
        private static AnimationClip LoadClip(string folder, string file, string take = null)
        {
            string path = $"{Pack}/Animations/{folder}/RPG-Character@{file}.FBX";

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => take == null ? !c.name.StartsWith("__preview__") : c.name == take);

            if (clip == null)
                Debug.LogError($"[CharacterContentFactory] No clip '{take ?? file}' in '{path}'. Is the RPG Character pack imported?");

            return clip;
        }
    }
}
