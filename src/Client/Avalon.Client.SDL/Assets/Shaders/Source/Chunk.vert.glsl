#version 450

// Uniform buffer object for transformations
layout (set = 1, binding = 0) uniform UniformBlock {
    mat4 Transform;         // model transformations (identity for chunks)
    mat4 ProjectionMatrix;  // ProjectionMatrix x ViewMatrix
    vec3 ChunkOffset;       // World position of the chunk (hence float)
};

// Input attributes
layout (location = 0) in ivec3 inLocalPosition; // Local position within the chunk
layout (location = 1) in vec3 inNormal;         // Vertex normal
layout (location = 2) in vec2 inTexCoord;       // Per-face UV (0-1)
layout (location = 3) in ivec2 inBlockCoords;   // Block X, Y in texture atlas

// Output attributes
layout (location = 0) out vec2 outTexCoord;
layout (location = 1) out vec3 outNormal;

const float atlasWidth = 1024.0;
const float atlasHeight = 512.0;
const float blockSize = 16.0;

void main() {
    // Compute normalized UV size per block
    vec2 uvSize = vec2(blockSize / atlasWidth, blockSize / atlasHeight);

    // Compute base UV offset in the atlas
    vec2 baseUV = vec2(inBlockCoords) * uvSize;

    // Compute final texture coordinate
    outTexCoord = baseUV + (inTexCoord * uvSize);

    // Transform the normal to world space
    outNormal = mat3(Transform) * inNormal;

    // Compute final world position
    vec3 worldPosition = vec3(inLocalPosition) + ChunkOffset;

    // Apply transformations
    gl_Position = ProjectionMatrix * vec4(worldPosition, 1.0);
}
