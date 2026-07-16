#version 330

in vec3 fragPosition;
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec4 fragColor;
in vec4 fragPositionLight;

uniform sampler2D texture0;   // albedo textura (bila 1x1 kdyz model zadnou nema)
uniform vec4 colDiffuse;      // barva materialu - raylib ji plni z MaterialMap.Color
uniform sampler2D shadowMap;  // stinova mapa (hloubkova textura)

uniform vec3 viewPos;         // pozice kamery (odlesky)
uniform vec3 sunDir;          // smer slunce KE scene, normalizovany
uniform vec3 sunColor;        // uz vynasobena silou slunce
uniform vec3 ambientColor;
uniform float specStrength;   // sila odlesku 0..1

out vec4 finalColor;

void main()
{
    vec4 albedo = texture(texture0, fragTexCoord) * colDiffuse * fragColor;

    vec3 N = normalize(fragNormal);
    vec3 L = normalize(-sunDir);

    // difuzni slozka (Lambert)
    float ndl = max(dot(N, L), 0.0);

    // odlesk (Blinn-Phong) - jen na osvetlenych plochach
    vec3 V = normalize(viewPos - fragPosition);
    vec3 H = normalize(L + V);
    float spec = pow(max(dot(N, H), 0.0), 32.0) * specStrength * step(0.001, ndl);

    // Vypocet stinu
    vec3 shadowIndex = (fragPositionLight.xyz / fragPositionLight.w) * 0.5 + 0.5;
    float shadow = 0.0;

    if (shadowIndex.x >= 0.0 && shadowIndex.x <= 1.0 &&
        shadowIndex.y >= 0.0 && shadowIndex.y <= 1.0 &&
        shadowIndex.z >= 0.0 && shadowIndex.z <= 1.0)
    {
        float bias = max(0.002 * (1.0 - dot(N, L)), 0.0005);
        vec2 texelSize = vec2(1.0 / 2048.0);
        
        for (int x = -1; x <= 1; ++x)
        {
            for (int y = -1; y <= 1; ++y)
            {
                float pcfDepth = texture(shadowMap, shadowIndex.xy + vec2(x, y) * texelSize).r;
                shadow += (shadowIndex.z - bias > pcfDepth) ? 0.0 : 1.0;
            }
        }
        shadow /= 9.0;
    }
    else
    {
        shadow = 1.0;
    }

    vec3 lit = albedo.rgb * (ambientColor + sunColor * ndl * shadow) + sunColor * spec * shadow;
    finalColor = vec4(lit, albedo.a);
}
