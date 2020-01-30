// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Hidden/ClippedLights" {
	Properties {
		_LightTexture0("", any) = "" {}
		_LightTextureB0("", 2D) = "" {}
		_ShadowMapTexture("", any) = "" {}
		_SrcBlend("", Float) = 1
		_DstBlend("", Float) = 1
		_ZTest("", Float) = 1
		_Cull("", Float) = 1
		_StencilPassFailZFail("", Float) = 0
		_Ref("", Float) = 144
	}

	SubShader {

		// Pass 1: Stencil write pass
		Pass {
			ColorMask 0
			ZClip True
			ZTest LEqual
			ZWrite Off
			Cull Off
			Blend One Zero
			Stencil {
				Ref 192//[_LightCullingMask]
				WriteMask 16//[_???]
				Comp Always
				Pass Keep
				Fail Keep
				ZFail Invert
			}


			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0
			#include "UnityCG.cginc"
			struct a2v {
				float4 pos : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			struct v2f {
				float4 vertex : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};
			v2f vert(a2v v) {
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				o.vertex = UnityObjectToClipPos(v.pos);
				return o;
			}
			fixed4 frag() : SV_Target { return fixed4(0,0,0,0); }
			ENDCG
		}

		// Pass 2: Lighting pass
		//  LDR case - Lighting encoded into a subtractive ARGB8 buffer
		//  HDR case - Lighting additively blended into floating point buffer
		Pass {
			Blend [_SrcBlend] [_DstBlend]
			ZTest[_ZTest]
			ZWrite Off
			Cull[_Cull]
			Stencil {
				Ref [_Ref]//144//[_LightCullingMask]
				ReadMask [_Ref]//144//[_???]
				WriteMask 16//[_???]
				Comp Equal
				Pass [_StencilPassFailZFail]
				Fail [_StencilPassFailZFail]
				ZFail [_StencilPassFailZFail]
			}

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_deferred
			#pragma fragment frag
			#pragma multi_compile_lightpass
			#pragma multi_compile ___ UNITY_HDR_ON

			#pragma exclude_renderers nomrt

			#include "UnityCG.cginc"
			#include "UnityDeferredLibrary.cginc"
			#include "UnityPBSLighting.cginc"
			#include "UnityStandardUtils.cginc"
			#include "UnityGBuffer.cginc"
			#include "UnityStandardBRDF.cginc"

			sampler2D _CameraGBufferTexture0;
			sampler2D _CameraGBufferTexture1;
			sampler2D _CameraGBufferTexture2;

			uniform int _NumPlanes;
			uniform float4 _Planes[6];
			uniform float _BlendDistance;

			half4 CalculateLight(unity_v2f_deferred i)
			{
				float3 wpos;
				float2 uv;
				float atten, fadeDist;
				UnityLight light;
				UNITY_INITIALIZE_OUTPUT(UnityLight, light);
				UnityDeferredCalculateLightParams(i, wpos, uv, light.dir, atten, fadeDist);

				float min_dist = 1.0;
				for (int i = 0.0; i < _NumPlanes; i++) {
					float dist = dot(_Planes[i], float4(wpos, 1));
					if (dist < 0.0) {
						return 0.0;
					}
					min_dist = min(min_dist, dist);
				}
				min_dist = saturate(min_dist / _BlendDistance);

				light.color = _LightColor.rgb * atten;

				// unpack Gbuffer
				half4 gbuffer0 = tex2D(_CameraGBufferTexture0, uv);
				half4 gbuffer1 = tex2D(_CameraGBufferTexture1, uv);
				half4 gbuffer2 = tex2D(_CameraGBufferTexture2, uv);
				UnityStandardData data = UnityStandardDataFromGbuffer(gbuffer0, gbuffer1, gbuffer2);
				// Uncomment if we want to allow controlling specular contribution of a light via alpha channel
				//data.specularColor *= _LightColor.a;

				float3 eyeVec = normalize(wpos - _WorldSpaceCameraPos);
				half oneMinusReflectivity = 1 - SpecularStrength(data.specularColor.rgb);

				UnityIndirect ind;
				UNITY_INITIALIZE_OUTPUT(UnityIndirect, ind);
				ind.diffuse = 0;
				ind.specular = 0;

				//half4 res = UNITY_BRDF_PBS(data.diffuseColor, data.specularColor, oneMinusReflectivity, lerp(data.smoothness, 0.0f, (1.0 - clamp(_LightColor.a, 0.0f, 1.0f)) * 0.2), data.normalWorld, -eyeVec, light, ind);
				half4 res = UNITY_BRDF_PBS(data.diffuseColor, data.specularColor, oneMinusReflectivity, data.smoothness, data.normalWorld, -eyeVec, light, ind);

				return res * min_dist;
			}

			#ifdef UNITY_HDR_ON
				half4
			#else
				fixed4
			#endif
			frag(unity_v2f_deferred i) : SV_Target
			{
				//return half4(1, 0, 1, 1);
				half4 c = CalculateLight(i);
				#ifdef UNITY_HDR_ON
					return c;
				#else
					return exp2(-c);
				#endif
			}

			ENDCG
		}

	}
	Fallback Off
}