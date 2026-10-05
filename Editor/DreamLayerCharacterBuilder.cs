using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DreamLayer.SpriteImporter
{
    /// <summary>Turns a set of imported DreamLayer clips into one character prefab with an Animator.</summary>
    public static class DreamLayerCharacterBuilder
    {
        [MenuItem("Assets/DreamLayer/Build Character From Selected Clips", priority = 41)]
        private static void BuildFromSelection()
        {
            var clips = Selection.GetFiltered<AnimationClip>(SelectionMode.Assets).OrderBy(c => c.name).ToArray();
            if (clips.Length == 0)
            {
                EditorUtility.DisplayDialog("DreamLayer", "Select one or more imported DreamLayer animation clips first.", "OK");
                return;
            }
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clips[0]))?.Replace('\\', '/');
            var prefab = Build(clips, folder, "Character");
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        [MenuItem("Assets/DreamLayer/Build Character From Selected Clips", true)]
        private static bool CanBuild() => Selection.GetFiltered<AnimationClip>(SelectionMode.Assets).Length > 0;

        /// <summary>
        /// Creates {name}.controller with one state per clip (the first clip, or one named "idle",
        /// is the default) and {name}.prefab with a SpriteRenderer and Animator.
        /// </summary>
        public static GameObject Build(AnimationClip[] clips, string folder, string name)
        {
            string controllerPath = $"{folder}/{name}.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            AnimatorState defaultState = null;
            foreach (var clip in clips)
            {
                var state = machine.AddState(clip.name);
                state.motion = clip;
                if (defaultState == null || clip.name.ToLowerInvariant().Contains("idle")) defaultState = state;
            }
            machine.defaultState = defaultState;

            var go = new GameObject(name);
            var renderer = go.AddComponent<SpriteRenderer>();
            var firstKey = AnimationUtility.GetObjectReferenceCurve(
                clips[0], EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"));
            if (firstKey != null && firstKey.Length > 0) renderer.sprite = firstKey[0].value as Sprite;
            go.AddComponent<Animator>().runtimeAnimatorController = controller;

            string prefabPath = $"{folder}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return prefab;
        }
    }
}
