using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ClippedLights {
	public class ClippedLightManager {
		private static ClippedLightManager _instance = null;
		public static ClippedLightManager Instance {
			get {
				if (_instance == null) {
					_instance = new ClippedLightManager();
				}
				return _instance;
			}
		}

		private enum LightType {
			Point,
			PointCookie,
		}

		private class CullingGroupWrapper {
			public CullingGroup cullingGroup = null;

			public CullingGroupWrapper(Camera camera) {
				cullingGroup = new CullingGroup();
				cullingGroup.targetCamera = camera;
			}

			public void Cleanup() {
				cullingGroup.Dispose();
			}
		}

		private class HashedLightList {
			private Dictionary<ClippedLight, int> dict = new Dictionary<ClippedLight, int>();
			public List<ClippedLight> list = new List<ClippedLight>();
			public Dictionary<Camera, CullingGroupWrapper> cullingGroupsWrappers = new Dictionary<Camera, CullingGroupWrapper>();
			public int[] queryResultIndices = new int[256];
			private BoundingSphere[] boundingSpheres = new BoundingSphere[256];
			private bool dirty = false;

			public CullingGroupWrapper GetCullingGroupWrapper(Camera camera) {
				CullingGroupWrapper cullingGroupWrapper;
				if (!cullingGroupsWrappers.TryGetValue(camera, out cullingGroupWrapper)) {
					cullingGroupWrapper = new CullingGroupWrapper(camera);
					cullingGroupsWrappers[camera] = cullingGroupWrapper;
					cullingGroupWrapper.cullingGroup.SetBoundingSpheres(boundingSpheres);
					dirty = true;
				}
				return cullingGroupWrapper;
			}

			public bool Add(ClippedLight light) {
				if (!dict.ContainsKey(light)) {
					list.Add(light);
					if (queryResultIndices.Length < list.Count) {
						queryResultIndices = new int[queryResultIndices.Length * 2];
					}
					if (boundingSpheres.Length < list.Count) {
						BoundingSphere[] newBoundingSpheres = new BoundingSphere[boundingSpheres.Length * 2];
						boundingSpheres.CopyTo(newBoundingSpheres, 0);
						boundingSpheres = newBoundingSpheres;
						foreach (CullingGroupWrapper cullingGroupWrapper in cullingGroupsWrappers.Values) {
							cullingGroupWrapper.cullingGroup.SetBoundingSpheres(boundingSpheres);
							cullingGroupWrapper.cullingGroup.SetBoundingSphereCount(list.Count);
						}
					}
					dict.Add(light, list.Count - 1);
					boundingSpheres[list.Count - 1] = new BoundingSphere(light.boundingSphereCenter, light.boundingSphereRadius);
					dirty = true;

					return true;
				}
				return false;
			}

			public bool Remove(ClippedLight light) {
				if (dict.TryGetValue(light, out int listIndex)) {
					ClippedLight lastLight = list[list.Count - 1];
					BoundingSphere lastSphere = boundingSpheres[list.Count - 1];

					// Swap back with removed light;
					if (lastLight != light) {
						list[listIndex] = lastLight;
						dict[lastLight] = listIndex;
						boundingSpheres[listIndex] = lastSphere;
					}

					// Remove last element in the list and remove the light from the dictionary
					list.RemoveAt(list.Count - 1);
					dict.Remove(light);

					dirty = true;

					return true;
				}
				return false;
			}

			public void UpdateLightBounds(ClippedLight light) {
				if (dict.TryGetValue(light, out int listIndex)) {
					BoundingSphere boundingSphere = boundingSpheres[listIndex];
					boundingSphere.position = light.boundingSphereCenter;
					boundingSphere.radius = light.boundingSphereRadius;
					boundingSpheres[listIndex] = boundingSphere;

					dirty = true;
				}
			}

			public void Update() {
				if (dirty) {
					foreach (CullingGroupWrapper cullingGroupWrapper in cullingGroupsWrappers.Values) {
						cullingGroupWrapper.cullingGroup.SetBoundingSphereCount(list.Count);
					}
				}
				dirty = false;
			}

			public void Cleanup() {
				foreach (CullingGroupWrapper cullingGroupWrapper in cullingGroupsWrappers.Values) {
					cullingGroupWrapper.Cleanup();
				}
				cullingGroupsWrappers.Clear();
			}
		}

		public static readonly int maxPlanesPerLight = 6;
		public static readonly string commandBufferName = "Deferred clipped lights";
		public static int maxCullingResults = 256;

		private const int SHADER_PASS_ALL = -1;
		private const int SHADER_PASS_LIGHTING = 0;
		private const int LIGHT_MESH_SUBDIVISION_LEVEL = 0;
		private const float LIGHT_MESH_RADIUS = 1.3f;

#if UNITY_EDITOR
		private static bool refreshReflectionCamera = false;
#endif
		private HashedLightList pointLights = new HashedLightList();
		private HashedLightList pointCookieLights = new HashedLightList();

		private HashSet<Camera> cameras = new HashSet<Camera>();
		private Shader shader;
		private Material pointLightMaterial;
		private Material pointLightMaterialInside;
		private Mesh sphereMesh;
		private CommandBuffer commandBuffer;
		private MaterialPropertyBlock propertyBlock;
		private Matrix4x4 lightGeometryMatrix = Matrix4x4.identity;
		private Vector4[] lightPlanes = new Vector4[6];
		private int globalLightLayermask = 0;

		private Texture currentTexture = null;

		private int _LightColor;
		private int _LightPos;
		private int _SrcBlend;
		private int _DstBlend;
		private int _ZTest;
		private int _Cull;
		private int _StencilPassFailZFail;
		private int _Ref;
		private int _NumPlanes;
		private int _Planes;
		private int _LightTexture0;
		private int _InvBlendDistance;
		private int unity_WorldToLight;

		private ClippedLightManager() {
			shader = Shader.Find("Hidden/ClippedLights");

			pointLightMaterial = new Material(shader);
			pointLightMaterial.hideFlags = HideFlags.HideAndDontSave;
			pointLightMaterial.enableInstancing = true;

			pointLightMaterialInside = new Material(shader);
			pointLightMaterialInside.hideFlags = HideFlags.HideAndDontSave;
			pointLightMaterialInside.enableInstancing = true;

			sphereMesh = IcoSphere.Generate(1f, LIGHT_MESH_SUBDIVISION_LEVEL);
			sphereMesh.hideFlags = HideFlags.HideAndDontSave;

			commandBuffer = new CommandBuffer();
			commandBuffer.name = commandBufferName;

			propertyBlock = new MaterialPropertyBlock();

			_LightColor = Shader.PropertyToID("_LightColor");
			_LightPos = Shader.PropertyToID("_LightPos");
			_SrcBlend = Shader.PropertyToID("_SrcBlend");
			_DstBlend = Shader.PropertyToID("_DstBlend");
			_ZTest = Shader.PropertyToID("_ZTest");
			_Cull = Shader.PropertyToID("_Cull");
			_StencilPassFailZFail = Shader.PropertyToID("_StencilPassFailZFail");
			_Ref = Shader.PropertyToID("_Ref");
			_NumPlanes = Shader.PropertyToID("_NumPlanes");
			_Planes = Shader.PropertyToID("_Planes");
			_LightTexture0 = Shader.PropertyToID("_LightTexture0");
			unity_WorldToLight = Shader.PropertyToID("unity_WorldToLight");
			_InvBlendDistance = Shader.PropertyToID("_InvBlendDistance");

			pointLightMaterial.SetInt(_ZTest, (int)CompareFunction.LessEqual);
			pointLightMaterial.SetInt(_Cull, (int)CullMode.Back);
			pointLightMaterial.SetInt(_StencilPassFailZFail, (int)StencilOp.Zero);
			pointLightMaterial.SetInt(_Ref, 144);

			pointLightMaterialInside.SetInt(_ZTest, (int)CompareFunction.Greater);
			pointLightMaterialInside.SetInt(_Cull, (int)CullMode.Front);
			pointLightMaterialInside.SetInt(_StencilPassFailZFail, (int)StencilOp.Keep);
			pointLightMaterialInside.SetInt(_Ref, 128);

			Camera.onPreRender += OnPreRenderCallback;
			Camera.onPreCull += OnPreCullCallback;
		}

#if UNITY_EDITOR
		[UnityEditor.Callbacks.DidReloadScripts]
		private static void OnScriptsReloaded() {
			// Clear all command buffers when reloading scripts in the editor
			// to avoid command buffers being added multiple times to the same camera
			Camera[] cameras = SceneUtility.FindAllComponentsInOpenScenes<Camera>(true);
			foreach (Camera camera in cameras) {
				RemoveDuplicateCommandBuffers(camera);
			}

			refreshReflectionCamera = true;
		}

		private static void RemoveDuplicateCommandBuffers(Camera camera) {
			CommandBuffer[] buffers = camera.GetCommandBuffers(CameraEvent.AfterLighting);
			foreach (CommandBuffer buffer in buffers) {
				if (buffer.name == commandBufferName) {
					camera.RemoveCommandBuffer(CameraEvent.AfterLighting, buffer);
				}
			}
		}
#endif

		void Cleanup() {
			_instance = null;
			Camera.onPreRender -= OnPreRenderCallback;
			Camera.onPreCull -= OnPreCullCallback;
			foreach (Camera cam in cameras) {
				if (cam) {
					cam.RemoveCommandBuffer(CameraEvent.AfterLighting, commandBuffer);
				}
			}
			commandBuffer.Dispose();

			Object.DestroyImmediate(pointLightMaterial);
			Object.DestroyImmediate(sphereMesh);

			pointLights.Cleanup();
			pointCookieLights.Cleanup();
		}

		public static void AddLight(ClippedLight light) {
			Instance.AddLightInternal(light);
		}

		private void AddLightInternal(ClippedLight light) {
			if (light.Cookie != null) {
				pointCookieLights.Add(light);
			} else {
				pointLights.Add(light);
			}
			globalLightLayermask |= light.gameObject.layer;
		}

		public static void RemoveLight(ClippedLight light) {
			Instance.RemoveLightInternal(light);
		}

		private void RemoveLightInternal(ClippedLight light) {
			pointLights.Remove(light);
			pointCookieLights.Remove(light);

			// Cleanup if all lights removed
			if (pointLights.list.Count == 0 && pointCookieLights.list.Count == 0) {
				Cleanup();
			}
		}

		private void OnPreCullCallback(Camera currentCamera) {
			pointLights.Update();
			pointCookieLights.Update();
		}

		private void OnPreRenderCallback(Camera currentCamera) {
			if (currentCamera == null)
				return;

			if (((1 << globalLightLayermask) & currentCamera.cullingMask) == 0) {
				return;
			}

#if UNITY_EDITOR
			if (SceneView.lastActiveSceneView != null && !SceneView.lastActiveSceneView.sceneLighting && currentCamera.cameraType == CameraType.SceneView) {
				commandBuffer.Clear();
				return;
			}

			if (currentCamera.cameraType == CameraType.Reflection && refreshReflectionCamera) {
				RemoveDuplicateCommandBuffers(currentCamera);
				cameras.Remove(currentCamera);
				refreshReflectionCamera = false;
			}
#endif

			if (!cameras.Contains(currentCamera)) {
				currentCamera.AddCommandBuffer(CameraEvent.AfterLighting, commandBuffer);
				cameras.Add(currentCamera);
			}

			commandBuffer.Clear();

			if (currentCamera.allowHDR) {
				pointLightMaterial.SetInt(_SrcBlend, (int)BlendMode.One);
				pointLightMaterial.SetInt(_DstBlend, (int)BlendMode.One);
				pointLightMaterialInside.SetInt(_SrcBlend, (int)BlendMode.One);
				pointLightMaterialInside.SetInt(_DstBlend, (int)BlendMode.One);
			} else {
				pointLightMaterial.SetInt(_SrcBlend, (int)BlendMode.DstColor);
				pointLightMaterial.SetInt(_DstBlend, (int)BlendMode.Zero);
				pointLightMaterialInside.SetInt(_SrcBlend, (int)BlendMode.DstColor);
				pointLightMaterialInside.SetInt(_DstBlend, (int)BlendMode.Zero);
			}

			// Unity seems to be setting keywords in previous passes so we need to disable those
			// There's probably a better way to do this...
			commandBuffer.DisableShaderKeyword("DIRECTIONAL");
			commandBuffer.DisableShaderKeyword("SPOT");
			commandBuffer.DisableShaderKeyword("LIGHTMAP_SHADOW_MIXING");
			commandBuffer.DisableShaderKeyword("SHADOWS_SHADOWMASK");

			int cameraLayerMask = currentCamera.cullingMask;
			Vector3 cameraPosition = currentCamera.transform.position;

			commandBuffer.EnableShaderKeyword("POINT");
			commandBuffer.DisableShaderKeyword("POINT_COOKIE");
			DrawLights(currentCamera, pointLights, cameraLayerMask, ref cameraPosition, LightType.Point);

			commandBuffer.EnableShaderKeyword("POINT_COOKIE");
			commandBuffer.DisableShaderKeyword("POINT");
			DrawLights(currentCamera, pointCookieLights, cameraLayerMask, ref cameraPosition, LightType.PointCookie);

		}

		List<ClippedLight> inverted_lights = new List<ClippedLight>();
		List<ClippedLight> render_lights   = new List<ClippedLight>();


		private void DrawLights(Camera currentCamera, HashedLightList lights, int cameraLayerMask, ref Vector3 cameraPosition, LightType lightType) {
			inverted_lights.Clear();
			render_lights.Clear();
			currentTexture = null;
			float toLightMagnitude;
			float lightRadius;
			Vector3 toLight = Vector3.zero;
			Transform lightTransform;

			// Get culling group for current camera
			CullingGroupWrapper cullingGroupWrapper = lights.GetCullingGroupWrapper(currentCamera);
			int numResults = cullingGroupWrapper.cullingGroup.QueryIndices(true, lights.queryResultIndices, 0);

			for (int i = 0; i < lights.list.Count; i++) {
				ClippedLight light = lights.list[i];
				lightTransform = light.transform;
				// Update world bounds if the light's transform has changed
				if (lightTransform.hasChanged) {
					lightTransform.hasChanged = false;
					light.UpdateLightBoundsWorld();
					light.UpdateMatrices();
					lights.UpdateLightBounds(light);
				}
			}

			for (int i = 0; i < numResults; i++) {
				int lightIndex = lights.queryResultIndices[i];
				ClippedLight light = lights.list[lightIndex];

				// Don't draw light if intensity or range is zero
				if (light.Intensity <= 0f || light.Range <= 0f) {
					continue;
				}

				// Cull layer
				if (((1 << light.gameObject.layer) & cameraLayerMask) == 0) {
					continue;
				}

				// Update changed light parameters
				if (light.dirty) {
					light.dirty = false;
				}

				lightRadius = light.boundingSphereRadius * LIGHT_MESH_RADIUS;
				VectorSub(ref light.boundingSphereCenter, ref cameraPosition, ref toLight);
				toLightMagnitude = toLight.magnitude;

				if (toLightMagnitude - currentCamera.nearClipPlane < lightRadius) {
					inverted_lights.Add(light);
					continue;
				}

				render_lights.Add(light);
			}

			// early out if nothing to do
			if (render_lights.Count == 0 && inverted_lights.Count == 0) {
				return;
			}

			if (lightType == LightType.PointCookie) {
				// sort by the cookie texture to try and avoid changing it unnecessarily
				// Array.Sort uses quicksort and is not stable which can lead to random performance fluctuation
				// OrderBy allocates memory
				// so we have to use handwritten insertion sort
				for (int i = 1; i < render_lights.Count; i++) {
					ClippedLight temp = render_lights[i];
					int hashcode = temp.Cookie.GetHashCode();
					int j = i - 1;
					while (j >= 0 && render_lights[j].Cookie.GetHashCode() > hashcode) {
						render_lights[j + 1] = render_lights[j];
						j--;
					}
					render_lights[j + 1] = temp;
					i++;
				}

				for (int i = 1; i < inverted_lights.Count; i++) {
					ClippedLight temp = inverted_lights[i];
					int hashcode = temp.Cookie.GetHashCode();
					int j = i - 1;
					while (j >= 0 && inverted_lights[j].Cookie.GetHashCode() > hashcode) {
						inverted_lights[j + 1] = inverted_lights[j];
						j--;
					}
					inverted_lights[j + 1] = temp;
					i++;
				}
			}

			// First draw lights that are not intersecting the camera near frustum
			foreach (var light in render_lights) {
				lightRadius = light.boundingSphereRadius * LIGHT_MESH_RADIUS;
				CalculateLightGeometryMatrix(ref light.boundingSphereCenter, ref lightRadius);

				// Draw the light
				lightTransform = light.transform;
				SetupLight(light, lightTransform, lightType);
				commandBuffer.DrawMesh(sphereMesh, lightGeometryMatrix, pointLightMaterial, 0, SHADER_PASS_ALL, propertyBlock);
			}

			// Finally draw the lights that are intersecting the camera near frustum
			foreach (var light in inverted_lights) {
				lightRadius = light.boundingSphereRadius * LIGHT_MESH_RADIUS;
				CalculateLightGeometryMatrix(ref light.boundingSphereCenter, ref lightRadius);

				// Draw the light
				lightTransform = light.transform;
				SetupLight(light, lightTransform, lightType);
				commandBuffer.DrawMesh(sphereMesh, lightGeometryMatrix, pointLightMaterialInside, 0, SHADER_PASS_LIGHTING, propertyBlock);
			}
		}

		// TODO: This should porobably be optimized
		private static float SphereConeIntersection(ref float dotLight, ref float tanTheta, ref float cosTheta, ref Vector3 toLight) {
			float b = dotLight * tanTheta;
			float c = Mathf.Sqrt(Dot(ref toLight, ref toLight) - dotLight * dotLight);
			float d = c - b;
			float e = d * cosTheta;
			return e;
		}

		private static float Dot(ref Vector3 a, ref Vector3 b) {
			return a.x * b.x + a.y * b.y + a.z * b.z;
		}

		private static void VectorSub(ref Vector3 left, ref Vector3 right, ref Vector3 result) {
			result.x = left.x - right.x;
			result.y = left.y - right.y;
			result.z = left.z - right.z;
		}

		private static void MatrixMul(ref Matrix4x4 mat, ref Vector4 vec, ref Vector4 result) {
			result.x = mat.m00 * vec.x + mat.m01 * vec.y + mat.m02 * vec.z + mat.m03 * vec.w;
			result.y = mat.m10 * vec.x + mat.m11 * vec.y + mat.m12 * vec.z + mat.m13 * vec.w;
			result.z = mat.m20 * vec.x + mat.m21 * vec.y + mat.m22 * vec.z + mat.m23 * vec.w;
			result.w = mat.m30 * vec.x + mat.m31 * vec.y + mat.m32 * vec.z + mat.m33 * vec.w;
		}

		private void CalculateLightGeometryMatrix(ref Vector3 center, ref float radius) {
			// We can skip allocations and make it faster because we don't care about rotations
			lightGeometryMatrix.m03 = center.x;
			lightGeometryMatrix.m13 = center.y;
			lightGeometryMatrix.m23 = center.z;
			lightGeometryMatrix.m00 = radius * 2f;
			lightGeometryMatrix.m11 = radius * 2f;
			lightGeometryMatrix.m22 = radius * 2f;
		}

		Vector3 currentLightPos;
		Vector4 currentLightPosRange;
		Color currentLightColor;
		private void SetupLight(ClippedLight light, Transform lightTransform, LightType lightType) {
			currentLightPos = lightTransform.position;
			currentLightPosRange.x = currentLightPos.x;
			currentLightPosRange.y = currentLightPos.y;
			currentLightPosRange.z = currentLightPos.z;
			currentLightPosRange.w = 1f / (light.Range * light.Range);

			light.GetColor(ref currentLightColor);

			propertyBlock.SetColor(_LightColor, currentLightColor);
			propertyBlock.SetVector(_LightPos, currentLightPosRange);
			propertyBlock.SetFloat(_InvBlendDistance, 1.0f / Mathf.Lerp(0.00001f, 1f, light.BlendDistance));

			if (lightType == LightType.PointCookie) {
				if (currentTexture != light.Cookie) {
					currentTexture = light.Cookie;
					propertyBlock.SetTexture(_LightTexture0, light.Cookie);
				}
				propertyBlock.SetMatrix(unity_WorldToLight, light.WorldToLocalMatrix);
			}

			int planeCount = 0;
			for (int i = 0; i < 6; i++) {
				if (light.planes[i].w < light.Range) {
					MatrixMul(ref light.PlaneInverseTransformMatrix, ref light.planes[i], ref lightPlanes[planeCount]);
					planeCount++;
				}
			}
			propertyBlock.SetInt(_NumPlanes, planeCount);
			propertyBlock.SetVectorArray(_Planes, lightPlanes);
		}
	}
}