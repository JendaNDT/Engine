#version 330

in vec2 fragTexCoord;

uniform sampler2D texture0;

uniform float bloomIntensity;
uniform float bloomThreshold;
uniform float vignettePower;

out vec4 finalColor;

void main()
{
    vec4 baseColor = texture(texture0, fragTexCoord);
    
    // 1. Box blur s jasovým filtrem pro Bloom
    vec3 glowColor = vec3(0.0);
    float totalWeight = 0.0;
    
    vec2 texelSize = vec2(1.8) / textureSize(texture0, 0);
    
    for (int x = -2; x <= 2; x++)
    {
        for (int y = -2; y <= 2; y++)
        {
            vec2 offset = vec2(float(x), float(y)) * texelSize;
            vec3 sampleColor = texture(texture0, fragTexCoord + offset).rgb;
            
            float luminance = dot(sampleColor, vec3(0.2126, 0.7152, 0.0722));
            if (luminance > bloomThreshold)
            {
                float weight = 1.0 - (length(vec2(x, y)) / 3.0);
                if (weight > 0.0)
                {
                    glowColor += sampleColor * weight;
                    totalWeight += weight;
                }
            }
        }
    }
    
    if (totalWeight > 0.0)
    {
        glowColor /= totalWeight;
    }
    
    vec3 result = baseColor.rgb + glowColor * bloomIntensity;
    
    // 2. Vignette (zatmavení okrajů)
    vec2 uv = fragTexCoord - vec2(0.5);
    float dist = length(uv);
    float vignette = smoothstep(0.8, 0.5 - vignettePower * 0.45, dist);
    
    result *= vignette;
    
    finalColor = vec4(result, baseColor.a);
}
