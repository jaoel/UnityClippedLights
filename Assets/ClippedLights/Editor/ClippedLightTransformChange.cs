using UnityEngine;

namespace ClippedLightsEditor {
    partial class ClippedLightEditor {
        class ClippedLightTransformChange {
            Transform transform;
            public Vector3 position;
            public Quaternion rotation;

            public ClippedLightTransformChange(Transform transform) {
                this.transform = transform;
                Update();
            }

            public bool Changed() {
                return PositionChanged() || RotationChanged();
            }

            public void Update() {
                position = transform.position;
                rotation = transform.rotation;
            }

            public bool PositionChanged() {
                return transform.position != position;
            }

            public bool RotationChanged() {
                return transform.rotation != rotation;
            }
        }
    }
}