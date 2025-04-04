#version 450

layout (location = 0) in vec3 inWorldPos;
layout (location = 1) in vec3 inNormal;
layout (location = 2) in vec2 inTexCoord;

layout (set = 2, binding = 0) uniform sampler2D albedoTexture;
layout (set = 2, binding = 1) uniform sampler2D specularTexture;

layout (location = 0) out vec4 outAlbedo;
layout (location = 1) out vec4 outNormals;
layout (location = 2) out vec4 outSpecular;
layout (location = 3) out float outDepth;

void main() {
    // Fetch albedo and specular data from textures
    vec3 albedo = texture(albedoTexture, inTexCoord).rgb;
    float specular = texture(specularTexture, inTexCoord).r;

    // Normalize normal for accurate lighting
    vec3 normal = normalize(inNormal);

    // Store outputs in respective G-Buffer attachments
    outAlbedo = vec4(albedo, 1.0);
    outNormals = vec4(normal * 0.5 + 0.5, 1.0); // Convert to [0,1] range
    outSpecular = vec4(specular, 1.0, 1.0, 1.0); // Specular intensity & shininess
    outDepth = gl_FragCoord.z; // Store depth for SSAO and lighting
}
