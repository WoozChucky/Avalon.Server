#version 450

/////////////////////////////////////////
// G-Buffer Inputs (set=2, bindings 0..3)
/////////////////////////////////////////
layout (set = 2, binding = 0) uniform sampler2D gAlbedo;
layout (set = 2, binding = 1) uniform sampler2D gNormal;
layout (set = 2, binding = 2) uniform sampler2D gDepth;
layout (set = 2, binding = 3) uniform sampler2D gSpecular;

/////////////////////////////////////////
// SSAO (Ambient Occlusion) (set=2, binding=4)
/////////////////////////////////////////
layout (set = 2, binding = 4) uniform sampler2D aoTexture;

/////////////////////////////////////////
// Shadow Map (set=2, binding=5)
/////////////////////////////////////////
layout (set = 2, binding = 5) uniform sampler2D shadowMap;

/////////////////////////////////////////
// Output
/////////////////////////////////////////
layout (location = 0) out vec4 outColor;

/////////////////////////////////////////
// Camera & Light Data (set=3)
/////////////////////////////////////////
//  - set=3, binding=0 : Camera data
//  - set=3, binding=1 : Main light data
//  - set=3, binding=2 : Light-space (for shadow map)
/////////////////////////////////////////

// For reconstructing positions (if using a depth-based approach)
layout (set = 3, binding = 0) uniform CameraData {
    mat4 invProjection;
    mat4 invView;
} cameraUBO;

// For basic directional light data
layout (set = 3, binding = 1) uniform LightUBO {
    vec3 lightDir;    // e.g. (0.5, -1.0, 0.1)
    vec3 lightColor;  // e.g. (1.0, 0.95, 0.8)
} lightUBO;

// For shadow map sampling
layout (set = 3, binding = 2) uniform LightSpaceUBO {
    mat4 lightSpaceMatrix;
    float shadowBias; // e.g. 0.005
} shadowData;

/////////////////////////////////////////
// Interpolants from vertex shader
/////////////////////////////////////////
layout (location = 0) in vec2 fragTexCoord;

/////////////////////////////////////////
// Helper: Reconstruct World Position
/////////////////////////////////////////
vec3 reconstructPosition(vec2 uv, float depth)
{
    // Reconstruct from [0..1] depth -> clip space -> view space -> world space
    vec4 clipPos = vec4(uv * 2.0 - 1.0, depth, 1.0);
    vec4 viewPos = cameraUBO.invProjection * clipPos;
    viewPos.xyz /= viewPos.w;          // now in view space

    vec4 worldPos = cameraUBO.invView * viewPos;
    return worldPos.xyz;               // final world-space position
}

/////////////////////////////////////////
// Helper: Calculate Shadow Factor
/////////////////////////////////////////
float CalculateShadowFactor(vec3 worldPos)
{
    // 1) Transform position from world -> light clip space
    vec4 lightClipPos = shadowData.lightSpaceMatrix * vec4(worldPos, 1.0);

    // 2) Homogeneous divide
    vec3 projCoords = lightClipPos.xyz / lightClipPos.w;

    // 3) Transform from [-1..1] to [0..1]
    projCoords = projCoords * 0.5 + 0.5;

    // If out of shadow map range, treat as unshadowed
    if (projCoords.x < 0.0 || projCoords.x > 1.0 ||
    projCoords.y < 0.0 || projCoords.y > 1.0 ||
    projCoords.z < 0.0 || projCoords.z > 1.0)
    {
        return 1.0;
    }

    // 4) Sample depth stored in shadow map
    float closestDepth = texture(shadowMap, projCoords.xy).r;

    // 5) Current depth vs. bias
    float currentDepth = projCoords.z;
    float bias = shadowData.shadowBias;

    // If geometry is behind stored depth -> in shadow
    // We'll do 1.0 for lit, 0.0 for shadow. So invert the result:
    float shadow = (currentDepth - bias) > closestDepth ? 1.0 : 0.0;
    return 1.0 - shadow; // 1 = lit, 0 = shadowed
}

/////////////////////////////////////////
// Main Lighting
/////////////////////////////////////////
void main()
{
    /////////////////////////////////////////
    // 1) Retrieve G-Buffer data
    /////////////////////////////////////////
    vec3 albedo = texture(gAlbedo, fragTexCoord).rgb;
    vec3 normalEnc = texture(gNormal, fragTexCoord).rgb;
    float depth = texture(gDepth, fragTexCoord).r;
    float specular = texture(gSpecular, fragTexCoord).r;

    // decode normals from [0..1]
    vec3 normal = normalize(normalEnc * 2.0 - 1.0);

    // Reconstruct the world position if needed
    vec3 worldPos = reconstructPosition(fragTexCoord, depth);

    /////////////////////////////////////////
    // 2) Basic Phong lighting (Directional)
    /////////////////////////////////////////
    // Light direction
    vec3 lightDir = normalize(lightUBO.lightDir);
    // If your lightDir is the direction from the scene to the light,
    // you might need to do -lightDir or so. Adjust as needed.

    // View direction (if camera is at origin, or supply camera pos)
    vec3 viewDir = normalize(-worldPos);

    // Diffuse term
    float diff = max(dot(normal, -lightDir), 0.0);
    vec3 diffuse = albedo * diff * lightUBO.lightColor;

    // Specular
    vec3 reflectDir = reflect(lightDir, normal);
    float specFactor = pow(max(dot(viewDir, reflectDir), 0.0), 16.0) * specular;
    vec3 specCol = vec3(1.0) * specFactor; // tinted if you prefer

    // Ambient
    vec3 ambient = albedo * 0.2; // simple base ambient

    /////////////////////////////////////////
    // 3) Sample AO
    /////////////////////////////////////////
    float ao = texture(aoTexture, fragTexCoord).r;

    // Multiply AO into ambient (and optionally diffuse)
    ambient *= ao;
    // diffuse *= ao; // uncomment if you want stronger occlusion on diffuse

    /////////////////////////////////////////
    // 4) Shadow
    /////////////////////////////////////////
    float shadowFactor = CalculateShadowFactor(worldPos);

    /////////////////////////////////////////
    // 5) Final
    /////////////////////////////////////////
    // Shadow factor typically only affects diffuse & specular, not ambient
    diffuse *= shadowFactor;
    specCol *= shadowFactor;

    vec3 finalColor = ambient + diffuse + specCol;
    outColor = vec4(finalColor, 1.0);
}
