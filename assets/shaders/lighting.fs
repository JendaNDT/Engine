#version 330

in vec3 fragPosition;
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec4 fragColor;
in vec4 fragPositionLight;

uniform sampler2D texture0;   // albedo
uniform sampler2D texture2;   // normal map (Raylib MaterialMapIndex.Normal)
uniform sampler2D texture3;   // roughness map (Raylib MaterialMapIndex.Roughness)
uniform vec4 colDiffuse;
uniform sampler2D shadowMap;

uniform vec3 viewPos;
uniform vec3 sunDir;
uniform vec3 sunColor;
uniform vec3 ambientColor;
uniform float specStrength;

// PBR uniformy
uniform int hasNormalMap = 0;
uniform int hasMetallicRoughnessMap = 0;
uniform float metallicFactor = 0.0;
uniform float roughnessFactor = 1.0;

out vec4 finalColor;

// Vypocet TBN matice ve fragment shaderu
vec3 getNormalFromMap(sampler2D normalMap, vec2 texCoords, vec3 normal, vec3 position)
{
    vec3 tangentNormal = texture(normalMap, texCoords).xyz * 2.0 - 1.0;

    vec3 Q1  = dFdx(position);
    vec3 Q2  = dFdy(position);
    vec2 st1 = dFdx(texCoords);
    vec2 st2 = dFdy(texCoords);

    vec3 N   = normalize(normal);
    vec3 T   = normalize(Q1*st2.t - Q2*st1.t);
    vec3 B   = -normalize(cross(N, T));
    mat3 TBN = mat3(T, B, N);

    return normalize(TBN * tangentNormal);
}

void main()
{
    vec4 albedo = texture(texture0, fragTexCoord) * colDiffuse * fragColor;

    vec3 N = normalize(fragNormal);
    if (hasNormalMap == 1)
    {
        N = getNormalFromMap(texture2, fragTexCoord, fragNormal, fragPosition);
    }

    vec3 V = normalize(viewPos - fragPosition);
    vec3 L = normalize(-sunDir);
    vec3 H = normalize(L + V);

    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);

    // Stiny
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

    // PBR parametry
    float metallic = metallicFactor;
    float roughness = roughnessFactor;
    if (hasMetallicRoughnessMap == 1)
    {
        // Gltf standard: roughness v G kanalu, metallic v B kanalu
        vec4 mr = texture(texture3, fragTexCoord);
        roughness *= mr.g;
        metallic *= mr.b;
    }
    roughness = clamp(roughness, 0.05, 1.0);

    // Cook-Torrance Specular BRDF
    vec3 F0 = vec3(0.04);
    F0 = mix(F0, albedo.rgb, metallic);
    vec3 F = F0 + (1.0 - F0) * pow(clamp(1.0 - max(dot(H, V), 0.0), 0.0, 1.0), 5.0);

    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float NdotH = max(dot(N, H), 0.0);
    float NdotH2 = NdotH * NdotH;
    float denom = (NdotH2 * (alpha2 - 1.0) + 1.0);
    float D = alpha2 / (3.14159 * denom * denom);

    float k = (alpha + 1.0) * (alpha + 1.0) / 8.0;
    float g1 = NdotV / (NdotV * (1.0 - k) + k);
    float g2 = NdotL / (NdotL * (1.0 - k) + k);
    float G = g1 * g2;

    vec3 nominator = D * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    vec3 specular = (nominator / denominator) * specStrength;

    vec3 kS = F;
    vec3 kD = vec3(1.0) - kS;
    kD *= 1.0 - metallic;

    vec3 diffuseLight = albedo.rgb * kD * (ambientColor + sunColor * NdotL * shadow);
    vec3 specularLight = sunColor * specular * NdotL * shadow;

    finalColor = vec4(diffuseLight + specularLight, albedo.a);
}
