#version 450

layout (location = 0) in vec3 inPos;     // fullscreen quad positions
layout (location = 1) in vec2 inUV;      // fullscreen quad UVs

layout (location = 0) out vec2 fragUV;   // pass UVs to fragment

void main()
{
    fragUV = inUV;
    gl_Position = vec4(inPos.xy, 0.0, 1.0);
}
