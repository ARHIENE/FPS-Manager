using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace FPSManager.EditorTools
{
    public static class BattleLocomotionBuilder
    {
        const string ControllerPath = "Assets/Animation/Battle/BattleLocomotion.controller";
        const string WalkDir = "Assets/Asset/Kevin Iglesias 1/Human Animations/Animations/Male/Movement/Walk/HumanM@Walk01_";
        const string RunDir = "Assets/Asset/Kevin Iglesias 1/Human Animations/Animations/Male/Movement/Run/HumanM@Run01_";
        const string IdleClipPath = "Assets/Asset/Kevin Iglesias 1/Human Animations/Animations/Male/Idles/HumanM@Idle01.fbx";

        static readonly string[] PrefabPaths =
        {
            "Assets/Prefabs/AIPlayer.prefab",
            "Assets/Prefabs/AIPlayer_TeamA.prefab",
            "Assets/Prefabs/AIPlayer_TeamB.prefab",
        };

        [MenuItem("FPS Manager/Build Battle Locomotion Controller")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Animation"))
                AssetDatabase.CreateFolder("Assets", "Animation");
            if (!AssetDatabase.IsValidFolder("Assets/Animation/Battle"))
                AssetDatabase.CreateFolder("Assets/Animation", "Battle");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);

            var tree = new BlendTree { name = "Locomotion" };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            tree.useAutomaticThresholds = false;

            const float w = 4.2f;
            const float r = 6.3f;
            const float d = 0.70710678f;

            AddChild(tree, IdleClipPath, 0f, 0f);

            AddChild(tree, WalkDir + "Forward.fbx", 0f, w);
            AddChild(tree, WalkDir + "ForwardRight.fbx", w * d, w * d);
            AddChild(tree, WalkDir + "Right.fbx", w, 0f);
            AddChild(tree, WalkDir + "BackwardRight.fbx", w * d, -w * d);
            AddChild(tree, WalkDir + "Backward.fbx", 0f, -w);
            AddChild(tree, WalkDir + "BackwardLeft.fbx", -w * d, -w * d);
            AddChild(tree, WalkDir + "Left.fbx", -w, 0f);
            AddChild(tree, WalkDir + "ForwardLeft.fbx", -w * d, w * d);

            AddChild(tree, RunDir + "Forward.fbx", 0f, r);
            AddChild(tree, RunDir + "ForwardRight.fbx", r * d, r * d);
            AddChild(tree, RunDir + "Right.fbx", r, 0f);
            AddChild(tree, RunDir + "BackwardRight.fbx", r * d, -r * d);
            AddChild(tree, RunDir + "Backward.fbx", 0f, -r);
            AddChild(tree, RunDir + "BackwardLeft.fbx", -r * d, -r * d);
            AddChild(tree, RunDir + "Left.fbx", -r, 0f);
            AddChild(tree, RunDir + "ForwardLeft.fbx", -r * d, r * d);

            var sm = controller.layers[0].stateMachine;
            var state = sm.AddState("Locomotion");
            state.motion = tree;
            sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            foreach (var path in PrefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                var humanDummy = root.transform.Find("HumanDummy");
                var animator = humanDummy != null ? humanDummy.GetComponent<Animator>() : null;
                if (animator != null)
                {
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                }
                else
                {
                    Debug.LogWarning($"[BattleLocomotionBuilder] Animator not found on HumanDummy in {path}");
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BattleLocomotionBuilder] BattleLocomotion controller built and assigned to 3 prefabs.");
        }

        static void AddChild(BlendTree tree, string clipPath, float x, float y)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                Debug.LogWarning($"[BattleLocomotionBuilder] Clip not found: {clipPath}");
                return;
            }
            tree.AddChild(clip, new Vector2(x, y));
        }
    }
}
