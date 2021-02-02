using UnityEngine;
using UnityEditor;
using ClippedLights;

namespace ClippedLightsEditor {
    [CustomEditor(typeof(ClippedLight)), CanEditMultipleObjects]
    partial class ClippedLightEditor : Editor {
        int currentPlaneIndex = -1;
        int previousPlaneIndex = -1;
        ClippedLightBoundsHandle clippedLightBoundsHandle = new ClippedLightBoundsHandle();
        ClippedLightTransformChange transformChange = null;
        
        Vector3[] editingPlaneAxis = null;

        private enum EditingMode {
            None,
            Bounds,
            Move,
        }

        static EditingMode editingMode = EditingMode.None;
        static ClippedLight editingLight = null;
        static Vector4[] copiedPlanes = null;

        private void OnEnable() {
            if (editingMode == EditingMode.Bounds) {
                Tools.hidden = true;
            }
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable() {
            Tools.hidden = false;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo() {
            clippedLightBoundsHandle.ShouldResetPosition();
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
            if (GUI.changed) {
                clippedLightBoundsHandle.ShouldRecalculateBounds();
            }

            if (editingPlaneAxis == null || editingPlaneAxis.Length != 6) {
                editingPlaneAxis = new Vector3[6];
            }

            ClippedLight light = (ClippedLight)target;
            if (light != editingLight) {
                currentPlaneIndex = previousPlaneIndex = -1;
                for (int i = 0; i < light.planes.Length; i++) {
                    editingPlaneAxis[i] = light.planes[i];
                }
            }
            editingLight = (ClippedLight)targets[0];

            EditingMode oldEditing = editingMode;

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel("Edit Mode");
                RadioButton(new GUIContent(EditorGUIUtility.IconContent("EditCollider").image, "Edit the the light clipping planes"), ref editingMode, EditingMode.Bounds);
                RadioButton(new GUIContent(EditorGUIUtility.IconContent("MoveTool").image, "Move the light source independently from its bounds"), ref editingMode, EditingMode.Move);
            }

            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel(" ");
                using (new EditorGUILayout.VerticalScope()) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        if (GUILayout.Button(new GUIContent("Reset bounds", "Reset the light clipping planes to their default values"))) {
                            Undo.RecordObject(light, "Reset Light Bounds");
                            light.planes = new[] {
                                new Vector4(1f, 0f, 0f, light.Range),
                                new Vector4(-1f, 0f, 0f, light.Range),
                                new Vector4(0f, 1f, 0f, light.Range),
                                new Vector4(0f, -1f, 0f, light.Range),
                                new Vector4(0f, 0f, 1f, light.Range),
                                new Vector4(0f, 0f, -1f, light.Range),
                            };
                            light.dirty = true;
                            clippedLightBoundsHandle.ShouldRecalculateBounds();
                            SceneView.RepaintAll();
                        }
                    }
                    using (new EditorGUILayout.HorizontalScope()) {
                        using (new EditorGUI.DisabledGroupScope(targets.Length != 1)) {
                            if (GUILayout.Button("Copy Bounds") && targets.Length == 1) {
                                Matrix4x4 localToWorld = Matrix4x4.Transpose(Matrix4x4.Inverse(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one)));

                                copiedPlanes = new Vector4[6];
                                for (int i = 0; i < 6; i++) {
                                    copiedPlanes[i] = localToWorld * light.planes[i];
                                }
                            }
                        }
                        using (new EditorGUI.DisabledGroupScope(copiedPlanes == null)) {
                            if (GUILayout.Button("Paste Bounds") && copiedPlanes != null) {
                                Undo.RecordObjects(targets, "Paste Light Planes");
                                for (int i = 0; i < targets.Length; i++) {
                                    if (targets[i] is ClippedLight targetLight) {
                                        Matrix4x4 worldToLocal = Matrix4x4.Inverse(Matrix4x4.Transpose(Matrix4x4.Inverse(Matrix4x4.TRS(targetLight.transform.position, targetLight.transform.rotation, Vector3.one))));
                                        targetLight.planes = new Vector4[6];
                                        for (int p = 0; p < 6; p++) {
                                            targetLight.planes[p] = worldToLocal * copiedPlanes[p];
                                        }
                                        targetLight.dirty = true;
                                    }
                                }
                                clippedLightBoundsHandle.ShouldRecalculateBounds();
                                SceneView.RepaintAll();
                            }
                        }
                    }
                }
            }

            if (editingMode == EditingMode.Bounds) {
                for (int i = 0; i < light.planes.Length; i++) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUILayout.LabelField("Plane " + (i + 1), GUILayout.Width(60));
                        GUILayout.HorizontalSlider(-1f, 0f, 1f);
                    }
                    using (new EditorGUI.IndentLevelScope()) {
                        if (i == currentPlaneIndex) {
                            Rect colorRect = GUILayoutUtility.GetLastRect();
                            colorRect.y += colorRect.height - EditorGUIUtility.singleLineHeight;
                            colorRect.height = EditorGUIUtility.singleLineHeight * 5f;
                            EditorGUI.DrawRect(colorRect, new Color(1f, 1f, 0f, 0.1f));
                        }
                        DrawDistanceInspector(light, i);
                        DrawAxisInspector(light, i);
                    }
                }
            }


            if (oldEditing != editingMode) {
                Tools.hidden = editingMode == EditingMode.Bounds;
                SceneView.RepaintAll();
            }

            previousPlaneIndex = currentPlaneIndex;
        }

        private void DrawDistanceInspector(ClippedLight light, int planeIndex) {
            EditorGUI.BeginChangeCheck();
            float distance = EditorGUILayout.FloatField("Distance", light.planes[planeIndex].w);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(light, "Light Plane Distance Change");
                light.planes[planeIndex].w = distance;
                light.dirty = true;
                clippedLightBoundsHandle.ShouldRecalculateBounds();
                SceneView.RepaintAll();
            }
        }

        private void DrawAxisInspector(ClippedLight light, int planeIndex) {
            bool dirty = editingPlaneAxis[planeIndex] != (Vector3)light.planes[planeIndex];
            editingPlaneAxis[planeIndex] = EditorGUILayout.Vector3Field("Axis" + (dirty ? "*" : ""), editingPlaneAxis[planeIndex]);
            using (new EditorGUILayout.HorizontalScope()) {
                EditorGUILayout.PrefixLabel(" ");
                using (new EditorGUI.DisabledGroupScope(editingPlaneAxis[planeIndex] == Vector3.zero)) {
                    if (GUILayout.Button(new GUIContent("Apply Axis", "Apply the values in the field to the currently selected plane axis"))) {
                        Undo.RecordObject(light, "Apply Light Axis Change");
                        Vector4 plane = editingPlaneAxis[planeIndex].normalized;
                        plane.w = light.planes[planeIndex].w;
                        light.planes[planeIndex] = plane;
                        light.dirty = true;
                        editingPlaneAxis[planeIndex] = plane;
                        clippedLightBoundsHandle.ShouldRecalculateBounds();
                        SceneView.RepaintAll();
                    }
                }
                if (GUILayout.Button(new GUIContent("Revert Axis", "Revert the field back to the currently selected plane axis value"))) {
                    editingPlaneAxis[planeIndex] = light.planes[planeIndex];
                }
            }
        }

        private void OnSceneGUI() {
            ClippedLight light = (ClippedLight)target;
            if (!light.enabled) {
                return;
            }

            if (transformChange == null) {
                transformChange = new ClippedLightTransformChange(light.transform);
            }

            if (light.transform.hasChanged) {
                light.transform.hasChanged = false;
                clippedLightBoundsHandle.ShouldRecalculateBounds();
            }

            // TODO: Add support for moving multiple selected lights at the same time
            if (editingMode == EditingMode.Move) {
                if (transformChange.Changed()) {
                    Matrix4x4 worldToLocal = Matrix4x4.Inverse(Matrix4x4.Transpose(Matrix4x4.Inverse(Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one))));
                    Matrix4x4 localToWorldOld = Matrix4x4.Transpose(Matrix4x4.Inverse(Matrix4x4.TRS(transformChange.position, transformChange.rotation, Vector3.one)));
                    for (int i = 0; i < light.planes.Length; i++) {
                        Vector4 oldWorldPlane = localToWorldOld * light.planes[i];
                        Vector4 newWorldPlane = worldToLocal * oldWorldPlane;
                        light.planes[i] = NormalizePlane(newWorldPlane);
                        light.dirty = true;
                    }
                    light.UpdateLightBoundsWorld();
                }
            }
            transformChange.Update();

            Matrix4x4 matrix = Matrix4x4.TRS(light.transform.position, light.transform.rotation, Vector3.one);
            using (new Handles.DrawingScope(matrix)) {
                for (int i = 0; i < light.planes.Length; i++) {
                    clippedLightBoundsHandle.SetPlane(i, light.planes[i]);
                }
                if (clippedLightBoundsHandle.Draw(light, editingMode == EditingMode.Bounds && light == editingLight)) {
                    Undo.RecordObject(light, "Light Plane Change");
                    for (int i = 0; i < light.planes.Length; i++) {
                        light.planes[i] = NormalizePlane(clippedLightBoundsHandle.GetPlane(i));
                        editingPlaneAxis[currentPlaneIndex] = light.planes[i];
                        light.dirty = true;
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
            ClippedLightHandle.Draw(light);
        }

        private Vector4 NormalizePlane(Vector4 plane) {
            float magnitude = Vector3.Magnitude(plane);
            return plane / magnitude;
        }
    }
}