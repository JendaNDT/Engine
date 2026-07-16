#version 330
// macOS umi jen GL 3.3 Core -> #version 330 je strop i minimum (DEEP_RESEARCH 4.3)

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
in vec4 vertexColor;

// Standardni raylib nazvy -> raylib je binduje automaticky pri LoadShader
uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matNormal;

uniform mat4 mvpLight;

out vec3 fragPosition;
out vec2 fragTexCoord;
out vec3 fragNormal;
out vec4 fragColor;
out vec4 fragPositionLight;

void main()
{
    fragPosition = vec3(matModel * vec4(vertexPosition, 1.0));
    fragTexCoord = vertexTexCoord;
    fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
    fragColor = vertexColor;
    fragPositionLight = mvpLight * vec4(fragPosition, 1.0);

    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
