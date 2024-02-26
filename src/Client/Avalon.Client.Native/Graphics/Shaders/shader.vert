#version 410

layout (location = 0) in vec2 position;
layout (location = 1) in vec4 color;
layout (location = 2) in vec2 textCoord;

out vec4 fragmentColor;
out vec2 fragmentPosition;
out vec2 fragmentTextCoord;

uniform mat4 projection;

void main() {
    // Set x and y to the position of the vertex
    gl_Position.xy = (projection * vec4(position, 0.0, 1.0)).xy ;
    gl_Position.z = 0.0;
    gl_Position.w = 1.0; // indicates that it is normalized

    // Set the color of the vertex
    fragmentColor = color;
    fragmentPosition = position;
    fragmentTextCoord = vec2(textCoord.x, textCoord.y);
}
