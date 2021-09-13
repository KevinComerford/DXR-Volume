Shader "Unlit/EmissiveRT"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                fixed4 col = tex2D(_MainTex, i.uv);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    
////////////////////////////////////////////////////////////

        Pass{

            Name "Raytest"
            Tags{ "LightMode" = "Meta" }
            HLSLPROGRAM
  
            #include "UnityRaytracingMeshUtils.cginc"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            #pragma raytracing test
  
            struct RayPayload
            {
                float4 color;
            };
  
            struct AttributeData
            {
                float2 barycentrics;
            };

            struct Vertex
            {
                float2 texcoord;
                float3 normal;
            };

            
            Texture2D<float4> _MainTex;
            SamplerState sampler_MainTex;

            Vertex FetchVertex(uint vertexIndex)
            {
                Vertex v;
                v.normal = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
                return v;
            }

            Vertex InterpolateVertices(Vertex v0, Vertex v1, Vertex v2, float3 barycentrics)
            {
                Vertex v;
                #define INTERPOLATE_ATTRIBUTE(attr) v.attr = v0.attr * barycentrics.x + v1.attr * barycentrics.y + v2.attr * barycentrics.z
                INTERPOLATE_ATTRIBUTE(normal);
                return v;
            }

  
            //https://coty.tips/raytracing-in-unity/
            [shader("closesthit")]
            void MyClosestHit(inout RayPayload payload,
                AttributeData attributes : SV_IntersectionAttributes) {
                uint2 launchIdx = DispatchRaysIndex();
            //    ShadingData shade = getShadingData( PrimitiveIndex(), attribs );

                uint primitiveIndex = PrimitiveIndex();
                uint3 triangleIndicies = UnityRayTracingFetchTriangleIndices(primitiveIndex);
                Vertex v0, v1, v2;

                v0.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.x, kVertexAttributeTexCoord0);
                v1.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.y, kVertexAttributeTexCoord0);
                v2.texcoord = UnityRayTracingFetchVertexAttribute2(triangleIndicies.z, kVertexAttributeTexCoord0);

                float3 barycentrics = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);

                Vertex vInterpolated;
                vInterpolated.texcoord = v0.texcoord * barycentrics.x + v1.texcoord * barycentrics.y + v2.texcoord * barycentrics.z;

                payload.color = (_MainTex.SampleLevel(sampler_MainTex, vInterpolated.texcoord,0));
            }
            ENDHLSL
        }
    }
    
}
