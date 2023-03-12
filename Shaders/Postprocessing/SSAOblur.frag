#version 330 core

uniform sampler2D frameBufferTexture;

in vec2 UV;
layout(location = 3) out vec4 blurao;

void main()
{
    vec2 offset;
    vec2 texelSize = 1.0 / vec2(textureSize(frameBufferTexture, 0));
    float result = 0.0;
    for (int x = -2; x < 2; ++x) 
    {
        for (int y = -2; y < 2; ++y) 
        {
            offset = vec2(float(x) * texelSize.x, float(y) * texelSize.y);
            result += texture(frameBufferTexture, UV + offset).a;
        }
    }
    float blur = result / (4.0 * 4.0);
    
    blurao = vec4(blur);
}