using UnityEngine;
using UnityEditor;
using ClippedLights;

namespace ClippedLightsEditor {
    [CustomEditor(typeof(ClippedLight)), CanEditMultipleObjects]
    class ClippedLightEditor : Editor {
        int currentPlaneIndex = -1;
        ClippedLightBoundsHandle clippedLightBoundsHandle = new ClippedLightBoundsHandle();

        private enum EditingMode {
            None,
            Bounds,
            Move,
        }

        static EditingMode editingMode = EditingMode.None;
        static ClippedLight editingLight = null;

        private void OnEnable() {
            if (editingMode != EditingMode.None) {
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

        private void RadioButton(GUIContent content, ref EditingMode currentMode, EditingMode selectedMode) {
            bool wasToggled = currentMode == selectedMode;
            bool isToggled = GUILayout.Toggle(wasToggled, content, "Button", GUILayout.Width(40), GUILayout.Height(25));
            if (wasToggled != isToggled) {
                if (isToggled) {
                    currentMode = selectedMode;
                } else {
                    currentMode = EditingMode.None;
                }
            }
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            ClippedLight light = (ClippedLight)target;
            editingLight = (ClippedLight)targets[0];

            EditingMode oldEditing = editingMode;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Edit");

            RadioButton(new GUIContent(EditorGUIUtility.IconContent("EditCollider").image, "Edit the the light clipping planes"), ref editingMode, EditingMode.Bounds);
            RadioButton(new GUIContent(EditorGUIUtility.IconContent("MoveTool").image, "Move the light source independently from its bounds"), ref editingMode, EditingMode.Move);

            EditorGUILayout.EndHorizontal();

            if (editingMode == EditingMode.Bounds && currentPlaneIndex != -1) {
                // TODO: Always draw distances for all planes in the light, not only when in edit mode
                DrawDistanceInspector(light);
            }


            if (oldEditing != editingMode) {
                Tools.hidden = editingMode != EditingMode.None;
                SceneView.RepaintAll();
            }


            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(" ");
            if (GUILayout.Button(new GUIContent("Reset bounds", "Reset the light clipping planes to their default values"))) {
                Undo.RecordObject(light, "Reset Light Bounds");
                light.planes = new[] {
                    new Vector4(1f, 0f, 0f, light.range),
                    new Vector4(-1f, 0f, 0f, light.range),
                    new Vector4(0f, 1f, 0f, light.range),
                    new Vector4(0f, -1f, 0f, light.range),
                    new Vector4(0f, 0f, 1f, light.range),
                    new Vector4(0f, 0f, -1f, light.range),
                };
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
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
                if (clippedLightBoundsHandle.Draw(light, editingMode == EditingMode.Bounds && light == editingLight)) {
                    Undo.RecordObject(light, "Light Plane Change");
                    for (int i = 0; i < light.planes.Length; i++) {
                        light.planes[i] = clippedLightBoundsHandle.GetPlane(i);
                    }
                }
                if (editingMode == EditingMode.Bounds) {
                    int previousIndex = currentPlaneIndex;
                    currentPlaneIndex = clippedLightBoundsHandle.SelectedPlaneIndex;
                    if (previousIndex != currentPlaneIndex) {
                        Repaint();
                    }
                }
            }

            if (editingMode == EditingMode.Bounds && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) {
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