using UnityEngine;
using UnityEngine.Events;

namespace ClippedLights {
	[ExecuteAlways]
	public class ClippedLight : MonoBehaviour {
		private const float DefaultRange = 10f;

		[SerializeField] private float range = DefaultRange;
		[SerializeField] private Color color = Color.white;
		[SerializeField] private float intensity = 1.0f;
		[SerializeField] private Texture cookie = null;
		[SerializeField, Range(0f, 1f)] private float blendDistance = 0f;

		[System.NonSerialized] public bool dirty = true;
		[System.NonSerialized] private bool alive = false;

		[HideInInspector]
		public Vector4[] planes = new[] {
			new Vector4(1f, 0f, 0f, DefaultRange),
			new Vector4(-1f, 0f, 0f, DefaultRange),
			new Vector4(0f, 1f, 0f, DefaultRange),
			new Vector4(0f, -1f, 0f, DefaultRange),
			new Vector4(0f, 0f, 1f, DefaultRange),
			new Vector4(0f, 0f, -1f, DefaultRange),
		};

		[HideInInspector] public Vector3 boundingSphereCenterLocal = Vector3.zero;
		[HideInInspector] public float boundingSphereRadius = 0f;
		[System.NonSerialized] public Vector3 boundingSphereCenter = Vector3.zero;
		
		[System.NonSerialized] public Matrix4x4 WorldToLocalMatrix = Matrix4x4.identity;
		[System.NonSerialized] public Matrix4x4 PlaneInverseTransformMatrix = Matrix4x4.identity;

		public float Range { get { return range; } set { dirty = range != value; range = value; } }
		public Color Color { get { return color; } set { dirty = color != value; color = value; } }
		public float Intensity { get { return intensity; } set { dirty = intensity != value; intensity = value; } }
		public Texture Cookie { get { return cookie; } set { dirty = cookie != value; cookie = value; } }
		public float BlendDistance { get { return blendDistance; } set { dirty = blendDistance != Mathf.Clamp01(value); blendDistance = Mathf.Clamp01(value); } }

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
			boundingSphereRadius = range;
			boundingSphereCenterLocal = Vector3.zero;
		}

		private void OnValidate() {
			intensity = Mathf.Max(intensity, 0f);
			range = Mathf.Max(range, 0f);
		}
#endif

		public void OnEnable() {
			UpdateLightBoundsWorld();
			UpdateMatrices();
			ClippedLightManager.AddLight(this);
			alive = true;

#if UNITY_EDITOR
			UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += onSceneSavingCallback;
#endif
		}

		public void Start() {
			UpdateLightBoundsWorld();
			UpdateMatrices();
			ClippedLightManager.AddLight(this);
			alive = true;
		}

		private void OnDestroy() {
			if (alive) {
				ClippedLightManager.RemoveLight(this);
			}
			alive = false;
		}

		public void OnDisable() {
			if (alive) {
				ClippedLightManager.RemoveLight(this);
			}
			alive = false;

#if UNITY_EDITOR
			UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= onSceneSavingCallback;
#endif
		}

		private void OnBecameVisible() {
			ClippedLightManager.AddLight(this);
			alive = true;
		}

		private void OnBecameInvisible() {
			if (alive) {
				ClippedLightManager.RemoveLight(this);
			}
			alive = false;
		}

#if UNITY_EDITOR
		public void onSceneSavingCallback(UnityEngine.SceneManagement.Scene scene, string path) {
			// Recalculate bounding sphere on save scene
			RecalculateLightBounds();
		}
#endif

		public void RecalculateLightBounds() {
			ClippedLightBoundingVolumeGeometry geometry = new ClippedLightBoundingVolumeGeometry();
			geometry.Calculate(this);
			RecalculateLightBounds(geometry);
		}

		public void RecalculateLightBounds(ClippedLightBoundingVolumeGeometry geometry) {
			if (geometry.TryGetBoundingSphere(this, out Vector3 center, out float radius)) {
				boundingSphereCenterLocal = center;
				boundingSphereRadius = radius;
			} else {
				boundingSphereCenterLocal = Vector3.zero;
				boundingSphereRadius = range;
			}
			UpdateLightBoundsWorld();
			UpdateMatrices();
			#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
			#endif
		}

		public void UpdateLightBoundsWorld() {
			Matrix4x4 mat = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
			boundingSphereCenter = mat.MultiplyPoint(boundingSphereCenterLocal);
		}

		public void UpdateMatrices() {
			WorldToLocalMatrix = Matrix4x4.Inverse(Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one));
			PlaneInverseTransformMatrix = Matrix4x4.Transpose(WorldToLocalMatrix);
		}

		public void GetColor(ref Color outColor) {
			outColor.r = color.r * intensity;
			outColor.g = color.g * intensity;
			outColor.b = color.b * intensity;
			outColor.a = color.a;
		}

		public void OnDrawGizmos() {
			Gizmos.DrawIcon(transform.position, "clipped_light_icon", true, color);
		}
	}
}