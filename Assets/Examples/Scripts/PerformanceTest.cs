using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ClippedLights {
    public class PerformanceTest : MonoBehaviour {
        public new Camera camera;
        public float cameraDistance = 15f;
        public float cameraHeight = 10f;
        public float cameraSpeed = 0.1f;
        public int lightCount = 500;
        public bool useClippedLights = false;


        private float averageFrameTime = 0f;

        class LightContainer {
            public GameObject gameObject;
            public float distance;
            public float speed;
            public float t;
            public float range;
            public Vector3 rotationAxis;
        }

        LightContainer[] lightContainers = new LightContainer[0];

        private void Start() {
            QualitySettings.vSyncCount = 0;

            InstantiateLights();
        }

        private void Update() {
            camera.transform.position = new Vector3(
                Mathf.Cos(Time.time * cameraSpeed) * cameraDistance,
                cameraHeight,
                Mathf.Sin(Time.time * cameraSpeed) * cameraDistance
                );
            camera.transform.LookAt(Vector3.zero);


            for (int i = 0; i < lightContainers.Length; i++) {
                lightContainers[i].gameObject.transform.position = new Vector3(
                    Mathf.Cos(lightContainers[i].t + Time.time * lightContainers[i].speed) * lightContainers[i].distance,
                    lightContainers[i].range * 0.5f,
                    Mathf.Sin(lightContainers[i].t + Time.time * lightContainers[i].speed) * lightContainers[i].distance
                );
                lightContainers[i].gameObject.transform.rotation = Quaternion.AngleAxis(lightContainers[i].t + Time.time * lightContainers[i].speed, lightContainers[i].rotationAxis);
            }

            averageFrameTime = Mathf.Lerp(averageFrameTime, Time.deltaTime, 0.05f);
        }

        private void OnGUI() {
            GUILayout.BeginArea(new Rect(20f, 20f, 200f, 200f));

            bool changed = false;


            GUILayout.Label("Light Count:");

            int oldLightCount = lightCount;
            string lightCountString = GUILayout.TextField(lightCount.ToString());
            if(int.TryParse(lightCountString, out int newLightCount)) {
                lightCount = newLightCount;
                if (lightCount != oldLightCount) {
                    changed = true;
                }
            }

            bool oldUseClippedLights = useClippedLights;
            useClippedLights = GUILayout.Toggle(useClippedLights, "Use clipped lights");
            if(useClippedLights != oldUseClippedLights) {
                changed = true;
            }

            GUILayout.Label(useClippedLights ? "Clipped Lights" : "Regular Lights");
            GUILayout.Label($"ms: {averageFrameTime * 1000f}");
            GUILayout.Label($"fps: {1f / averageFrameTime}");

            GUILayout.EndArea();

            if (changed) {
                InstantiateLights();
            }
        }

        private void InstantiateLights() {
            for (int i = 0; i < lightContainers.Length; i++) {
                LightContainer light = lightContainers[i];
                Destroy(light.gameObject);
            }

            Random.InitState(1337);
            lightContainers = new LightContainer[lightCount];
            for (int i = 0; i < lightContainers.Length; i++) {
                LightContainer container = new LightContainer();
                container.gameObject = new GameObject("Light");
                container.gameObject.transform.position = new Vector3(0f, 2f, 0f);

                Color color = Random.ColorHSV(0f, 1f, 0f, 1f, 1f, 1f);
                float range = Random.Range(1f, 5f);
                float intensity = Random.Range(0.1f, 1f);

                if (useClippedLights) {
                    ClippedLight light = container.gameObject.AddComponent<ClippedLight>();
                    light.Color = color;
                    light.Intensity = intensity;
                    light.Range = range;
                    light.planes = new[] {
                        new Vector4(1f, 0f, 0f, range),
                        new Vector4(-1f, 0f, 0f, range),
                        new Vector4(0f, 1f, 0f, range),
                        new Vector4(0f, -1f, 0f, range),
                        new Vector4(0f, 0f, 1f, range),
                        new Vector4(0f, 0f, -1f, range),
                    };
                    light.RecalculateLightBounds();
                } else {
                    Light light = container.gameObject.AddComponent<Light>();
                    light.color = color;
                    light.intensity = intensity;
                    light.range = range;
                }

                container.distance = Random.insideUnitCircle.magnitude * 10f;
                container.t = Random.Range(0f, Mathf.PI * 2f);
                container.speed = Random.Range(-0.2f, 0.2f);
                container.range = range;
                container.rotationAxis = Random.onUnitSphere;
                lightContainers[i] = container;
            }
        }
    }
}