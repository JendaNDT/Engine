#version 330

in vec3 vertexPosition;

uniform mat4 matProjection;
uniform mat4 matView;

out vec3 fragTexCoord;

void main()
{
    fragTexCoord = vertexPosition;
    
    // Odstraníme translaci z matice pohledu
    mat4 viewNoTranslation = matView;
    viewNoTranslation[3][0] = 0.0;
    viewNoTranslation[3][1] = 0.0;
    viewNoTranslation[3][2] = 0.0;

    vec4 pos = matProjection * viewNoTranslation * vec4(vertexPosition, 1.0);
    
    // Zápis z na w zajistí depth = 1.0 (nejzazší pozadí)
    gl_Position = pos.xyww;
}
