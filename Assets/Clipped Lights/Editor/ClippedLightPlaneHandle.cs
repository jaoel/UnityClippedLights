using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ClippedLights {
    public class ClippedLightPlaneHandle {
        public static float Draw(Vector4 plane, float lightRange, bool drawCircle) {
            Color prevColor = Handles.color;

            Vector3 axis = -plane;
            float distance = plane.w;
            Vector3 axisVector = axis * distance;
            Color color = Handles.color;

            Vector3 pos = axis * plane.w;
            float size = BoxBoundsHandle.DefaultMidpointHandleSizeFunction(pos);
            Handles.color = Color.black;
            Handles.DotHandleCap(0, pos, Quaternion.identity, size * 1.5f, EventType.Repaint);
            Handles.color = prevColor;
            pos = Handles.Slider(pos, axis, size, Handles.DotHandleCap, 0f);

            if (drawCircle && distance < lightRange && distance > -lightRange) {
                float circleRadius = Mathf.Sin(Mathf.Acos(distance / lightRange)) * lightRange;
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                color.a = 0.05f;
                Handles.color = color;
                Handles.DrawSolidDisc(axisVector, axis, circleRadius);

                Handles.zTest = UnityEngine.Rendering.CompareFunction.Greater;
                color.a = 0.025f;
                Handles.color = color;
                Handles.DrawSolidDisc(axisVector, axis, circleRadius);

                //color.a = 1f;
                //Handles.color = color;
                //Handles.DrawWireDisc(axisVector, axis, circleRadius);
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            color.a = 1f;
            Handles.color = color;
            Handles.DrawDottedLine(axisVector, axis * lightRange, 5f);

            Handles.color = prevColor;

            return Vector3.Dot(pos, axis);
        }
    }
}