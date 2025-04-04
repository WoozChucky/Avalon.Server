#version 450

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec2 inTexCoord;

layout (location = 0) out vec2 fragTexCoord;

void main()
{
    fragTexCoord = inTexCoord;
    gl_Position = vec4(inPosition.xy, 0.0, 1.0);
}
