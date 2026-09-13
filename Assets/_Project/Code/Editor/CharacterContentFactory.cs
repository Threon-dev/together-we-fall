using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TogetherWeFall.Skills;

namespace TogetherWeFall.EditorTools
{
    /// <summary>
    /// The player's body: the Fina model from its store pack, and an animator
    /// controller built from the RPG Character pack's unarmed clips.
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
        private const string ClipFolder = Pack + "/Animations/Unarmed";
        private const string UpperBodyMaskPath = Pack + "/Avatars/RPG-Character-Upperbody-AvatarMask.mask";

        private const string CharacterFolder = SceneBuildUtility.ArtFolder + "/Characters";
        private const string ControllerPath = CharacterFolder + "/PlayerLocomotion.controller";

        /// <summary>
        /// Two layers.
        ///
        /// Base: locomotion, and death over it. Idle in the middle of the blend
        /// tree, a ring of strafing walks at half a stick, a ring of runs at the
        /// edge — keys always give a full stick, so the walks are for a gamepad.
        ///
        /// Upper body: casts and flinches, masked to the waist up, so a
        /// character can shoot while backing away and the legs keep running.
        /// </summary>
        public static RuntimeAnimatorController CreateLocomotionController()
        {
            SceneBuildUtility.EnsureAssetFolder(CharacterFolder);

            AnimatorController controller = LoadEmptiedOrCreate();

            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cast", AnimatorControllerParameterType.Trigger);

            // SkillEffectKind as a number: which kind of skill decides the pose.
            controller.AddParameter("CastKind", AnimatorControllerParameterType.Int);

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

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";

            tree.AddChild(LoadClip("Idle"), Vector2.zero);
            AddRing(tree, "Strafe", 0.5f);
            AddRing(tree, "Run", 1f);

            AnimatorState death = machine.AddState("Death");
            death.motion = LoadClip("Death1");

            AnimatorStateTransition die = machine.AddAnyStateTransition(death);
            die.AddCondition(AnimatorConditionMode.If, 0f, "Dead");
            die.canTransitionToSelf = false;
            die.duration = 0.1f;

            // Revive, for the day there is one: nothing lowers Dead yet.
            AnimatorStateTransition rise = death.AddTransition(locomotion);
            rise.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            rise.duration = 0.3f;
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

            // The pack cuts each cast into start, loop and end. A skill here is
            // instant, so the loop is skipped: wind up, release, back.
            AddCast(machine, empty, SkillEffectKind.Projectile, "Cast-R-Attack1");
            AddCast(machine, empty, SkillEffectKind.ChainBolt, "Cast-L-Attack1");
            AddCast(machine, empty, SkillEffectKind.AreaBurst, "Cast-Dual-AOE1");
            AddCast(machine, empty, SkillEffectKind.PersistentZone, "Cast-Dual-Summon1");

            AnimatorState melee = machine.AddState("MeleeArc");
            melee.motion = LoadClip("Attack-R1");
            EnterCast(machine, melee, SkillEffectKind.MeleeArc);
            ReturnTo(melee, empty);

            AnimatorState hit = machine.AddState("Hit");
            hit.motion = LoadClip("GetHit-F1");

            AnimatorStateTransition flinch = machine.AddAnyStateTransition(hit);
            flinch.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            flinch.duration = 0.05f;
            ReturnTo(hit, empty);
        }

        private static void AddCast(
            AnimatorStateMachine machine, AnimatorState empty, SkillEffectKind kind, string file)
        {
            AnimatorState start = machine.AddState($"{kind} Start");
            start.motion = LoadClip(file, $"Unarmed-{file}_start");

            AnimatorState end = machine.AddState($"{kind} End");
            end.motion = LoadClip(file, $"Unarmed-{file}_end");

            EnterCast(machine, start, kind);

            AnimatorStateTransition release = start.AddTransition(end);
            release.hasExitTime = true;
            release.exitTime = 1f;
            release.duration = 0.05f;

            ReturnTo(end, empty);
        }

        /// <summary>
        /// From any state, so a held button with a short cooldown starts the
        /// cast over rather than queueing behind the one still playing.
        /// </summary>
        private static void EnterCast(AnimatorStateMachine machine, AnimatorState state, SkillEffectKind kind)
        {
            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Cast");
            enter.AddCondition(AnimatorConditionMode.Equals, (int)kind, "CastKind");
            enter.canTransitionToSelf = true;
            enter.duration = 0.05f;
        }

        private static void ReturnTo(AnimatorState from, AnimatorState empty)
        {
            AnimatorStateTransition back = from.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.15f;
        }

        /// <summary>
        /// The pack's materials are Unity-chan shaders written for the built-in
        /// pipeline, which URP draws pink. Each one gets a URP Lit twin with the
        /// same texture, kept in our art folder so the pack stays untouched.
        /// </summary>
        public static void ConvertMaterials(GameObject model)
        {
            string folder = CharacterFolder + "/" + model.name;
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

        private static void AddRing(BlendTree tree, string gait, float radius)
        {
            float d = radius * Mathf.Sqrt(0.5f);

            tree.AddChild(LoadClip($"{gait}-Forward"), new Vector2(0f, radius));
            tree.AddChild(LoadClip($"{gait}-Forward-Right"), new Vector2(d, d));
            tree.AddChild(LoadClip($"{gait}-Right"), new Vector2(radius, 0f));
            tree.AddChild(LoadClip($"{gait}-Backward-Right"), new Vector2(d, -d));
            tree.AddChild(LoadClip($"{gait}-Backward"), new Vector2(0f, -radius));
            tree.AddChild(LoadClip($"{gait}-Backward-Left"), new Vector2(-d, -d));
            tree.AddChild(LoadClip($"{gait}-Left"), new Vector2(-radius, 0f));
            tree.AddChild(LoadClip($"{gait}-Forward-Left"), new Vector2(-d, d));
        }

        /// <summary>
        /// A clip out of one of the pack's FBX files. Most hold one; the casts
        /// hold three, and then the take has to be named.
        /// </summary>
        private static AnimationClip LoadClip(string file, string take = null)
        {
            string path = $"{ClipFolder}/RPG-Character@Unarmed-{file}.FBX";

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => take == null ? !c.name.StartsWith("__preview__") : c.name == take);

            if (clip == null)
                Debug.LogError($"[CharacterContentFactory] No clip '{take ?? file}' in '{path}'. Is the RPG Character pack imported?");

            return clip;
        }
    }
}
