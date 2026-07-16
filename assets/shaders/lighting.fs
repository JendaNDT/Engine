#version 330

in vec3 fragPosition;
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec4 fragColor;

uniform sampler2D texture0;   // albedo textura (bila 1x1 kdyz model zadnou nema)
uniform vec4 colDiffuse;      // barva materialu - raylib ji plni z MaterialMap.Color

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

    vec3 lit = albedo.rgb * (ambientColor + sunColor * ndl) + sunColor * spec;
    finalColor = vec4(lit, albedo.a);
}
