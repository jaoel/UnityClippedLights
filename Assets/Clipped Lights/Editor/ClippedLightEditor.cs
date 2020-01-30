using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace ClippedLights {
    [CustomEditor(typeof(ClippedLight)), CanEditMultipleObjects]
    class ClippedLightEditor : Editor {
        int currentPlaneIndex = -1;
        bool editing = false;
        Tool LastTool = Tool.None;

        private void OnEnable() {
            LastTool = Tools.current;
        }

        private void OnDisable() {
            if (editing) {
                Tools.current = LastTool;
                editing = false;
                currentPlaneIndex = -1;
            }
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();

            for (int i = 0; i < targets.Length; i++) {
                Object target = targets[i];
                ClippedLight light = (ClippedLight)target;
            }

            bool oldEditing = editing;

            editing = GUILayout.Toggle(editing, new GUIContent(EditorGUIUtility.IconContent("EditCollider").image), "Button", GUILayout.Width(40), GUILayout.Height(25));

            if (oldEditing != editing) {
                if (editing) {
                    LastTool = Tools.current;
                    Tools.current = Tool.None;
                } else {
                    Tools.current = LastTool;
                }
                SceneView.RepaintAll();
            }
        }

        private void OnSceneGUI() {
            ClippedLight light = (ClippedLight)target;

            if (editing) {
                if(Tools.current != Tool.None) {
                    Tools.current = Tool.None;
                }
                Matrix4x4 matrix = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one);
                using (new Handles.DrawingScope(matrix)) {
                    Handles.color = Color.white;
                    for (int i = 0; i < light.planes.Length; i++) {
                        float prevDistance = light.planes[i].w;
                        light.planes[i].w = ClippedLightPlaneHandle.Draw(light.planes[i], light.range, currentPlaneIndex == i);
                        if (prevDistance != light.planes[i].w) {
                            currentPlaneIndex = i;
                        }
                    }
                }
            }


            Handles.color = new Color(0.99215686274f, 0.98823529411f, 0.53333333333f, 0.5f);
            ClippedLightHandle.Draw(light.transform.position, light.range);
        }
    }
}