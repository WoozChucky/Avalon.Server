#version 450

layout (location = 0) in vec3 inPosition;
layout (location = 1) in vec3 inNormal;
layout (location = 2) in vec2 inTexCoord;

layout (set = 1, binding = 0) uniform CameraUBO {
    mat4 projection;
    mat4 view;
} camera;

layout (set = 1, binding = 1) uniform ObjectUBO {
    mat4 model;
} object;

layout (location = 0) out vec3 fragWorldPos;
layout (location = 1) out vec3 fragNormal;
layout (location = 2) out vec2 fragTexCoord;

void main() {
    vec4 worldPos = object.model * vec4(inPosition, 1.0);
    fragWorldPos = worldPos.xyz;
    fragNormal = normalize(mat3(object.model) * inNormal);
    fragTexCoord = inTexCoord;
    gl_Position = camera.projection * camera.view * worldPos;
}
