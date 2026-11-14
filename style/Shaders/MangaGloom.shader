// «Мрак» — полноэкранный манга-фильтр, как в рендерах village/renders (village_low.png):
// тон сжат в бумагу / растр / штриховку / тушь, контуры тушью по глубине и нормалям,
// серое растровое небо с белыми полосами ветра, косые штрихи снега, виньетка и зерно.
// Насыщенные цвета (огонь, кровь, вода, оранжевый контур героя) остаются цветными — по style bible.
// Параметры — глобальные _MG_*, их выставляет компонент MangaGloom на камере.
// Без компонента (_MG_Intensity = 0) картинка проходит без изменений.
Shader "Calabash/MangaGloom"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "MangaGloom"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float  _MG_Intensity;
            float4 _MG_Tone;     // x чёрная точка, y белая точка, z гамма (>1 — мрачнее), w тон бумаги
            float4 _MG_Dots;     // x шаг растра, px@1080p, y сила растра, z тон, ниже которого растр, w тон точки
            float4 _MG_Hatch;    // x шаг штриховки, y сила, z тон начала штриховки, w тон начала перекрёстной
            float4 _MG_Ink;      // x толщина линии, px@1080p, y порог глубины, z порог нормалей, w тон туши
            float4 _MG_InkFade;  // x начало ослабления, м, y конец, м, z сила линий вдали, w неровность линии
            float4 _MG_Fog;      // x начало тумана, м, y конец, z во сколько темнеет небо (множитель), w сила полос неба
            float4 _MG_Streaks;  // x число полос неба, y толщина, px, z тон полос, w волнистость
            float4 _MG_Snow;     // x сила штрихов снега, y наклон, рад, z скорость, w плотность
            float4 _MG_Extra;    // x виньетка, y зерно, z порог насыщенности «оставить цвет», w кадр

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), u.x),
                            lerp(Hash12(i + float2(0, 1)), Hash12(i + float2(1, 1)), u.x), u.y);
            }

            bool IsFar(float raw)
            {
            #if UNITY_REVERSED_Z
                return raw <= 1e-6;
            #else
                return raw >= 1.0 - 1e-6;
            #endif
            }

            float EyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return IsFar(raw) ? 1e5 : LinearEyeDepth(raw, _ZBufferParams);
            }

            // контур тушью: разрыв глубины или излом нормали между соседями
            float InkEdge(float2 uv, float eye, float3 n, float widthPx)
            {
                float2 o = widthPx / _ScreenParams.xy;
                float2 offs[4] = { float2(o.x, 0), float2(-o.x, 0), float2(0, o.y), float2(0, -o.y) };
                float edge = 0;
                [unroll] for (int i = 0; i < 4; i++)
                {
                    float2 uv2 = uv + offs[i];
                    float e2 = EyeDepth(uv2);
                    float dz = abs(e2 - eye) / max(min(e2, eye), 0.1);
                    edge = max(edge, saturate((dz - _MG_Ink.y) / max(_MG_Ink.y, 1e-3)));
                    if (eye < 1e4 && e2 < 1e4)
                    {
                        float3 n2 = SampleSceneNormals(uv2);
                        float dn = 1.0 - saturate(dot(n, n2));
                        edge = max(edge, saturate((dn - _MG_Ink.z) / max(_MG_Ink.z, 1e-3) * 2.0));
                    }
                }
                return edge;
            }

            // белые волнистые полосы ветра на небе — привязаны к направлению взгляда, не к экрану
            float SkyStreaks(float2 uv)
            {
                float3 posWS = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 dir = normalize(posWS - _WorldSpaceCameraPos);
                float az = atan2(dir.x, dir.z);              // −π..π
                // высота на цилиндре вокруг камеры: при ровном взгляде полосы горизонтальные, а не дуги
                float el = dir.y / max(length(dir.xz), 0.05);
                float bands = _MG_Streaks.x;
                float v = el * bands + sin(az * 3.0) * _MG_Streaks.w + sin(az * 7.0 + 1.3) * _MG_Streaks.w * 0.4;
                float band = floor(v);
                float f = frac(v) - 0.5 - (Hash12(float2(band, 3.1)) - 0.5) * 0.5;
                float px = abs(f) / max(fwidth(v), 1e-5);
                float w = _MG_Streaks.y * _ScreenParams.y / 1080.0;
                float stroke = 1.0 - smoothstep(w * 0.5, w * 0.5 + 1.2, px);
                // полосы рваные: по азимуту — отрезки разной длины
                float segs = 6.0 + floor(Hash12(float2(band, 7.7)) * 5.0);
                float s = frac(az / (2.0 * PI) * segs + Hash12(float2(band, 1.9)));
                float keep = smoothstep(0.0, 0.04, s) * (1.0 - smoothstep(0.5, 0.62, s));
                float fade = smoothstep(0.03, 0.15, el) * (1.0 - smoothstep(1.6, 2.4, el));
                return stroke * keep * fade * step(0.25, Hash12(float2(band, 5.3)));
            }

            // косые штрихи летящего снега, экранные
            float SnowStreaks(float2 px)
            {
                float a = _MG_Snow.y;
                float2 d = float2(sin(a), -cos(a));       // вдоль штриха: вниз и вправо
                float2 p = float2(dot(px, float2(-d.y, d.x)), dot(px, d));
                float t = _Time.y * _MG_Snow.z;
                float result = 0;
                [unroll] for (int layer = 0; layer < 2; layer++)
                {
                    float spacing = layer == 0 ? 23.0 : 37.0;
                    float len = layer == 0 ? 28.0 : 46.0;
                    float col = floor(p.x / spacing);
                    float h = Hash12(float2(col, layer * 17.0));
                    float x = abs(frac(p.x / spacing) - 0.2 - 0.6 * h) * spacing;
                    float yy = p.y - t * (0.7 + h) * (layer == 0 ? 1.0 : 1.6) + h * 500.0;
                    float seg = floor(yy / (len * 3.0));
                    float fy = frac(yy / (len * 3.0)) * 3.0;
                    float on = step(Hash12(float2(col, seg + layer * 31.0)), _MG_Snow.w);
                    float dash = smoothstep(0.0, 0.15, fy) * (1.0 - smoothstep(0.8, 1.0, fy));
                    float wpx = layer == 0 ? 0.6 : 0.9;
                    result = max(result, on * dash * (1.0 - smoothstep(wpx, wpx + 0.9, x)));
                }
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                if (_MG_Intensity <= 0.001) return src;

                float k = _ScreenParams.y / 1080.0;            // узоры одинаковые на любом разрешении
                float2 px = uv * _ScreenParams.xy;
                float2 pk = px / k;

                float3 c = LinearToSRGB(saturate(src.rgb));
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                float sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
                float keepColor = smoothstep(_MG_Extra.z, _MG_Extra.z + 0.1, sat);

                float raw = SampleSceneDepth(uv);
                bool sky = IsFar(raw);
                float eye = sky ? 1e5 : LinearEyeDepth(raw, _ZBufferParams);
                float3 n = sky ? float3(0, 1, 0) : SampleSceneNormals(uv);

                // 1. тон: уровни и гамма, даль и небо уходят в серый
                float t = saturate((lum - _MG_Tone.x) / max(_MG_Tone.y - _MG_Tone.x, 1e-3));
                t = pow(t, _MG_Tone.z);
                float fogMix = sky ? 1.0 : saturate((eye - _MG_Fog.x) / max(_MG_Fog.y - _MG_Fog.x, 1e-3));
                t = lerp(t, t * _MG_Fog.z, fogMix);
                t = min(t, _MG_Tone.w);

                // 2. растр: точки под 45°, их площадь держит средний тон
                float g = 0.70710678;
                float2 q = float2(pk.x * g - pk.y * g, pk.x * g + pk.y * g) / _MG_Dots.x;
                float d = length(frac(q) - 0.5);
                float inkDot = _MG_Dots.w;
                float cov = saturate((1.0 - t) / (1.0 - inkDot));
                cov *= smoothstep(_MG_Dots.z, _MG_Dots.z - 0.08, t);
                float r = sqrt(cov / PI);
                float aa = 0.7 / _MG_Dots.x;
                float dotMask = 1.0 - smoothstep(r - aa, r + aa, d);
                float screenTone = lerp(_MG_Tone.w, inkDot, dotMask);
                float v = lerp(t, screenTone, _MG_Dots.y);

                // 3. штриховка тёмного: косые линии, ещё темнее — перекрёстные
                float hp = _MG_Hatch.x;
                float h1 = abs(frac((pk.x + pk.y) / hp) - 0.5) * 2.0;    // 0 на линии
                float h2 = abs(frac((pk.x - pk.y) / hp + 0.5) - 0.5) * 2.0;
                float haa = 1.4 / hp;
                float w1 = saturate((_MG_Hatch.z - t) / max(_MG_Hatch.z, 1e-3));
                float w2 = saturate((_MG_Hatch.w - t) / max(_MG_Hatch.w, 1e-3));
                float line1 = 1.0 - smoothstep(w1 - haa, w1 + haa, h1);
                float line2 = 1.0 - smoothstep(w2 - haa, w2 + haa, h2);
                float hatch = max(line1 * step(0.001, w1), line2 * step(0.001, w2));
                v = lerp(v, _MG_Ink.w, hatch * _MG_Hatch.y);

                // 4. небо: полосы ветра
                if (sky) v = lerp(v, _MG_Streaks.z, SkyStreaks(uv) * _MG_Fog.w);

                // 5. контур тушью, вдали тоньше и бледнее
                float wob = lerp(1.0, 0.55 + 0.9 * ValueNoise(pk * 0.08), _MG_InkFade.w);
                float edge = InkEdge(uv, eye, n, max(_MG_Ink.x * k * wob, 0.5));
                float near = min(eye, EyeDepth(uv + float2(_MG_Ink.x * k, 0) / _ScreenParams.xy));
                float far01 = saturate((near - _MG_InkFade.x) / max(_MG_InkFade.y - _MG_InkFade.x, 1e-3));
                edge *= lerp(1.0, _MG_InkFade.z, far01);
                v = lerp(v, _MG_Ink.w, edge * (1.0 - keepColor));

                // 6. снег, виньетка, зерно
                v = lerp(v, 0.97, SnowStreaks(pk) * _MG_Snow.x);
                float2 vc = (uv - 0.5) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                v *= 1.0 - _MG_Extra.x * smoothstep(0.35, 1.05, length(vc));
                v += (Hash12(px + _MG_Extra.w * 13.17) - 0.5) * _MG_Extra.y;
                v = saturate(v);

                float3 gray = v.xxx;
                float3 keep = c * (1.0 - _MG_Extra.x * smoothstep(0.35, 1.05, length(vc)));
                float3 outSrgb = lerp(gray, keep, keepColor);
                float3 result = lerp(c, outSrgb, _MG_Intensity);
                return half4(SRGBToLinear(result), src.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
