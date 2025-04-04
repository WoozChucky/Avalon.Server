#version 450

// All textures for the fragment shader go in set=2
layout (set = 2, binding = 0) uniform sampler2D gDepth;
layout (set = 2, binding = 1) uniform sampler2D gNormal;
layout (set = 2, binding = 2) uniform sampler2D noiseTexture;

// All uniform buffers for the fragment shader go in set=3
layout (set = 3, binding = 0) uniform SSAOSettings {
    vec3 samples[64];
    int kernelSize;
    float radius;
    float bias;
} uSSAO;

layout (set = 3, binding = 1) uniform Proj {
    mat4 projection;
} proj;

layout (location = 0) in vec2 fragTexCoord;

layout (location = 0) out float occlusionFactor;

// Reconstruct position from depth
vec3 reconstructPosition(vec2 texCoords, float depth)
{
    vec4 clipSpace = vec4(texCoords * 2.0 - 1.0, depth, 1.0);
    vec4 viewSpace = inverse(proj.projection) * clipSpace;
    return viewSpace.xyz / viewSpace.w;
}

void main()
{
    float d = texture(gDepth, fragTexCoord).r;
    vec3 fragPos = reconstructPosition(fragTexCoord, d);
    vec3 normal = normalize(texture(gNormal, fragTexCoord).rgb);

    // Sample random vector from noise
    vec3 randomVec = normalize(texture(noiseTexture, fragTexCoord * 4.0).xyz);

    // Build TBN
    vec3 tangent = normalize(randomVec - normal * dot(randomVec, normal));
    vec3 bitangent = cross(normal, tangent);
    mat3 TBN = mat3(tangent, bitangent, normal);

    float occlusion = 0.0;
    for (int i = 0; i < uSSAO.kernelSize; i++)
    {
        vec3 samplePos = fragPos + (TBN * uSSAO.samples[i]) * uSSAO.radius;
        vec4 sampleScreen = proj.projection * vec4(samplePos, 1.0);
        sampleScreen.xyz /= sampleScreen.w;
        sampleScreen.xy = sampleScreen.xy * 0.5 + 0.5;

        float sampleDepth = texture(gDepth, sampleScreen.xy).r;
        float rangeCheck = smoothstep(0.0, 1.0, uSSAO.radius / abs(fragPos.z - sampleDepth));

        occlusion += (sampleDepth >= samplePos.z + uSSAO.bias ? 1.0 : 0.0) * rangeCheck;
    }

    occlusionFactor = 1.0 - (occlusion / uSSAO.kernelSize);
}
