using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;
using ClippedLights;
using System.Collections.Generic;

namespace ClippedLightsEditor {
    partial class ClippedLightEditor {
        public class ClippedLightBoundsHandle {
            enum FaceEventType {
                None,
                Select,
                Deselect,
                Move,
            }

            private static readonly Color colorHandleFront = new Color(1f, 1f, 1f, 1f);
            private static readonly Color colorHandleBehind = new Color(1f, 1f, 1f, 0.25f);
            private static readonly Color colorLineFront = new Color(1f, 1f, 1f, 0.4f);
            private static readonly Color colorLineBehind = new Color(1f, 1f, 1f, 0.05f);

            private Vector4[] planes = new Vector4[6];
            private int currentHotControl = 0;
            private Vector3 dragStartPosition = Vector3.zero;
            private Vector3 rotateStartDirection = Vector3.forward;
            private float dragStartPlaneDistance = 0f;
            private Dictionary<ClippedLight, ClippedLightBoundingVolumeGeometry> cachedBoundingVolumes = new Dictionary<ClippedLight, ClippedLightBoundingVolumeGeometry>();
            private bool dirty = false;

            public int SelectedPlaneIndex { get; set; } = -1;

            public void SetPlane(int planeIndex, Vector4 plane) {
                planes[planeIndex] = plane;
            }

            public Vector4 GetPlane(int planeIndex) {
                return planes[planeIndex];
            }

            public void SetDirty() {
                dirty = true;
            }

            public bool Draw(ClippedLight light, bool edit) {
                Vector3 cameraPos = Handles.inverseMatrix.MultiplyPoint(Camera.current.transform.position);
                if (!cachedBoundingVolumes.TryGetValue(light, out ClippedLightBoundingVolumeGeometry cachedGeometry)) {
                    cachedGeometry = cachedBoundingVolumes[light] = new ClippedLightBoundingVolumeGeometry();
                }
                cachedGeometry.Calculate(light);

                bool changed = false;

                if (edit) {
                    for (int i = 0; i < cachedGeometry.faces.Length; i++) {
                        // Draw selection handles
                        Vector3 axis = -light.planes[i];
                        int sliderControlID = GUIUtility.GetControlID(GetHashCode(), FocusType.Passive);
                        if (SelectedPlaneIndex == i) {
                            if (dirty) {
                                dirty = false;
                                rotateStartDirection = light.planes[i];
                                rotateStartDirection.Normalize();
                                dragStartPlaneDistance = light.planes[i].w;
                                dragStartPosition = GetHandlePosition(light, cachedGeometry.faces[i], light.planes[i]);
                            }
                            if (Tools.current == Tool.Move) {
                                // Draw slider
                                //Vector3 offset = (Vector3)light.planes[i] * planes[i].w;
                                //Vector3 currentPosition = dragStartPosition - offset;
                                Vector3 currentPosition = GetHandlePosition(light, cachedGeometry.faces[i], light.planes[i]);
                                FaceEventType faceEvent = DrawFaceSlider(sliderControlID, currentPosition, axis, cameraPos, out float newDistance);
                                if (faceEvent == FaceEventType.Select || faceEvent == FaceEventType.Deselect) {
                                    dragStartPosition = GetHandlePosition(light, cachedGeometry.faces[i], light.planes[i]);
                                    dragStartPlaneDistance = light.planes[i].w;
                                }
                                if (faceEvent == FaceEventType.Move) {
                                    planes[i] = light.planes[i];
                                    planes[i].w = newDistance;
                                    changed = true;
                                }

                                // Draw move circle
                                DrawWireDiscTwoSided(currentPosition, axis, 1f);
                                Handles.DrawDottedLine(currentPosition, cachedGeometry.faces[i].center, 5f);
                            } else if (Tools.current == Tool.Rotate) {
                                if ((Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp) && Event.current.button == 0) {
                                    rotateStartDirection = light.planes[i];
                                    rotateStartDirection.Normalize();
                                    dragStartPlaneDistance = light.planes[i].w;
                                    dragStartPosition = GetHandlePosition(light, cachedGeometry.faces[i], light.planes[i]);
                                }
                                if (Tools.pivotMode == PivotMode.Pivot) {
                                    EditorGUI.BeginChangeCheck();
                                    Quaternion rotation = Handles.RotationHandle(Quaternion.identity, Vector3.zero);
                                    if (EditorGUI.EndChangeCheck()) {
                                        Quaternion finalRotation = Quaternion.SlerpUnclamped(Quaternion.identity, rotation, Event.current.alt ? 0.1f : 1f);
                                        Vector4 newPlane = finalRotation * rotateStartDirection;
                                        newPlane.w = light.planes[i].w;
                                        planes[i] = newPlane;
                                        changed = true;
                                    }
                                } else {
                                    EditorGUI.BeginChangeCheck();
                                    Quaternion rotation = Handles.RotationHandle(Quaternion.identity, dragStartPosition);
                                    if (EditorGUI.EndChangeCheck()) {
                                        Quaternion finalRotation = Quaternion.SlerpUnclamped(Quaternion.identity, rotation, Event.current.alt ? 0.1f : 1f);
                                        Vector3 newPlane = finalRotation * rotateStartDirection;
                                        newPlane.Normalize();
                                        float angle = Vector3.Angle(dragStartPosition, newPlane);
                                        planes[i] = newPlane;
                                        planes[i].w = -dragStartPosition.magnitude * Mathf.Cos(angle * Mathf.Deg2Rad);
                                        changed = true;
                                    }
                                }
                            }
                        } else {
                            Vector3 handlePosition = GetHandlePosition(light, cachedGeometry.faces[i], light.planes[i]);
                            if (!cachedGeometry.faces[i].included || light.range < light.planes[i].w) {
                                Handles.DrawDottedLine(cachedGeometry.faces[i].center, handlePosition, 5f);
                            }
                            FaceEventType faceEvent = DrawFaceSelector(SelectedPlaneIndex == i ? (dragStartPosition + axis * light.planes[i].w) : handlePosition, axis, cameraPos);
                            if (faceEvent == FaceEventType.Select) {
                                rotateStartDirection = light.planes[i];
                                dragStartPlaneDistance = light.planes[i].w;
                                dragStartPosition = GetHandlePosition(light, cachedGeometry.faces[i], light.planes[i]);
                                SelectedPlaneIndex = i;
                            }
                        }
                    }
                }

                // Draw bounds
                for (int i = 0; i < cachedGeometry.faces.Length; i++) {
                    for (int l = 0; l < cachedGeometry.faces[i].pointIndices.Length; l++) {
                        Vector3 start = cachedGeometry.points[cachedGeometry.faces[i].pointIndices[l]];
                        Vector3 end = cachedGeometry.points[cachedGeometry.faces[i].pointIndices[(l + 1) % cachedGeometry.faces[i].pointIndices.Length]];
                        DrawLineTwoSided(start, end);
                    }
                }

                return changed;
            }

            private Vector3 GetHandlePosition(ClippedLight light, BoundingVolumeFace face, Vector4 plane) {
                if (face.included) {
                    float range = light.range;
                    float planeDistance = plane.w;
                    Vector3 planeNormal = -plane;
                    if (planeDistance > range) {
                        float delta = planeDistance - range;
                        return face.center + planeNormal * delta;
                    } else {
                        return face.center;
                    }
                } else {
                    Vector3 normal = plane;
                    float angle = Vector3.Angle(normal, face.center);
                    float dist = face.center.magnitude * Mathf.Cos(angle * Mathf.Deg2Rad) + plane.w;
                    return face.center - normal * dist;
                }
            }

            private FaceEventType DrawFaceSelector(Vector3 position, Vector3 normal, Vector3 cameraPos) {
                FaceEventType eventType = FaceEventType.None;

                using (new HandlesScope()) {
                    Handles.color = Vector3.Dot(position - cameraPos, normal) < 0f
                        ? colorHandleFront
                        : colorHandleBehind;

                    bool eventWasUsed = Event.current.type == EventType.Used;
                    float size = PrimitiveBoundsHandle.DefaultMidpointHandleSizeFunction(position);

                    if (Handles.Button(position, Quaternion.identity, size, 10f * size, Handles.DotHandleCap)) {
                        eventType = FaceEventType.Select;
                    }
                }

                return eventType;
            }

            private FaceEventType DrawFaceSlider(int controlID, Vector3 position, Vector3 normal, Vector3 cameraPos, out float outDistance) {
                FaceEventType eventType = FaceEventType.None;
                outDistance = 0f;

                using (new HandlesScope()) {
                    Handles.color = Vector3.Dot(position - cameraPos, normal) < 0f
                        ? colorHandleFront
                        : colorHandleBehind;

                    bool eventWasUsed = Event.current.type == EventType.Used;
                    Vector3 newPosition = Handles.Slider(controlID, position, normal, 35f * PrimitiveBoundsHandle.DefaultMidpointHandleSizeFunction(position), Handles.ArrowHandleCap, 0f);

                    // Slider used the event, we can use this to get the type of event
                    if (!eventWasUsed && Event.current.type == EventType.Used) {
                        if (GUIUtility.hotControl == controlID && currentHotControl == 0) {
                            eventType = FaceEventType.Select;
                        }

                        if (currentHotControl == controlID && GUIUtility.hotControl == 0) {
                            eventType = FaceEventType.Deselect;
                        }

                        if (newPosition != position) {
                            eventType = FaceEventType.Move;
                            outDistance = Vector3.Dot(newPosition, normal);
                        }

                        currentHotControl = GUIUtility.hotControl;
                    }
                }

                return eventType;
            }

            private void DrawLineTwoSided(Vector3 start, Vector3 end) {
                using (new HandlesScope()) {
                    Handles.zTest = CompareFunction.Less;
                    Handles.color = colorLineFront;
                    Handles.DrawLine(start, end);

                    Handles.zTest = CompareFunction.GreaterEqual;
                    Handles.color = colorLineBehind;
                    Handles.DrawLine(start, end);
                }
            }

            private void DrawWireDiscTwoSided(Vector3 position, Vector3 axis, float radius) {
                using (new HandlesScope()) {
                    Handles.zTest = CompareFunction.Less;
                    Handles.color = colorLineFront;
                    Handles.DrawWireDisc(position, axis, radius);

                    Handles.zTest = CompareFunction.GreaterEqual;
                    Handles.color = colorLineBehind;
                    Handles.DrawWireDisc(position, axis, radius);
                }
            }
        }
    }
}