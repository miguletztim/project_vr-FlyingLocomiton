Shader "Custom/GridShader"
{
    Properties
    {
        _GridColor ("Grid Color", Color) = (0, 1, 0, 0.5)
        _BackgroundColor ("Background Color", Color) = (0, 0, 0, 0)
        _GridSpacing ("Grid Spacing", Float) = 1.0
        _LineWidth ("Line Width", Float) = 0.05
    }
    
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };
            
            float4 _GridColor;
            float4 _BackgroundColor;
            float _GridSpacing;
            float _LineWidth;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // Grid-Berechnung in World-Space
                float2 pos = i.worldPos.xz;
                
                // Modulo für Grid-Linien
                float2 grid = abs(frac(pos / _GridSpacing - 0.5) - 0.5) / fwidth(pos / _GridSpacing);
                float gridLine = min(grid.x, grid.y);  // Umbenennung von 'line' zu 'gridLine'
                
                // Linienstärke anwenden
                float gridMask = 1.0 - min(gridLine / _LineWidth, 1.0);
                
                // Farben mischen
                fixed4 color = lerp(_BackgroundColor, _GridColor, gridMask);
                
                return color;
            }
            ENDCG
        }
    }
}