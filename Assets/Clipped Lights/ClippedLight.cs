using UnityEngine;

namespace ClippedLights {
	[ExecuteAlways]
	public class ClippedLight : MonoBehaviour {
		private const float DefaultRange = 10f;

		public float range = DefaultRange;
		public Color color = Color.white;
		public float intensity = 1.0f;
		public Texture cookie = null;
		[Range(0f, 1f)] public float blendDistance = 0f;

		[HideInInspector]
		public Vector4[] planes = new[] {
			new Vector4(1f, 0f, 0f, DefaultRange),
			new Vector4(-1f, 0f, 0f, DefaultRange),
			new Vector4(0f, 1f, 0f, DefaultRange),
			new Vector4(0f, -1f, 0f, DefaultRange),
			new Vector4(0f, 0f, 1f, DefaultRange),
			new Vector4(0f, 0f, -1f, DefaultRange),
		};

#if UNITY_EDITOR
		private void Reset() {
			Light light = GetComponent<Light>();
			if (light) {
				range = light.range;
				color = light.color;
				intensity = light.intensity;
				cookie = light.cookie;
				for (int i = 0; i < planes.Length; i++) {
					planes[i].w = range;
				}
			}
		}
#endif

		private void Awake() {
			ClippedLightManager.AddLight(this);
		}

		public void OnEnable() {
			ClippedLightManager.AddLight(this);
		}

		public void Start() {
			ClippedLightManager.AddLight(this);
		}

		public void OnDisable() {
			ClippedLightManager.RemoveLight(this);
		}

		private void OnBecameVisible() {
			// TODO: Implement CullingGroup
			ClippedLightManager.AddLight(this);
		}

		private void OnBecameInvisible() {
			// TODO: Implement CullingGroup
			ClippedLightManager.RemoveLight(this);
		}

		public Color GetColor() {
			return new Color(
				color.r * intensity,
				color.g * intensity,
				color.b * intensity,
				color.a
			);
		}

		public void OnDrawGizmos() {
			Gizmos.DrawIcon(transform.position, "clipped_light_icon", true);
		}
	}
}