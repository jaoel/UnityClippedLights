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

		// Pass 2: Lighting pass
		//  LDR case - Lighting encoded into a subtractive ARGB8 buffer
		//  HDR case - Lighting additively blended into floating point buffer
		Pass {
			Blend [_SrcBlend] [_DstBlend]
			ZTest[_ZTest]
			ZWrite Off
			Cull[_Cull]

			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_deferred
			#pragma fragment frag
			#pragma multi_compile_lightpass
			#pragma multi_compile ___ UNITY_HDR_ON

			#pragma exclude_renderers nomrt

			#include "UnityCG.cginc"
			#include "UnityPBSLighting.cginc"
			#include "UnityStandardUtils.cginc"
			#include "UnityGBuffer.cginc"
			#include "UnityStandardBRDF.cginc"

			// copied from UnityDeferredLibrary.cginc

#if defined (DIRECTIONAL) || defined (DIRECTIONAL_COOKIE)
#error This shader doesn't support directional lights
#endif  // defined (DIRECTIONAL) || defined (DIRECTIONAL_COOKIE)


#if defined(SPOT)
#error This shader doesn't support spot lights
#endif  // defined (SPOT)


// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

#ifndef UNITY_DEFERRED_LIBRARY_INCLUDED
#define UNITY_DEFERRED_LIBRARY_INCLUDED

// Deferred lighting / shading helpers


// --------------------------------------------------------
// Vertex shader


struct vert_data {
	float4 vertex : POSITION;
	float3 normal : NORMAL;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};


struct unity_v2f_deferred {
    float4 pos : SV_POSITION;
    float4 uv : TEXCOORD0;
    float3 ray : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};


unity_v2f_deferred vert_deferred (vert_data v)
{
    unity_v2f_deferred o;

    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);

    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = ComputeScreenPos(o.pos);
    o.ray = UnityObjectToViewPos(v.vertex) * float3(-1,-1,1);

    return o;
}


// --------------------------------------------------------
// Shadow/fade helpers

// Receiver plane depth bias create artifacts when depth is retrieved from
// the depth buffer. see UnityGetReceiverPlaneDepthBias in UnityShadowLibrary.cginc
#ifdef UNITY_USE_RECEIVER_PLANE_BIAS
    #undef UNITY_USE_RECEIVER_PLANE_BIAS
#endif

#include "UnityShadowLibrary.cginc"


//Note :
// SHADOWS_SHADOWMASK + LIGHTMAP_SHADOW_MIXING -> ShadowMask mode
// SHADOWS_SHADOWMASK only -> Distance shadowmask mode


#endif // UNITY_DEFERRED_LIBRARY_INCLUDED

			// end of UnityDeferredLibrary.cginc

			// --------------------------------------------------------
			// Shared uniforms


			sampler2D _CameraGBufferTexture0;
			sampler2D _CameraGBufferTexture1;
			sampler2D _CameraGBufferTexture2;

			#if defined (SHADOWS_SHADOWMASK)
			sampler2D _CameraGBufferTexture4;
			#endif

			UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

			sampler2D_float _LightTextureB0;

			#if defined (POINT_COOKIE)
			samplerCUBE_float _LightTexture0;
			#else
			sampler2D_float _LightTexture0;
			#endif

			#if defined (SHADOWS_SCREEN)
			sampler2D _ShadowMapTexture;
			#endif

		CBUFFER_START(Light)
			float4 _LightPos;
			float4 _LightColor;
			float4x4 unity_WorldToLight;

			int _NumPlanes;
			float4 _Planes[6];
			float _InvBlendDistance;
		CBUFFER_END  // Light

			// --------------------------------------------------------
			half UnityDeferredSampleShadowMask(float2 uv)
			{
				half shadowMaskAttenuation = 1.0f;

				#if defined (SHADOWS_SHADOWMASK)
					half4 shadowMask = tex2D(_CameraGBufferTexture4, uv);
					shadowMaskAttenuation = saturate(dot(shadowMask, unity_OcclusionMaskSelector));
				#endif

				return shadowMaskAttenuation;
			}

			// --------------------------------------------------------
			half UnityDeferredSampleRealtimeShadow(half fade, float3 vec, float2 uv)
			{
				half shadowAttenuation = 1.0f;

				#if defined(UNITY_FAST_COHERENT_DYNAMIC_BRANCHING) && defined(SHADOWS_SOFT) && !defined(LIGHTMAP_SHADOW_MIXING)
				//avoid expensive shadows fetches in the distance where coherency will be good
				UNITY_BRANCH
				if (fade < (1.0f - 1e-2f))
				{
				#endif

						#if defined(SHADOWS_CUBE)
							shadowAttenuation = UnitySampleShadowmap(vec);
						#endif

				#if defined(UNITY_FAST_COHERENT_DYNAMIC_BRANCHING) && defined(SHADOWS_SOFT) && !defined(LIGHTMAP_SHADOW_MIXING)
				}
				#endif

				return shadowAttenuation;
			}

			// --------------------------------------------------------
			half UnityDeferredComputeShadow(float3 vec, float fadeDist, float2 uv)
			{

				half fade                      = UnityComputeShadowFade(fadeDist);
				half shadowMaskAttenuation     = UnityDeferredSampleShadowMask(uv);
				half realtimeShadowAttenuation = UnityDeferredSampleRealtimeShadow(fade, vec, uv);

				return UnityMixRealtimeAndBakedShadows(realtimeShadowAttenuation, shadowMaskAttenuation, fade);
			}

			// --------------------------------------------------------
			// Common lighting data calculation (direction, attenuation, ...)
			void UnityDeferredCalculateLightParams (
				unity_v2f_deferred i,
				out float3 outWorldPos,
				out float2 outUV,
				out half3 outLightDir,
				out float outAtten,
				out float outFadeDist)
			{
				i.ray = i.ray * (_ProjectionParams.z / i.ray.z);
				float2 uv = i.uv.xy / i.uv.w;

				// read depth and reconstruct world position
				float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
				depth = Linear01Depth (depth);
				float4 vpos = float4(i.ray * depth,1);
				float3 wpos = mul (unity_CameraToWorld, vpos).xyz;

				float fadeDist = UnityComputeShadowFadeDistance(wpos, vpos.z);

				// point light case
					float3 tolight = wpos - _LightPos.xyz;
					half3 lightDir = -normalize (tolight);

					float att = dot(tolight, tolight) * _LightPos.w;
					float atten = tex2D (_LightTextureB0, att.rr).UNITY_ATTEN_CHANNEL;

					atten *= UnityDeferredComputeShadow (tolight, fadeDist, uv);

					#if defined (POINT_COOKIE)
					atten *= texCUBEbias(_LightTexture0, float4(mul(unity_WorldToLight, half4(wpos,1)).xyz, -8)).w;
					#endif //POINT_COOKIE

				outWorldPos = wpos;
				outUV = uv;
				outLightDir = lightDir;
				outAtten = atten;
				outFadeDist = fadeDist;
			}


			half4 CalculateLight(unity_v2f_deferred i)
			{
				float3 wpos;
				float2 uv;
				float atten, fadeDist;
				UnityLight light;
				UNITY_INITIALIZE_OUTPUT(UnityLight, light);
				UnityDeferredCalculateLightParams(i, wpos, uv, light.dir, atten, fadeDist);

				float min_dist = 1.0;
				for (int i = 0; i < _NumPlanes; i++) {
					float dist = dot(_Planes[i], float4(wpos, 1));
					min_dist = min(min_dist, dist);
				}
				if (min_dist < 0.0) {
					return 0.0;
				}
				min_dist = saturate(min_dist * _InvBlendDistance);

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

				half4 res = UNITY_BRDF_PBS(data.diffuseColor, data.specularColor, oneMinusReflectivity, lerp(data.smoothness, 0.0f, (1.0 - clamp(_LightColor.a, 0.0f, 1.0f)) * 0.2), data.normalWorld, -eyeVec, light, ind);
				//half4 res = UNITY_BRDF_PBS(data.diffuseColor, data.specularColor, oneMinusReflectivity, data.smoothness, data.normalWorld, -eyeVec, light, ind);

				return res * min_dist;
			}

			#ifdef UNITY_HDR_ON
				half4
			#else
				fixed4
			#endif
			frag(unity_v2f_deferred i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
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