using UnityEditor;
using UnityEngine;

namespace ClippedLightsEditor {
    // This is a handle that sort of emulates Unity's built-in light handle sphere
    public class ClippedLightHandle {
        public static void Draw(Vector3 position, float radius) {
            DrawAxisCircle(position, Vector3.right, radius);
            DrawAxisCircle(position, Vector3.up, radius);
            DrawAxisCircle(position, Vector3.forward, radius);

            if (Camera.current.orthographic) {
                Handles.DrawWireDisc(position, Camera.current.transform.forward, radius);
            } else {
                DrawOutlineCircle(position, radius);
            }
        }

        private static void DrawAxisCircle(Vector3 position, Vector3 axis, float radius) {
            using (new HandlesScope()) {
                Color backColor = Handles.color;
                backColor.a = 0.2f;

                // The vector to the furthest point on the sphere we can see from the camera will always be at
                // a 90 degree angle to the vector from that point to the circle's center with a length of radius
                Vector3 cameraPos = Handles.inverseMatrix.MultiplyPoint(Camera.current.transform.position);
                Vector3 toCamera = cameraPos - position;
                float distance = toCamera.magnitude;

                // If the distance to the camera is less than the radius were inside the sphere.
                // In this case everything should have alpha, so we only need to draw one wire disc.
                if (distance < radius) {
                    Handles.color = backColor;
                    Handles.DrawWireDisc(position, axis, radius);
                } else {
                    float angle = Mathf.Acos(radius / distance) * Mathf.Rad2Deg;
                    Vector3 fromVector = Vector3.Cross(axis, toCamera).normalized;
                    fromVector = Quaternion.AngleAxis(angle - 90f, axis) * fromVector;
                    angle = 2f * angle;
                    Handles.DrawWireArc(position, axis, fromVector, -angle, radius);
                    Handles.color = backColor;
                    Handles.DrawWireArc(position, axis, fromVector, 360f - angle, radius);
                }
            }
        }

        private static void DrawOutlineCircle(Vector3 position, float radius) {
            // Extrapolate the vector orthogonal to the view direction
            Vector3 cameraPos = Handles.inverseMatrix * Camera.current.transform.position;
            Vector3 toCamera = cameraPos - position;
            float distance = toCamera.magnitude;
            float angle = Mathf.Asin(radius / distance);
            float extrapolatedRadius = Mathf.Tan(angle) * distance;
            Handles.DrawWireDisc(position, toCamera.normalized, extrapolatedRadius);
        }
    }
}