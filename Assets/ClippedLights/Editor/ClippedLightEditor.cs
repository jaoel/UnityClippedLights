using UnityEngine;
using UnityEditor;
using ClippedLights;

namespace ClippedLightsEditor {
    [CustomEditor(typeof(ClippedLight)), CanEditMultipleObjects]
    class ClippedLightEditor : Editor {
        int currentPlaneIndex = -1;
        ClippedLightBoundsHandle clippedLightBoundsHandle = new ClippedLightBoundsHandle();

        static bool editing = false;
        static ClippedLight editingLight = null;

        private void OnEnable() {
            if (editing) {
                Tools.hidden = true;
            }
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable() {
            Tools.hidden = false;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo() {
            clippedLightBoundsHandle.SetDirty();
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            ClippedLight light = (ClippedLight)target;
            editingLight = (ClippedLight)targets[0];

            bool oldEditing = editing;

            EditorGUILayout.BeginHorizontal();

            editing = GUILayout.Toggle(editing, new GUIContent(EditorGUIUtility.IconContent("EditCollider").image), "Button", GUILayout.Width(40), GUILayout.Height(25));

            // TODO: Implement Move Tool which moves light but keeps planes the same relative to the light
            GUILayout.Toggle(false, new GUIContent(EditorGUIUtility.IconContent("MoveTool").image), "Button", GUILayout.Width(40), GUILayout.Height(25));

            EditorGUILayout.EndHorizontal();

            if (editing && currentPlaneIndex != -1) {
                DrawDistanceInspector(light);
                //DrawRotationInspector(light);
            }


            if (oldEditing != editing) {
                Tools.hidden = editing;
                SceneView.RepaintAll();
            }
        }

        private void DrawRotationInspector(ClippedLight light) {
            Vector3 planeNormal = light.planes[currentPlaneIndex];
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, planeNormal);
            EditorGUI.BeginChangeCheck();
            rotation.eulerAngles = EditorGUILayout.Vector3Field("Rotation", rotation.eulerAngles);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(light, "Light Plane Rotation Change");
                Vector3 euler = rotation * Vector3.up;
                light.planes[currentPlaneIndex] = new Vector4(euler.x, euler.y, euler.z, light.planes[currentPlaneIndex].w);
                SceneView.RepaintAll();
            }
        }

        private void DrawDistanceInspector(ClippedLight light) {
            EditorGUI.BeginChangeCheck();
            float distance = EditorGUILayout.FloatField("Distance", light.planes[currentPlaneIndex].w);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(light, "Light Plane Distance Change");
                light.planes[currentPlaneIndex].w = distance;
                SceneView.RepaintAll();
            }
        }

        private void OnSceneGUI() {
            ClippedLight light = (ClippedLight)target;
            Matrix4x4 matrix = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one);
            using (new Handles.DrawingScope(matrix)) {
                for (int i = 0; i < light.planes.Length; i++) {
                    clippedLightBoundsHandle.SetPlane(i, light.planes[i]);
                }
                if (clippedLightBoundsHandle.Draw(light, editing && light == editingLight)) {
                    Undo.RecordObject(light, "Light Plane Change");
                    for(int i = 0; i < light.planes.Length; i++) {
                        light.planes[i] = clippedLightBoundsHandle.GetPlane(i);
                    }
                }
                if (editing) {
                    int previousIndex = currentPlaneIndex;
                    currentPlaneIndex = clippedLightBoundsHandle.SelectedPlaneIndex;
                    if (previousIndex != currentPlaneIndex) {
                        Repaint();
                    }
                }
            }

            if (editing && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) {
                if (currentPlaneIndex != -1) {
                    clippedLightBoundsHandle.SelectedPlaneIndex = currentPlaneIndex = -1;
                }
                Event.current.Use();
                Repaint();
            }

            Handles.color = new Color(0.99215686274f, 0.98823529411f, 0.53333333333f, 0.5f);
            ClippedLightHandle.Draw(light.transform.position, light.range);
        }
    }
}