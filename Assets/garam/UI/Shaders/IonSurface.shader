Shader "OnYourMark/IonSurface"
{
 Properties { [HDR] _EmissionColor("Ion emission",Color)=(.15,3,2.4,1) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
  Pass
  {
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION; };
   struct Varyings { float4 positionCS:SV_POSITION; float fog:TEXCOORD0; };
   CBUFFER_START(UnityPerMaterial)
    half4 _EmissionColor;
   CBUFFER_END
   Varyings vert(Attributes i){Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.fog=ComputeFogFactor(o.positionCS.z);return o;}
   half4 frag(Varyings i):SV_Target{return half4(MixFog(_EmissionColor.rgb*(.94+.06*sin(_Time.y*1.4)),i.fog),1);}
   ENDHLSL
  }
 }
}
