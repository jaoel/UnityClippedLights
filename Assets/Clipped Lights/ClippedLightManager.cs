using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

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

		public static readonly int maxPlanesPerLight = 6;
		public static readonly string commandBufferName = "Deferred clipped lights";

		private HashSet<ClippedLight> pointLights = new HashSet<ClippedLight>();
		private HashSet<Camera> cameras = new HashSet<Camera>();
		private Shader shader;
		private Material pointLightMaterial;
		private Material pointLightMaterialInside;
		private Mesh sphereMesh;
		private CommandBuffer commandBuffer;
		private MaterialPropertyBlock propertyBlock;
		private Matrix4x4 lightGeometryMatrix = Matrix4x4.identity;
		private Matrix4x4 lightPlaneITMatrix = Matrix4x4.identity;
		private Vector4[] lightPlanes = new Vector4[6];

		private int _LightColor;
		private int _LightPos;
		private int _LightAsQuad;
		private int _SrcBlend;
		private int _DstBlend;
		private int _ZTest;
		private int _Cull;
		private int _StencilPassFailZFail;
		private int _Ref;
		private int _NumPlanes;
		private int _Planes;
		private int _LightTexture0;
		private int _BlendDistance;
		private int unity_WorldToLight;

		private ClippedLightManager() {
			shader = Shader.Find("Hidden/ClippedLights");

			pointLightMaterial = new Material(shader);
			pointLightMaterial.hideFlags = HideFlags.HideAndDontSave;

			pointLightMaterialInside = new Material(shader);
			pointLightMaterialInside.hideFlags = HideFlags.HideAndDontSave;

			sphereMesh = IcoSphere.Generate(1f, 1);
			sphereMesh.hideFlags = HideFlags.HideAndDontSave;

			commandBuffer = new CommandBuffer();
			commandBuffer.name = commandBufferName;

			propertyBlock = new MaterialPropertyBlock();

			_LightColor = Shader.PropertyToID("_LightColor");
			_LightPos = Shader.PropertyToID("_LightPos");
			_LightAsQuad = Shader.PropertyToID("_LightAsQuad");
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
			_BlendDistance = Shader.PropertyToID("_BlendDistance");

			pointLightMaterial.SetInt(_ZTest, (int)CompareFunction.LessEqual);
			pointLightMaterial.SetInt(_Cull, (int)CullMode.Back);
			pointLightMaterial.SetInt(_StencilPassFailZFail, (int)StencilOp.Zero);
			pointLightMaterial.SetInt(_Ref, 144);

			pointLightMaterialInside.SetInt(_ZTest, (int)CompareFunction.Greater);
			pointLightMaterialInside.SetInt(_Cull, (int)CullMode.Front);
			pointLightMaterialInside.SetInt(_StencilPassFailZFail, (int)StencilOp.Keep);
			pointLightMaterialInside.SetInt(_Ref, 128);

			Camera.onPreRender += OnPreRenderCallback;
		}

#if UNITY_EDITOR
		[UnityEditor.Callbacks.DidReloadScripts]
		private static void OnScriptsReloaded() {
			// Clear all command buffers when reloading scripts in the editor
			// to avoid command buffers being added multiple times to the same camera
			Camera[] cameras = SceneUtility.FindAllComponentsInOpenScenes<Camera>(true);
			foreach (Camera camera in cameras) {
				CommandBuffer[] buffers = camera.GetCommandBuffers(CameraEvent.AfterLighting);
				foreach (CommandBuffer buffer in buffers) {
					if (buffer.name == commandBufferName) {
						camera.RemoveCommandBuffer(CameraEvent.AfterLighting, buffer);
					}
				}
			}
		}
#endif

		void CLeanup() {
			_instance = null;
			Debug.Log("Disposing ClippedLightManager");
			Camera.onPreRender -= OnPreRenderCallback;
			foreach (Camera cam in cameras) {
				cam.RemoveCommandBuffer(CameraEvent.AfterLighting, commandBuffer);
			}
			commandBuffer.Dispose();

			Object.DestroyImmediate(pointLightMaterial);
			Object.DestroyImmediate(sphereMesh);
		}

		public static void AddLight(ClippedLight light) {
			Instance.AddLightInternal(light);
		}

		private void AddLightInternal(ClippedLight light) {
			pointLights.Add(light);
		}

		public static void RemoveLight(ClippedLight light) {
			Instance.RemoveLightInternal(light);
		}

		private void RemoveLightInternal(ClippedLight light) {
			pointLights.Remove(light);
		}

		private void OnPreRenderCallback(Camera currentCamera) {
			if (!currentCamera)
				return;

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

			DrawLights(currentCamera, pointLights);

		}

		private void DrawLights(Camera currentCamera, HashSet<ClippedLight> lights) {
			List<ClippedLight> inverted_lights = new List<ClippedLight>();

			// Unity seems to be setting keywords in previous passes so we need to disable those
			// There's probably a better way to do this...
			commandBuffer.DisableShaderKeyword("DIRECTIONAL");
			commandBuffer.DisableShaderKeyword("SPOT");

			// First draw lights that are not intersecting the camera near frustum
			foreach (var light in lights) {
				if (Vector3.Distance(currentCamera.transform.position, light.transform.position) - currentCamera.nearClipPlane < light.range * 1.05) {
					inverted_lights.Add(light);
				} else {
					CalculateLightGeometryMatrix(light);

					// Draw stencil buffer
					commandBuffer.DrawMesh(sphereMesh, lightGeometryMatrix, pointLightMaterial, 0, 0);

					// Draw the light
					DrawLight(light, pointLightMaterial);
				}
			}

			// Finally draw the lights that are intersecting the camera near frustum
			foreach (var light in inverted_lights) {
				CalculateLightGeometryMatrix(light);

				// Draw the light
				DrawLight(light, pointLightMaterialInside);
			}
		}

		private void CalculateLightGeometryMatrix(ClippedLight light) {
			// We can skip allocations and make it faster because we don't care about rotations
			lightGeometryMatrix.m03 = light.transform.position.x;
			lightGeometryMatrix.m13 = light.transform.position.y;
			lightGeometryMatrix.m23 = light.transform.position.z;
			lightGeometryMatrix.m00 = light.range * 2.1f;
			lightGeometryMatrix.m11 = light.range * 2.1f;
			lightGeometryMatrix.m22 = light.range * 2.1f;
		}

		private void DrawLight(ClippedLight light, Material material) {
			Vector3 pos = light.transform.position;
			Vector4 pos_range = new Vector4(pos.x, pos.y, pos.z, 1f / (light.range * light.range));

			propertyBlock.Clear();
			propertyBlock.SetColor(_LightColor, light.GetColor());
			propertyBlock.SetVector(_LightPos, pos_range);
			propertyBlock.SetFloat(_LightAsQuad, 0f);
			propertyBlock.SetFloat(_BlendDistance, Mathf.Lerp(0.00001f, 1f, light.blendDistance));

			if (light.cookie != null) {
				commandBuffer.EnableShaderKeyword("POINT_COOKIE");
				commandBuffer.DisableShaderKeyword("POINT");
				propertyBlock.SetTexture(_LightTexture0, light.cookie);
				propertyBlock.SetMatrix(unity_WorldToLight, light.transform.worldToLocalMatrix);
			} else {
				commandBuffer.EnableShaderKeyword("POINT");
				commandBuffer.DisableShaderKeyword("POINT_COOKIE");
			}

			lightPlaneITMatrix = Matrix4x4.Transpose(Matrix4x4.Inverse(light.transform.localToWorldMatrix));
			int planeCount = 0;
			for (int i = 0; i < 6; i++) {
				if (light.planes[i].w < light.range) {
					lightPlanes[planeCount] = lightPlaneITMatrix * light.planes[i];
					planeCount++;
				}
			}
			propertyBlock.SetInt(_NumPlanes, planeCount);
			propertyBlock.SetVectorArray(_Planes, lightPlanes);

			commandBuffer.DrawMesh(sphereMesh, lightGeometryMatrix, material, 0, 1, propertyBlock);
		}
	}
}