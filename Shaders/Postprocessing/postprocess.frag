#version 330 core

uniform sampler2D frameBufferTexture;
uniform sampler2D depth;
uniform sampler2D gPosition;
uniform sampler2D gNormal;
uniform sampler2D texNoise;

uniform bool ACES = true;
uniform bool showDepth = false;

uniform vec3 samples[64];
uniform mat4 projection;

in vec2 UV;
out vec4 fragColor;

vec3 ACESFilm( vec3 x) {
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return clamp((x*(a*x+b))/(x*(c*x+d)+e), 0.0, 1.0);
}

float near = 0.1; 
float far  = 100.0; 

float LinearizeDepth(float depth) 
{
    float z = depth * 2.0 - 1.0; // back to NDC 
    return (2.0 * near * far) / (far + near - z * (far - near));	
}

uniform bool ssaoOnOff = true;
uniform float SSAOpower = 0.3;
uniform float radius = 0.3;
int kernelSize = 64;
float bias = 0.015;

void main()
{
    vec3 color = texture(frameBufferTexture, UV).rgb;
    if (ACES && !showDepth) color = ACESFilm(color);
    if (showDepth) color = vec3(LinearizeDepth(texture(depth, UV).r) / far);

    if (ssaoOnOff)
    {
        vec2 noiseScale = vec2(textureSize(gNormal, 0).x / 4, textureSize(gNormal, 0).y / 4);

        vec3 fragPos = texture(gPosition, UV).xyz;
        vec3 norm = texture(gNormal, UV).rgb;
        vec3 randomVec = normalize(texture(texNoise, UV * noiseScale).xyz);

        vec3 tangent = normalize(randomVec - norm * dot(randomVec, norm));
        vec3 bitangent = cross(norm, tangent);
        mat3 TBN = mat3(tangent, bitangent, norm);

        float occlusion = 0.0;
        for (int i = 0; i < kernelSize; i++)
        {
            vec3 samplePos = TBN * samples[i];
            samplePos = fragPos + samplePos * radius;

            vec4 offset = vec4(samplePos, 1.0);
            offset = offset * projection;
            offset.xyz /= offset.w;
            offset.xyz = offset.xyz * 0.5 + 0.5;

            float sampleDepth = texture(gPosition, offset.xy).z;

            float rangeCheck = smoothstep(0.0, 1.0, radius / abs(fragPos.z - sampleDepth));
            occlusion += (sampleDepth >= samplePos.z + bias ? 1.0 : 0.0) * rangeCheck;
        }

        occlusion = 1.0 - (occlusion / kernelSize);
        occlusion = pow(occlusion, SSAOpower);
        fragColor = vec4(color, occlusion);
    }
    else fragColor = vec4(color, 1);
}