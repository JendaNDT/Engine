#version 330

in vec3 fragTexCoord;

uniform sampler2D panorama;

out vec4 finalColor;

const float PI = 3.14159265359;

void main()
{
    vec3 dir = normalize(fragTexCoord);
    
    // Projekce 3D směru na sférické UV souřadnice (Equirectangular)
    float phi = atan(dir.z, dir.x);
    float theta = asin(dir.y);
    
    float u = 1.0 - (phi + PI) / (2.0 * PI);
    float v = (theta + PI / 2.0) / PI;
    
    finalColor = texture(panorama, vec2(u, v));
}
