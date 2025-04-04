#version 450

layout (location = 0) in vec3 InPosition;

layout (set = 1, binding = 0) uniform ShadowPassUBO {
    mat4 LightSpaceMatrix;
} UBO;

void main() {
    gl_Position = UBO.LightSpaceMatrix * vec4(InPosition, 1.0);
}
