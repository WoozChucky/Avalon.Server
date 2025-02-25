#version 450

// Textures
layout(set = 2, binding = 0) uniform sampler2D BlockTexture;

// Input attributes
layout(location = 0) in vec2 FragTexCoord;
layout(location = 1) in vec3 FragNormal;

// Output color
layout(location = 0) out vec4 OutColor;

void main() {
    vec4 texColor = texture(BlockTexture, FragTexCoord);
    OutColor = vec4(texColor.rgb, 1.0); // Semi-transparent water
}
