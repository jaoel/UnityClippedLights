using UnityEngine;
using UnityEditor;
using ClippedLights;

namespace ClippedLightsEditor {
    [CustomEditor(typeof(ClippedLight)), CanEditMultipleObjects]
    class ClippedLightEditor : Editor {
        int currentPlaneIndex = -1;
        ClippedLightBoundsHandle clippedLightBoundsHandle = new ClippedLightBoundsHandle();
        static Vector4[] copiedPlanes = null;

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

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel("Edit Mode");
                RadioButton(new GUIContent(EditorGUIUtility.IconContent("EditCollider").image, "Edit the the light clipping planes"), ref editingMode, EditingMode.Bounds);
                RadioButton(new GUIContent(EditorGUIUtility.IconContent("MoveTool").image, "Move the light source independently from its bounds"), ref editingMode, EditingMode.Move);
            }

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel("Planes");
                using (new EditorGUILayout.VerticalScope()) {
                    using (new EditorGUILayout.HorizontalScope()) {
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
                    }
                    using (new EditorGUILayout.HorizontalScope()) {
                        using (new EditorGUI.DisabledGroupScope(targets.Length != 1)) {
                            if (GUILayout.Button("Copy") && targets.Length == 1) {
                                copiedPlanes = new Vector4[6];
                                System.Array.Copy(light.planes, copiedPlanes, 6);
                            }
                        }
                        using (new EditorGUI.DisabledGroupScope(copiedPlanes == null)) {
                            if (GUILayout.Button("Paste") && copiedPlanes != null) {
                                for (int i = 0; i < targets.Length; i++) {
                                    if (targets[i] is ClippedLight targetLight) {
                                        Undo.RecordObject(targetLight, "Paste Light Planes");
                                        targetLight.planes = new Vector4[6];
                                        System.Array.Copy(copiedPlanes, targetLight.planes, 6);
                                        SceneView.RepaintAll();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (editingMode == EditingMode.Bounds && currentPlaneIndex != -1) {
                // TODO: Always draw distances for all planes in the light, not only when in edit mode
                DrawDistanceInspector(light);
            }


            if (oldEditing != editingMode) {
                Tools.hidden = editingMode != EditingMode.None;
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